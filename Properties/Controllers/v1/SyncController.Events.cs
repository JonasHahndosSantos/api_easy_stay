using System.Data;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiEasyStay.Properties.Data;
using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApiEasyStay.Properties.Controllers.v1;

public sealed partial class SyncController
{
    [HttpPost("push")]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Push(
        [FromBody] SyncPushRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.EspacoId == Guid.Empty || request.DispositivoId == Guid.Empty)
        {
            return BadRequest(new { message = "Hospedagem e dispositivo são obrigatórios." });
        }
        var authorizationError = await ValidateAccessAsync(
            request.EspacoId,
            cancellationToken);
        if (authorizationError is not null) return authorizationError;
        var deviceError = await ValidateDeviceAsync(request.DispositivoId, cancellationToken);
        if (deviceError is not null) return deviceError;
        if (request.Eventos is null || request.Eventos.Count > MaxBatchSize)
        {
            return BadRequest(new { message = $"Envie no maximo {MaxBatchSize} eventos por vez." });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await LockWorkspaceAsync(request.EspacoId, cancellationToken);
            var workspace = await _context.EspacosSincronizacao
                .FirstAsync(x => x.Id == request.EspacoId, cancellationToken);
            var legacyComplete = workspace.DispositivoCargaInicialId is null &&
                await _context.Sincronizacoes.AnyAsync(x => x.EspacoId == request.EspacoId, cancellationToken);
            if (!workspace.CargaInicialConcluida &&
                !legacyComplete &&
                workspace.DispositivoCargaInicialId != request.DispositivoId)
            {
                return Conflict(new { message = "A primeira carga deve ser concluída no dispositivo que a iniciou." });
            }
            var response = new SyncPushResponseDto();
            var incomingIds = request.Eventos.Select(x => x.Id).ToList();
            var knownEventIds = await _context.Sincronizacoes
                .AsNoTracking()
                .Where(x => x.EspacoId == request.EspacoId && incomingIds.Contains(x.EventoId))
                .Select(x => x.EventoId)
                .ToHashSetAsync(cancellationToken);
            var reservationState = request.Eventos.Any(x => string.Equals(x.Entidade, "reservas", StringComparison.OrdinalIgnoreCase))
                ? await LoadReservationState(request.EspacoId, cancellationToken) : new Dictionary<Guid, SincronizacaoEntity>();
            var customerState = request.Eventos.Any(x => string.Equals(x.Entidade, "clientes", StringComparison.OrdinalIgnoreCase))
                ? await LoadCustomerState(request.EspacoId, cancellationToken) : new Dictionary<Guid, SincronizacaoEntity>();
            var userState = request.Eventos.Any(x => string.Equals(x.Entidade, "usuarios", StringComparison.OrdinalIgnoreCase))
                ? await LoadEntityState(request.EspacoId, "usuarios", cancellationToken) : new Dictionary<Guid, SincronizacaoEntity>();
            var latestTimestamp = await _context.Sincronizacoes
                .Where(x => x.EspacoId == request.EspacoId)
                .MaxAsync(x => (DateTime?)x.DataHoraCriado, cancellationToken);
            var timestamp = DateTime.UtcNow;
            if (latestTimestamp >= timestamp) timestamp = latestTimestamp.Value.AddTicks(10);
            timestamp = new DateTime(timestamp.Ticks - timestamp.Ticks % 10, DateTimeKind.Utc);
            var tick = 0L;

            foreach (var incoming in request.Eventos)
            {
                var entityName = incoming.Entidade?.Trim().ToLowerInvariant() ?? "";
                var operation = incoming.Operacao?.Trim().ToLowerInvariant() ?? "";
                if (incoming.Id == Guid.Empty ||
                    (incoming.EntidadeId is null || incoming.EntidadeId == Guid.Empty) ||
                    incoming.Dados.ValueKind != JsonValueKind.Object ||
                    !AllowedEntities.Contains(entityName) ||
                    operation is not ("criar" or "atualizar" or "excluir"))
                {
                    return BadRequest(new { message = "O lote possui um evento inválido." });
                }
                if (TryGetProperty(incoming.Dados, "id", out var payloadId) &&
                    (!Guid.TryParse(payloadId.ToString(), out var parsedId) ||
                     parsedId != incoming.EntidadeId))
                {
                    return BadRequest(new { message = "O ID do evento não corresponde aos dados." });
                }
                if (Encoding.UTF8.GetByteCount(incoming.Dados.GetRawText()) > MaxEventBytes)
                {
                    return BadRequest(new { message = "Um evento excede o tamanho permitido." });
                }
                if (knownEventIds.Contains(incoming.Id))
                {
                    response.EventosAceitos.Add(incoming.Id);
                    continue;
                }

                var prior = await FindCurrentStateAsync(request.EspacoId, entityName, incoming.EntidadeId.Value, cancellationToken);
                var previousVersion = prior is null ? null : await FindLatestVersionAsync(
                    request.EspacoId, entityName, incoming.EntidadeId.Value, cancellationToken);
                var requireVersion = !_environment.IsDevelopment() && workspace.CargaInicialConcluida;
                if (prior is not null &&
                    (incoming.VersaoBase.HasValue && incoming.VersaoBase != previousVersion ||
                     requireVersion && !incoming.VersaoBase.HasValue))
                {
                    response.Conflitos.Add(new SyncConflictDto
                    {
                        FilaId = incoming.Id, Entidade = entityName, EntidadeId = incoming.EntidadeId,
                        Tipo = "registro_desatualizado", Mensagem = "Outro dispositivo alterou este registro. Revise os dados antes de salvar novamente.",
                        DadosLocais = incoming.Dados.Clone(), DadosRemotos = JsonSerializer.SerializeToElement(JsonNode.Parse(prior.Dados))
                    });
                    if (request.Atomico) { response.EventosAceitos.Clear(); return Ok(response); }
                    continue;
                }
                if (prior is not null)
                    incoming.Dados = JsonSerializer.SerializeToElement(JsonNode.Parse(SyncPayload.Merge(prior.Dados, incoming.Dados.GetRawText())));
                var validationError = operation == "excluir" ? null : await SyncRecordValidator.ValidateAsync(
                    entityName, incoming.Dados, async (entity, id) =>
                        (await FindCurrentStateAsync(request.EspacoId, entity, id, cancellationToken)) is { Excluido: false },
                    async roomId =>
                    {
                        var room = await FindCurrentStateAsync(request.EspacoId, "quartos", roomId, cancellationToken);
                        return room is null || room.Excluido ? null : JsonSerializer.Deserialize<JsonElement>(room.Dados);
                    });
                if (operation == "excluir" && entityName is "quartos" or "clientes" or "perfis")
                {
                    var dependentEntity = entityName == "perfis" ? "usuarios" : "reservas";
                    var field = entityName switch { "quartos" => "quartoId", "clientes" => "clienteId", _ => "perfilId" };
                    var dependents = await LoadEntityState(request.EspacoId, dependentEntity, cancellationToken);
                    foreach (var dependent in dependents.Values)
                    {
                        if (dependent.Operacao == "excluir") continue;
                        using var doc = JsonDocument.Parse(dependent.Dados!);
                        if (TryGetString(doc.RootElement, field, out var reference) &&
                            Guid.TryParse(reference, out var referenceId) && referenceId == incoming.EntidadeId)
                        { validationError = "O registro possui vinculos ativos e nao pode ser excluido."; break; }
                    }
                }
                if (validationError is not null)
                {
                    response.Conflitos.Add(new SyncConflictDto
                    {
                        FilaId = incoming.Id, Entidade = entityName, EntidadeId = incoming.EntidadeId,
                        Tipo = "dados_invalidos", Mensagem = validationError,
                        DadosLocais = incoming.Dados.Clone(), DadosRemotos = JsonSerializer.SerializeToElement(new { })
                    });
                    if (request.Atomico) { response.EventosAceitos.Clear(); return Ok(response); }
                    continue;
                }
                if (entityName == "usuarios" && operation != "excluir" && !SyncPayload.IsDeleted(incoming.Dados.GetRawText()))
                {
                    TryGetString(incoming.Dados, "email", out var email);
                    var duplicate = userState.Values.FirstOrDefault(state => state.EntidadeId != incoming.EntidadeId &&
                        state.Operacao != "excluir" && string.Equals(
                            ReadUserEmail(state.Dados!), email.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (duplicate is not null)
                    {
                        response.Conflitos.Add(new SyncConflictDto
                        {
                            FilaId = incoming.Id, Entidade = entityName, EntidadeId = incoming.EntidadeId,
                            Tipo = "usuario_email_duplicado", Mensagem = "Ja existe um usuario com este e-mail nesta pousada.",
                            DadosLocais = incoming.Dados.Clone(), DadosRemotos = JsonSerializer.SerializeToElement(new { })
                        });
                        if (request.Atomico) { response.EventosAceitos.Clear(); return Ok(response); }
                        continue;
                    }
                }

                if (entityName == "dados_empresa")
                {
                    TryGetString(incoming.Dados, "documento", out var rawDocument);
                    TryGetString(incoming.Dados, "nome", out var companyName);
                    var document = OnlyDigits(rawDocument);
                    if (operation == "excluir" ||
                        string.IsNullOrWhiteSpace(companyName) ||
                        (workspace.DocumentoEmpresaHash is not null &&
                         (!IsValidCompanyDocument(document) || HashText(document) != workspace.DocumentoEmpresaHash)))
                        return BadRequest(new { message = "O CPF/CNPJ e o nome devem corresponder à pousada conectada." });
                    var companyState = await _context.SyncEntityStates
                        .FirstOrDefaultAsync(x => x.EspacoId == request.EspacoId && x.Entidade == "dados_empresa", cancellationToken);
                    companyState ??= _context.SyncEntityStates.Local
                        .FirstOrDefault(x => x.EspacoId == request.EspacoId && x.Entidade == "dados_empresa");
                    if (companyState is not null && companyState.EntidadeId != incoming.EntidadeId)
                        return Conflict(new { message = "Esta pousada já possui um cadastro empresarial. Baixe os dados antes de alterar." });
                    workspace.Nome = Truncate(companyName.Trim(), 160);
                    workspace.DataHoraAtualizado = timestamp;
                }

                if (entityName == "reservas" && operation != "excluir")
                {
                    var conflict = FindReservationConflict(
                        incoming,
                        reservationState.Values);
                    if (conflict is not null)
                    {
                        response.Conflitos.Add(conflict);
                        if (request.Atomico)
                        {
                            response.EventosAceitos.Clear();
                            return Ok(response);
                        }
                        continue;
                    }
                }

                if (entityName == "clientes" && operation != "excluir")
                {
                    var conflict = FindCustomerConflict(incoming, customerState.Values);
                    if (conflict is not null)
                    {
                        response.Conflitos.Add(conflict);
                        if (request.Atomico)
                        {
                            response.EventosAceitos.Clear();
                            return Ok(response);
                        }
                        continue;
                    }
                }

                var serverTime = timestamp.AddTicks(tick++ * 10);
                var sync = new SincronizacaoEntity
                {
                    Id = Guid.NewGuid(),
                    EspacoId = request.EspacoId,
                    DispositivoId = request.DispositivoId,
                    NomeDispositivo = Truncate(request.NomeDispositivo?.Trim(), 160),
                    EventoId = incoming.Id,
                    Entidade = entityName,
                    EntidadeId = incoming.EntidadeId,
                    Operacao = operation,
                    Dados = incoming.Dados.GetRawText(),
                    DataHoraCriado = serverTime,
                    DataHoraAtualizado = serverTime,
                    DataHoraSincronizado = serverTime,
                    Forcado = incoming.Forcar
                };
                await _context.Sincronizacoes.AddAsync(sync, cancellationToken);
                sync.Dados = await UpsertCurrentStateAsync(
                    request.EspacoId,
                    entityName,
                    incoming.EntidadeId.Value,
                    operation,
                    incoming.Dados.GetRawText(),
                    serverTime,
                    cancellationToken);
                response.EventosAceitos.Add(incoming.Id);
                knownEventIds.Add(incoming.Id);

                if (entityName == "reservas" && incoming.EntidadeId.HasValue)
                {
                    reservationState[incoming.EntidadeId.Value] = sync;
                }
                if (entityName == "clientes" && incoming.EntidadeId.HasValue)
                {
                    if (operation == "excluir")
                    {
                        customerState.Remove(incoming.EntidadeId.Value);
                    }
                    else
                    {
                        customerState[incoming.EntidadeId.Value] = sync;
                    }
                }
                if (entityName == "usuarios") userState[incoming.EntidadeId!.Value] = sync;
            }

            await _context.SaveChangesAsync(cancellationToken);
            var lastEventTime = await _context.Sincronizacoes
                .AsNoTracking()
                .Where(x => x.EspacoId == request.EspacoId)
                .MaxAsync(x => (DateTime?)x.DataHoraCriado, cancellationToken);
            response.Cursor = lastEventTime?.ToString("O");
            await transaction.CommitAsync(cancellationToken);
            return Ok(response);
        }
        catch (Exception error) when (IsSerializationFailure(error))
        {
            return Conflict(new
            {
                message = "Outra sincronização alterou esta hospedagem. Tente novamente."
            });
        }
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull(
        [FromQuery] Guid espacoId,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        if (espacoId == Guid.Empty)
        {
            return BadRequest(new { message = "Informe o código da hospedagem." });
        }
        var authorizationError = await ValidateAccessAsync(espacoId, cancellationToken);
        if (authorizationError is not null) return authorizationError;

        var (after, afterId) = ParseCursor(cursor);
        if (!string.IsNullOrWhiteSpace(cursor) && after is null)
        {
            return BadRequest(new { message = "Cursor de sincronização inválido." });
        }

        var query = _context.Sincronizacoes
            .AsNoTracking()
            .Where(x => x.EspacoId == espacoId);
        if (after.HasValue)
        {
            query = afterId.HasValue
                ? query.Where(x => x.DataHoraCriado > after.Value ||
                    (x.DataHoraCriado == after.Value && x.Id.CompareTo(afterId.Value) > 0))
                : query.Where(x => x.DataHoraCriado > after.Value);
        }

        var candidates = await query
            .OrderBy(x => x.DataHoraCriado)
            .ThenBy(x => x.Id)
            .Take(501)
            .ToListAsync(cancellationToken);
        var events = candidates.Take(500).ToList();

        return Ok(new SyncPullResponseDto
        {
            Eventos = events.Select(ToPullDto).ToList(),
            Cursor = events.Count == 0 ? cursor : FormatCursor(events[^1]),
            HasMore = candidates.Count > events.Count
        });
    }

}
