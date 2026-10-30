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
    [HttpPost("company-document/check")]
    public async Task<IActionResult> CheckCompanyDocument(
        [FromBody] SyncCompanyDocumentCheckDto request,
        CancellationToken cancellationToken)
    {
        var document = NormalizeCompanyDocument(request.DocumentoEmpresa);
        if (!IsValidCompanyDocument(document))
        {
            return BadRequest(new { message = "Informe um CPF ou CNPJ válido." });
        }

        var documentHash = HashText(document);
        var workspace = await _context.EspacosSincronizacao
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.DocumentoEmpresaHash == documentHash && x.Ativo,
                cancellationToken);
        var hasData = workspace is not null && await _context.Sincronizacoes
            .AsNoTracking()
            .AnyAsync(x => x.EspacoId == workspace.Id, cancellationToken);

        return Ok(new
        {
            exists = workspace is not null,
            hasData
        });
    }

    [HttpPost("auth/request-code")]
    [RequestSizeLimit(8_192)]
    public async Task<IActionResult> RequestCode(
        [FromBody] SyncAuthCodeRequestDto request,
        CancellationToken cancellationToken)
    {
        var identifier = NormalizeIdentifier(request.Identificador);
        if (!IsValidIdentifier(identifier))
        {
            return BadRequest(new { message = "Informe um e-mail válido. O código é enviado somente por e-mail." });
        }
        var now = DateTime.UtcNow;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var identifierHash = HashText(identifier);
        var companyDocument = NormalizeCompanyDocument(request.DocumentoEmpresa);
        var companyDocumentHash = string.Empty;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAuthAsync(identifierHash, companyDocument, cancellationToken);
        var recentCodes = _context.SyncAuthCodes.IgnoreQueryFilters()
            .Where(x => x.IdentificadorHash == identifierHash && x.DataHoraCriado > now.AddHours(-1));
        var latestRequest = await recentCodes.MaxAsync(x => (DateTime?)x.DataHoraCriado, cancellationToken);
        if (latestRequest.HasValue && now - latestRequest.Value < _security.CodeCooldown ||
            await recentCodes.CountAsync(cancellationToken) >= 10)
        {
            Response.Headers.RetryAfter = "60";
            return StatusCode(429, new { message = "Aguarde antes de solicitar outro codigo para este contato." });
        }
        if (!string.IsNullOrWhiteSpace(companyDocument))
        {
            if (!IsValidCompanyDocument(companyDocument))
            {
                return BadRequest(new { message = "Informe um CPF ou CNPJ válido." });
            }
            companyDocumentHash = HashText(companyDocument);
            var registeredWorkspace = await _context.EspacosSincronizacao
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.DocumentoEmpresaHash == companyDocumentHash,
                    cancellationToken);
            if (registeredWorkspace is not null &&
                (!registeredWorkspace.Ativo || registeredWorkspace.DataHoraDeletado is not null ||
                registeredWorkspace.IdentificadorHash != identifierHash))
            {
                return Unauthorized(new
                {
                    message = "Use o contato já vinculado a esta pousada."
                });
            }
            var contactWorkspace = await _context.EspacosSincronizacao.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdentificadorHash == identifierHash, cancellationToken);
            if (contactWorkspace is not null && contactWorkspace.DocumentoEmpresaHash != companyDocumentHash)
                return Conflict(new { message = "Este contato já está vinculado a outra pousada. Use o contato daquela pousada ou informe outro contato para criar uma nova." });
        }
        else
        {
            var workspace = await _context.EspacosSincronizacao
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.IdentificadorHash == identifierHash && x.Ativo,
                    cancellationToken);
            if (workspace is null)
            {
                return NotFound(new
                {
                    message = "Nenhuma pousada na nuvem foi encontrada para este contato."
                });
            }
        }

        var expiredCodes = await _context.SyncAuthCodes
            .Where(x => x.IdentificadorHash == identifierHash && x.UsadoEm == null)
            .ToListAsync(cancellationToken);
        foreach (var item in expiredCodes)
        {
            item.DataHoraDeletado = now;
            item.DataHoraAtualizado = now;
        }

        await _context.SyncAuthCodes.AddAsync(new SyncAuthCodeEntity
        {
            Id = Guid.NewGuid(),
            IdentificadorHash = identifierHash,
            DocumentoEmpresaHash = companyDocumentHash,
            CodigoHash = _security.HashCode(identifier, companyDocumentHash, code),
            ExpiraEm = now.Add(AuthCodeLifetime),
            NomeDispositivo = Truncate(request.NomeDispositivo?.Trim(), 160),
            DataHoraCriado = now,
            DataHoraAtualizado = now
        }, cancellationToken);

        string deliveredBy;
        try { deliveredBy = await _codeSender.SendAsync(identifier, code, cancellationToken); }
        catch (Exception error) when (error is SmtpException or HttpRequestException or InvalidOperationException ||
            error is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Falha de entrega de codigo; tipo {ErrorType}", error.GetType().Name);
            return StatusCode(503, new { message = "Nao foi possivel enviar o codigo por este canal. Verifique a configuracao de envio da API." });
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Ok(new
        {
            sent = true,
            expiresInSeconds = (int)AuthCodeLifetime.TotalSeconds,
            deliveredBy,
            codigoDesenvolvimento = deliveredBy == "development-console" ? code : null
        });
    }

    [HttpPost("auth/verify-code")]
    [RequestSizeLimit(8_192)]
    public async Task<IActionResult> VerifyCode(
        [FromBody] SyncAuthCodeVerifyDto request,
        CancellationToken cancellationToken)
    {
        var identifier = NormalizeIdentifier(request.Identificador);
        var code = request.Codigo?.Trim() ?? "";
        if (!IsValidIdentifier(identifier) || code.Length != 6 || code.Any(c => c is < '0' or > '9') || request.DispositivoId == Guid.Empty)
        {
            return BadRequest(new { message = "Código ou e-mail inválido." });
        }
        var now = DateTime.UtcNow;
        var identifierHash = HashText(identifier);
        var companyDocument = NormalizeCompanyDocument(request.DocumentoEmpresa);
        var companyDocumentHash = string.Empty;
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAuthAsync(identifierHash, companyDocument, cancellationToken);
        if (!string.IsNullOrWhiteSpace(companyDocument))
        {
            if (!IsValidCompanyDocument(companyDocument))
            {
                return BadRequest(new { message = "Informe um CPF ou CNPJ válido." });
            }
            companyDocumentHash = HashText(companyDocument);
            var registeredWorkspace = await _context.EspacosSincronizacao
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.DocumentoEmpresaHash == companyDocumentHash,
                    cancellationToken);
            if (registeredWorkspace is not null &&
                (!registeredWorkspace.Ativo || registeredWorkspace.DataHoraDeletado is not null ||
                registeredWorkspace.IdentificadorHash != identifierHash))
            {
                return Unauthorized(new { message = "Código inválido ou expirado." });
            }
        }
        var authCode = await _context.SyncAuthCodes
            .Where(x =>
                x.IdentificadorHash == identifierHash &&
                x.DocumentoEmpresaHash == companyDocumentHash &&
                x.UsadoEm == null &&
                x.ExpiraEm >= now)
            .OrderByDescending(x => x.DataHoraCriado)
            .FirstOrDefaultAsync(cancellationToken);
        if (authCode is null)
        {
            return Unauthorized(new { message = "Código inválido ou expirado." });
        }
        if (authCode.Tentativas >= 5)
        {
            return Unauthorized(new { message = "Código bloqueado por excesso de tentativas." });
        }
        if (!FixedTimeEquals(authCode.CodigoHash, _security.HashCode(identifier, companyDocumentHash, code)))
        {
            authCode.Tentativas++;
            authCode.DataHoraAtualizado = now;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Unauthorized(new { message = "Código inválido ou expirado." });
        }

        authCode.UsadoEm = now;
        authCode.DataHoraAtualizado = now;

        EspacoSincronizacaoEntity? workspace;
        if (string.IsNullOrWhiteSpace(companyDocument))
        {
            workspace = await _context.EspacosSincronizacao
                .FirstOrDefaultAsync(
                    x => x.IdentificadorHash == identifierHash && x.Ativo,
                    cancellationToken);
            if (workspace is null)
            {
                return NotFound(new
                {
                    message = "A pousada vinculada a este contato não está mais disponível."
                });
            }
        }
        else
        {
            workspace = await GetOrCreateWorkspaceAsync(
                identifier,
                identifierHash,
                companyDocumentHash,
                request.NomeEmpresa,
                cancellationToken);
        }
        var accessKey = GenerateAccessKey();
        var oldTokens = await _context.SyncAccessTokens.Where(x =>
            x.EspacoId == workspace.Id && x.DispositivoId == request.DispositivoId && x.Ativo)
            .ToListAsync(cancellationToken);
        foreach (var token in oldTokens) token.Ativo = false;
        await _context.SyncAccessTokens.AddAsync(new SyncAccessTokenEntity
        {
            Id = Guid.NewGuid(),
            EspacoId = workspace.Id,
            TokenHash = HashKey(accessKey),
            DispositivoId = request.DispositivoId == Guid.Empty ? null : request.DispositivoId,
            NomeDispositivo = Truncate(request.NomeDispositivo?.Trim(), 160),
            Ativo = true,
            DataHoraCriado = now,
            DataHoraAtualizado = now
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var user = await FindSyncedUserAsync(workspace.Id, identifier, cancellationToken);
        return Ok(new
        {
            espacoId = workspace.Id,
            workspaceId = workspace.Id,
            nomeEmpresa = workspace.Nome,
            workspaceName = workspace.Nome,
            syncToken = accessKey,
            accessKey,
            expiresAt = now.Add(_security.TokenLifetime),
            userId = user?.UserId,
            userEmail = user?.Email,
            email = user?.Email
        });
    }

    [HttpPost("auth/revoke")]
    public async Task<IActionResult> Revoke([FromBody] SyncBootstrapRequestDto request, CancellationToken cancellationToken)
    {
        var authorizationError = await ValidateAccessAsync(request.EspacoId, cancellationToken);
        if (authorizationError is not null) return authorizationError;
        var hash = HashKey(GetSuppliedKey());
        await _context.SyncAccessTokens.Where(x => x.EspacoId == request.EspacoId && x.TokenHash == hash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Ativo, false), cancellationToken);
        return Ok(new { revoked = true });
    }

}
