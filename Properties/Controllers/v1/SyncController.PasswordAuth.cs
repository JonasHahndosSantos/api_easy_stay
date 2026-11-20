using System.Text;
using System.Text.Json;
using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ApiEasyStay.Properties.Controllers.v1;

public sealed partial class SyncController
{
    [HttpPost("auth/password")]
    [EnableRateLimiting("sync-auth")]
    [RequestSizeLimit(8_192)]
    public async Task<IActionResult> AuthenticatePassword(
        [FromBody] SyncPasswordAuthDto request,
        CancellationToken cancellationToken)
    {
        var identifier = NormalizeIdentifier(request.Identificador);
        var password = request.Senha ?? string.Empty;
        if (!IsValidIdentifier(identifier) || password.Length is < 6 or > 256 ||
            Encoding.UTF8.GetByteCount(password) > 1024 || request.DispositivoId == Guid.Empty)
            return BadRequest(new { message = "Informe um e-mail, uma senha e um dispositivo válidos." });

        var document = NormalizeCompanyDocument(request.DocumentoEmpresa);
        if (document.Length > 0 && !IsValidCompanyDocument(document))
            return BadRequest(new { message = "Informe um CPF ou CNPJ válido." });

        var identifierHash = HashText(identifier);
        var documentHash = document.Length == 0 ? null : HashText(document);
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await LockAuthAsync(identifierHash, document, cancellationToken);

        var workspace = await _context.EspacosSincronizacao.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => documentHash == null
                ? x.IdentificadorHash == identifierHash
                : x.DocumentoEmpresaHash == documentHash, cancellationToken);
        if (workspace is null)
        {
            if (documentHash is null || await _context.EspacosSincronizacao.IgnoreQueryFilters()
                    .AnyAsync(x => x.IdentificadorHash == identifierHash, cancellationToken))
                return Unauthorized(new { message = "E-mail ou senha inválidos." });

            workspace = await GetOrCreateWorkspaceAsync(
                identifier, identifierHash, documentHash, request.NomeEmpresa, cancellationToken);
            workspace.SenhaCriacaoHash = SyncPasswordHasher.Hash(password);
        }
        else
        {
            if (!workspace.Ativo || workspace.DataHoraDeletado is not null ||
                workspace.IdentificadorHash != identifierHash)
                return Unauthorized(new { message = "E-mail ou senha inválidos." });

            var user = await FindSyncedUserAsync(workspace.Id, identifier, cancellationToken);
            if (user is not null)
            {
                var state = await _context.SyncEntityStates.AsNoTracking().FirstOrDefaultAsync(
                    x => x.EspacoId == workspace.Id && x.Entidade == "usuarios" &&
                         x.EntidadeId == user.UserId && !x.Excluido, cancellationToken);
                if (state is null) return Unauthorized(new { message = "E-mail ou senha inválidos." });
                using var data = JsonDocument.Parse(state.Dados);
                if (!TryGetString(data.RootElement, "senha", out var storedPassword) ||
                    !SyncPasswordHasher.Verify(password, storedPassword))
                    return Unauthorized(new { message = "E-mail ou senha inválidos." });
                workspace.SenhaCriacaoHash = storedPassword;
            }
            else
            {
                var hasSyncedUsers = await _context.SyncEntityStates.AsNoTracking().AnyAsync(
                    x => x.EspacoId == workspace.Id && x.Entidade == "usuarios" && !x.Excluido,
                    cancellationToken);
                if (hasSyncedUsers || !SyncPasswordHasher.Verify(password, workspace.SenhaCriacaoHash))
                    return Unauthorized(new { message = "E-mail ou senha inválidos." });
            }
        }

        var now = DateTime.UtcNow;
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
            DispositivoId = request.DispositivoId,
            NomeDispositivo = Truncate(request.NomeDispositivo?.Trim(), 160),
            Ativo = true,
            DataHoraCriado = now,
            DataHoraAtualizado = now
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var syncedUser = await FindSyncedUserAsync(workspace.Id, identifier, cancellationToken);
        return Ok(new
        {
            espacoId = workspace.Id,
            workspaceId = workspace.Id,
            nomeEmpresa = workspace.Nome,
            workspaceName = workspace.Nome,
            syncToken = accessKey,
            accessKey,
            expiresAt = now.Add(_security.TokenLifetime),
            userId = syncedUser?.UserId,
            userEmail = syncedUser?.Email,
            email = syncedUser?.Email
        });
    }
}
