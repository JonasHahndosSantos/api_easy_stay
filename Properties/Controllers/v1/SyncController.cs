using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiEasyStay.Properties.Data;
using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApiEasyStay.Properties.Controllers.v1;

[ApiController]
[Route("api/v1/sync")]
[EnableRateLimiting("sync")]
public sealed class SyncController : ControllerBase
{
    private const int MaxBatchSize = 250;
    private const int MaxEventBytes = 512_000;
    private static readonly HashSet<string> AllowedEntities =
    [
        "perfis", "clientes", "quartos", "usuarios", "reservas", "lancamentos_financeiros"
    ];

    private readonly AppDbContext _context;
    public SyncController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] SyncRegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.EspacoId == Guid.Empty)
        {
            return BadRequest(new { message = "Informe o código da hospedagem." });
        }

        var suppliedKey = GetSuppliedKey();
        if (!IsValidKey(suppliedKey))
        {
            return BadRequest(new
            {
                message = "A chave da hospedagem deve possuir pelo menos 32 caracteres."
            });
        }

        var existing = await _context.EspacosSincronizacao
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == request.EspacoId, cancellationToken);
        if (existing is not null)
        {
            if (existing.DataHoraDeletado is not null || !existing.Ativo)
            {
                return Conflict(new { message = "Esta hospedagem está desativada." });
            }
            if (!MatchesKey(existing.ChaveHash, suppliedKey))
            {
                return Conflict(new
                {
                    message = "Este código de hospedagem já utiliza outra chave."
                });
            }

            return Ok(new { registered = true, espacoId = existing.Id });
        }

        var now = DateTime.UtcNow;
        var workspace = new EspacoSincronizacaoEntity
        {
            Id = request.EspacoId,
            ChaveHash = HashKey(suppliedKey),
            Nome = string.IsNullOrWhiteSpace(request.NomeDispositivo)
                ? null
                : request.NomeDispositivo.Trim(),
            Ativo = true,
            DataHoraCriado = now,
            DataHoraAtualizado = now
        };
        await _context.EspacosSincronizacao.AddAsync(workspace, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (
            error.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new
            {
                message = "Este código de hospedagem já foi cadastrado por outro dispositivo."
            });
        }
        return StatusCode(
            StatusCodes.Status201Created,
            new { registered = true, espacoId = workspace.Id });
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health(
        [FromQuery] Guid espacoId,
        CancellationToken cancellationToken)
    {
        if (espacoId == Guid.Empty)
        {
            return BadRequest(new { message = "Informe o código da hospedagem." });
        }
        var authorizationError = await ValidateAccessAsync(espacoId, cancellationToken);
        if (authorizationError is not null) return authorizationError;

        return Ok(new { online = true, serverTime = DateTime.UtcNow });
    }

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
        if (request.Eventos is null || request.Eventos.Count > MaxBatchSize)
        {
            return BadRequest(new { message = $"Envie no maximo {MaxBatchSize} eventos por vez." });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
        var response = new SyncPushResponseDto();
        var incomingIds = request.Eventos.Select(x => x.Id).ToList();
        var knownEventIds = await _context.Sincronizacoes
            .AsNoTracking()
            .Where(x => x.EspacoId == request.EspacoId && incomingIds.Contains(x.EventoId))
            .Select(x => x.EventoId)
            .ToHashSetAsync(cancellationToken);
        var reservationState = await LoadReservationState(request.EspacoId, cancellationToken);
        var timestamp = DateTime.UtcNow;
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
            if (incoming.Dados.TryGetProperty("id", out var payloadId) &&
                (!Guid.TryParse(payloadId.ToString(), out var parsedId) ||
                 parsedId != incoming.EntidadeId))
            {
                return BadRequest(new { message = "O ID do evento não corresponde aos dados." });
            }
            if (entityName == "reservas" && operation != "excluir" &&
                ParseReservation(incoming.Dados, incoming.EntidadeId, operation) is null)
            {
                return BadRequest(new { message = "A reserva possui quarto ou datas inválidos." });
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

            if (entityName == "reservas" && operation != "excluir")
            {
                var conflict = FindReservationConflict(incoming, reservationState.Values);
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

            var serverTime = timestamp.AddTicks(tick++);
            var sync = new SincronizacaoEntity
            {
                Id = Guid.NewGuid(),
                EspacoId = request.EspacoId,
                DispositivoId = request.DispositivoId,
                NomeDispositivo = request.NomeDispositivo?.Trim(),
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
            response.EventosAceitos.Add(incoming.Id);
            knownEventIds.Add(incoming.Id);

            if (entityName == "reservas" && incoming.EntidadeId.HasValue)
            {
                reservationState[incoming.EntidadeId.Value] = sync;
            }
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

    private async Task<IActionResult?> ValidateAccessAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var workspace = await _context.EspacosSincronizacao
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == workspaceId && x.Ativo,
                cancellationToken);
        if (workspace is null)
        {
            return NotFound(new
            {
                message = "Hospedagem não cadastrada. Crie-a no primeiro dispositivo."
            });
        }

        var suppliedKey = GetSuppliedKey();
        if (!IsValidKey(suppliedKey) || !MatchesKey(workspace.ChaveHash, suppliedKey))
        {
            return Unauthorized(new { message = "Chave de sincronização inválida." });
        }

        return null;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> Latest(
        [FromQuery] Guid espacoId,
        [FromQuery] string entidade,
        [FromQuery] Guid entidadeId,
        CancellationToken cancellationToken)
    {
        var normalizedEntity = entidade?.Trim().ToLowerInvariant() ?? "";
        if (espacoId == Guid.Empty || entidadeId == Guid.Empty ||
            !AllowedEntities.Contains(normalizedEntity))
        {
            return BadRequest(new { message = "Hospedagem, entidade e registro são obrigatórios." });
        }
        var authorizationError = await ValidateAccessAsync(espacoId, cancellationToken);
        if (authorizationError is not null) return authorizationError;

        var latest = await _context.Sincronizacoes
            .AsNoTracking()
            .Where(x => x.EspacoId == espacoId && x.Entidade == normalizedEntity &&
                        x.EntidadeId == entidadeId)
            .OrderByDescending(x => x.DataHoraCriado)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return Ok(new { evento = latest is null ? null : ToPullDto(latest) });
    }

    private string GetSuppliedKey() => Request.Headers["X-EasyStay-Key"].ToString();

    private static bool IsValidKey(string key) => key.Length is >= 32 and <= 256;

    private static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static bool MatchesKey(string expectedHash, string suppliedKey)
    {
        if (expectedHash.Length != 64 || !IsValidKey(suppliedKey)) return false;
        try
        {
            var expected = Convert.FromHexString(expectedHash);
            var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static (DateTime? Timestamp, Guid? Id) ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);

        var separator = cursor.LastIndexOf('|');
        if (separator > 0 &&
            DateTime.TryParse(cursor[..separator], out var timestamp) &&
            Guid.TryParse(cursor[(separator + 1)..], out var id))
        {
            return (timestamp.ToUniversalTime(), id);
        }

        return DateTime.TryParse(cursor, out var legacyTimestamp)
            ? (legacyTimestamp.ToUniversalTime(), null)
            : (null, null);
    }

    private static string FormatCursor(SincronizacaoEntity entity) =>
        $"{entity.DataHoraCriado:O}|{entity.Id:D}";

    private static bool IsSerializationFailure(Exception error) =>
        error is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.SerializationFailure ||
        error.InnerException is PostgresException inner &&
            inner.SqlState == PostgresErrorCodes.SerializationFailure;

    private async Task<Dictionary<Guid, SincronizacaoEntity>> LoadReservationState(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var events = await _context.Sincronizacoes
            .FromSqlInterpolated($"""
                SELECT DISTINCT ON (entidade_id) *
                FROM sincronizacoes
                WHERE espaco_id = {workspaceId}
                  AND entidade = 'reservas'
                  AND entidade_id IS NOT NULL
                ORDER BY entidade_id, data_hora_criado DESC, id DESC
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return events.ToDictionary(x => x.EntidadeId!.Value);
    }

    private static SyncConflictDto? FindReservationConflict(
        SyncPushEventDto incoming,
        IEnumerable<SincronizacaoEntity> currentReservations)
    {
        var local = ParseReservation(incoming.Dados, incoming.EntidadeId, incoming.Operacao);
        if (local is null || !local.IsActive) return null;

        foreach (var current in currentReservations)
        {
            if (current.EntidadeId == incoming.EntidadeId || string.IsNullOrWhiteSpace(current.Dados))
            {
                continue;
            }
            using var document = JsonDocument.Parse(current.Dados);
            var remote = ParseReservation(document.RootElement, current.EntidadeId, current.Operacao);
            if (remote is null || !remote.IsActive || remote.RoomId != local.RoomId)
            {
                continue;
            }
            if (local.Start < remote.End && local.End > remote.Start)
            {
                return new SyncConflictDto
                {
                    FilaId = incoming.Id,
                    Entidade = "reservas",
                    EntidadeId = incoming.EntidadeId,
                    Tipo = "reserva_sobreposta",
                    Mensagem = "Já existe outra reserva para este quarto no período informado.",
                    DadosLocais = incoming.Dados.Clone(),
                    DadosRemotos = document.RootElement.Clone()
                };
            }
        }
        return null;
    }

    private static ReservationSnapshot? ParseReservation(
        JsonElement data,
        Guid? id,
        string? operation)
    {
        if (operation == "excluir" || data.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetString(data, "quartoId", out var room) ||
            !TryGetDate(data, "dataEntrada", out var start) ||
            !TryGetDate(data, "dataSaida", out var end))
        {
            return null;
        }
        if (start >= end || !Guid.TryParse(room, out _)) return null;
        var status = TryGetInt(data, "status", out var value) ? value : 1;
        var deleted = data.TryGetProperty("DataHoraDeletado", out var deletedValue) &&
                      deletedValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        return new ReservationSnapshot(id, room, start, end, status != 4 && !deleted);
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property)) return false;
        value = property.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDate(JsonElement element, string name, out DateTime value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) &&
               DateTime.TryParse(property.ToString(), out value);
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) &&
               int.TryParse(property.ToString(), out value);
    }

    private static SyncPullEventDto ToPullDto(SincronizacaoEntity entity)
    {
        using var document = JsonDocument.Parse(entity.Dados ?? "{}");
        return new SyncPullEventDto
        {
            IdEvento = entity.EventoId,
            Entidade = entity.Entidade ?? string.Empty,
            EntidadeId = entity.EntidadeId,
            Operacao = entity.Operacao ?? string.Empty,
            Dados = document.RootElement.Clone(),
            DispositivoId = entity.DispositivoId,
            NomeDispositivo = entity.NomeDispositivo,
            DataHoraServidor = entity.DataHoraCriado
        };
    }

    private sealed record ReservationSnapshot(
        Guid? Id,
        string RoomId,
        DateTime Start,
        DateTime End,
        bool IsActive);
}
