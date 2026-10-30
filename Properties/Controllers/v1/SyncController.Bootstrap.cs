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
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] SyncRegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment()) return NotFound();
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

            return Ok(new
            {
                registered = true,
                espacoId = existing.Id,
                workspaceId = existing.Id,
                nomeEmpresa = existing.Nome,
                workspaceName = existing.Nome
            });
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
            CargaInicialConcluida = true,
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
            new
            {
                registered = true,
                espacoId = workspace.Id,
                workspaceId = workspace.Id,
                nomeEmpresa = workspace.Nome,
                workspaceName = workspace.Nome
            });
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

    // Recuperacao local de uma base ausente. Producao continua exigindo o
    // fluxo de verificacao de e-mail; esta rota nunca sobrepoe uma hospedagem.
    [HttpPost("recover")]
    public async Task<IActionResult> Recover(
        [FromBody] SyncRecoveryRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            return StatusCode(403, new { message = "Reconecte a hospedagem com um codigo de acesso para iniciar uma nova carga." });
        var key = GetSuppliedKey();
        if (!IsValidKey(key) || request.EspacoId == Guid.Empty || request.DispositivoId == Guid.Empty)
            return BadRequest(new { message = "Informe a hospedagem, o dispositivo e a chave da conexao local." });
        var identifier = NormalizeIdentifier(request.Identificador);
        var document = NormalizeCompanyDocument(request.DocumentoEmpresa);
        if (identifier.Length > 0 && !IsValidIdentifier(identifier))
            return BadRequest(new { message = "Informe um e-mail valido nos dados da hospedagem." });
        if (document.Length > 0 && !IsValidCompanyDocument(document))
            return BadRequest(new { message = "Informe um CPF ou CNPJ valido nos dados da hospedagem." });

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockWorkspaceAsync(request.EspacoId, cancellationToken);
        var existing = await _context.EspacosSincronizacao.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == request.EspacoId, cancellationToken);
        if (existing is not null)
        {
            if (!existing.Ativo || existing.DataHoraDeletado is not null || !MatchesKey(existing.ChaveHash, key))
                return Conflict(new { message = "A hospedagem ja existe com outra conexao. Nenhum dado foi substituido." });
            if (!existing.CargaInicialConcluida && existing.DispositivoCargaInicialId != request.DispositivoId)
                return Conflict(new { message = "A primeira carga pertence a outro dispositivo. Aguarde a conclusao." });
            return Ok(new { registered = true, needsUpload = !existing.CargaInicialConcluida });
        }
        var identifierHash = identifier.Length == 0 ? null : HashText(identifier);
        var documentHash = document.Length == 0 ? null : HashText(document);
        if (await _context.EspacosSincronizacao.IgnoreQueryFilters().AnyAsync(
            x => (identifierHash != null && x.IdentificadorHash == identifierHash) ||
                 (documentHash != null && x.DocumentoEmpresaHash == documentHash), cancellationToken))
            return Conflict(new { message = "Ja existe uma hospedagem para esta empresa ou contato. Conecte a hospedagem existente." });

        var now = DateTime.UtcNow;
        await _context.EspacosSincronizacao.AddAsync(new EspacoSincronizacaoEntity
        {
            Id = request.EspacoId,
            ChaveHash = HashKey(key),
            IdentificadorHash = identifierHash,
            DocumentoEmpresaHash = documentHash,
            Nome = string.IsNullOrWhiteSpace(request.NomeEmpresa) ? null : Truncate(request.NomeEmpresa.Trim(), 160),
            Ativo = true,
            DispositivoCargaInicialId = request.DispositivoId,
            CargaInicialConcluida = false,
            DataHoraCriado = now,
            DataHoraAtualizado = now
        }, cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { message = "A hospedagem ja foi criada por outro dispositivo. Nenhum dado foi substituido." });
        }
        return StatusCode(201, new { registered = true, needsUpload = true });
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap(
        [FromBody] SyncBootstrapRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.EspacoId == Guid.Empty || request.DispositivoId == Guid.Empty)
            return BadRequest(new { message = "Hospedagem e dispositivo são obrigatórios." });
        var authorizationError = await ValidateAccessAsync(request.EspacoId, cancellationToken);
        if (authorizationError is not null) return authorizationError;
        var deviceError = await ValidateDeviceAsync(request.DispositivoId, cancellationToken);
        if (deviceError is not null) return deviceError;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockWorkspaceAsync(request.EspacoId, cancellationToken);
        var workspace = await _context.EspacosSincronizacao
            .FirstAsync(x => x.Id == request.EspacoId, cancellationToken);
        if (workspace.DispositivoCargaInicialId is null)
        {
            var legacyFirstEvent = await _context.Sincronizacoes.AsNoTracking()
                .Where(x => x.EspacoId == request.EspacoId)
                .OrderBy(x => x.DataHoraCriado).ThenBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (legacyFirstEvent is not null)
                return Ok(new { source = legacyFirstEvent.DispositivoId == request.DispositivoId ? "local" : "cloud", completed = true });
        }
        if (!workspace.CargaInicialConcluida)
        {
            if (workspace.DispositivoCargaInicialId is null && !request.Concluir)
                workspace.DispositivoCargaInicialId = request.DispositivoId;
            if (workspace.DispositivoCargaInicialId != request.DispositivoId)
                return Conflict(new { message = "A primeira carga está em andamento em outro dispositivo. Aguarde a conclusão e tente novamente." });
            if (request.Concluir)
            {
                if (!await _context.Sincronizacoes.AnyAsync(x => x.EspacoId == request.EspacoId, cancellationToken))
                    return BadRequest(new { message = "Envie os dados antes de concluir a primeira carga." });
                if (!await _context.SyncEntityStates.AnyAsync(x => x.EspacoId == request.EspacoId && x.Entidade == "usuarios" && !x.Excluido, cancellationToken) ||
                    !await _context.SyncEntityStates.AnyAsync(x => x.EspacoId == request.EspacoId && x.Entidade == "perfis" && !x.Excluido, cancellationToken))
                    return BadRequest(new { message = "A primeira carga precisa incluir usuarios e permissoes antes de ser concluida." });
                workspace.CargaInicialConcluida = true;
            }
            workspace.DataHoraAtualizado = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Ok(new
        {
            source = workspace.DispositivoCargaInicialId == request.DispositivoId ? "local" : "cloud",
            completed = workspace.CargaInicialConcluida
        });
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> Snapshot([FromQuery] Guid espacoId, CancellationToken cancellationToken)
    {
        var authorizationError = await ValidateAccessAsync(espacoId, cancellationToken);
        if (authorizationError is not null) return authorizationError;
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var workspace = await _context.EspacosSincronizacao.AsNoTracking()
            .FirstAsync(x => x.Id == espacoId, cancellationToken);
        var legacyComplete = workspace.DispositivoCargaInicialId is null &&
            await _context.Sincronizacoes.AnyAsync(x => x.EspacoId == espacoId, cancellationToken);
        if (!workspace.CargaInicialConcluida && !legacyComplete)
            return Conflict(new { message = "A primeira carga ainda não terminou. Aguarde o envio no dispositivo de origem." });
        var latest = await _context.Sincronizacoes.AsNoTracking()
            .Where(x => x.EspacoId == espacoId)
            .OrderByDescending(x => x.DataHoraCriado).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var states = await _context.SyncEntityStates.AsNoTracking()
            .Where(x => x.EspacoId == espacoId).ToListAsync(cancellationToken);
        var versions = await _context.Sincronizacoes.FromSqlInterpolated($"""
            SELECT DISTINCT ON (entidade, entidade_id) * FROM sincronizacoes
            WHERE espaco_id = {espacoId} AND entidade_id IS NOT NULL
            ORDER BY entidade, entidade_id, data_hora_criado DESC, id DESC
            """).AsNoTracking().ToListAsync(cancellationToken);
        var eventVersions = versions.ToDictionary(x => (x.Entidade, x.EntidadeId!.Value), x => x.EventoId);
        var previousData = new Dictionary<(string?, Guid), string>();
        if (states.Any(x => x.Excluido))
        {
            var previous = await _context.Sincronizacoes.FromSqlInterpolated($"""
                SELECT DISTINCT ON (entidade, entidade_id) * FROM sincronizacoes
                WHERE espaco_id = {espacoId} AND operacao != 'excluir' AND entidade_id IS NOT NULL
                ORDER BY entidade, entidade_id, data_hora_criado DESC, id DESC
                """).AsNoTracking().ToListAsync(cancellationToken);
            previousData = previous.ToDictionary(x => (x.Entidade, x.EntidadeId!.Value), x => x.Dados ?? "{}");
        }
        var entityOrder = new[] { "dados_empresa", "perfis", "clientes", "quartos", "usuarios", "reservas", "lancamentos_financeiros" };
        var events = states.OrderBy(x => Array.IndexOf(entityOrder, x.Entidade))
            .ThenBy(x => x.EntidadeId).Select(state =>
            {
                var data = JsonNode.Parse(state.Dados)!.AsObject();
                if (state.Excluido && previousData.TryGetValue((state.Entidade, state.EntidadeId), out var original))
                {
                    data = JsonNode.Parse(SyncPayload.Merge(original, data.ToJsonString()))!.AsObject();
                }
                if (state.Excluido && data["DataHoraDeletado"] is null)
                    data["DataHoraDeletado"] = state.DataHoraOrigem;
                return new SyncPullEventDto
                {
                    IdEvento = eventVersions.GetValueOrDefault((state.Entidade, state.EntidadeId), state.Id),
                    Entidade = state.Entidade,
                    EntidadeId = state.EntidadeId,
                    Operacao = "atualizar",
                    Dados = JsonSerializer.SerializeToElement(data),
                    DataHoraServidor = state.DataHoraOrigem
                };
            }).ToList();
        await transaction.CommitAsync(cancellationToken);
        return Ok(new SyncPullResponseDto
        {
            Eventos = events,
            Cursor = latest is null ? null : FormatCursor(latest),
            HasMore = false
        });
    }

}
