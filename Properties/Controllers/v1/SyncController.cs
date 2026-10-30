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

[ApiController]
[Route("api/v1/sync")]
[EnableRateLimiting("sync")]
public sealed partial class SyncController : ControllerBase
{
    private const int MaxBatchSize = 250;
    private const int MaxEventBytes = 512_000;
    private static readonly TimeSpan AuthCodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> AllowedEntities =
    [
        "dados_empresa", "perfis", "clientes", "quartos", "usuarios", "reservas", "lancamentos_financeiros"
    ];

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SyncController> _logger;
    private readonly SyncSecurity _security;
    private readonly AuthCodeSender _codeSender;

    public SyncController(
        AppDbContext context,
        IWebHostEnvironment environment,
        ILogger<SyncController> logger,
        SyncSecurity security,
        AuthCodeSender codeSender)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
        _security = security;
        _codeSender = codeSender;
    }

    private Task LockWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({workspaceId.ToString()}, 0))", cancellationToken);

    private async Task<IActionResult?> ValidateDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var hash = HashKey(GetSuppliedKey());
        var token = await _context.SyncAccessTokens.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TokenHash == hash && x.Ativo, cancellationToken);
        return token?.DispositivoId is Guid registered && registered != deviceId
            ? Unauthorized(new { message = "Este acesso pertence a outro dispositivo." })
            : null;
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
                code = "workspace_not_found",
                message = "Hospedagem não cadastrada. Crie-a no primeiro dispositivo."
            });
        }

        var suppliedKey = GetSuppliedKey();
        if (!IsValidKey(suppliedKey))
        {
            return Unauthorized(new { message = "Chave de sincronização inválida." });
        }

        var keyHash = HashKey(suppliedKey);
        var validSince = DateTime.UtcNow.Subtract(_security.TokenLifetime);
        var validDeviceToken = await _context.SyncAccessTokens
            .AsNoTracking()
            .AnyAsync(
                x => x.EspacoId == workspaceId &&
                     x.Ativo &&
                     x.DataHoraCriado >= validSince &&
                     x.TokenHash == keyHash,
                cancellationToken);
        if (!(_environment.IsDevelopment() && MatchesKey(workspace.ChaveHash, suppliedKey)) && !validDeviceToken)
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

    [HttpGet("state")]
    public async Task<IActionResult> CurrentState(
        [FromQuery] Guid espacoId,
        [FromQuery] bool incluirExcluidos,
        CancellationToken cancellationToken)
    {
        if (espacoId == Guid.Empty)
        {
            return BadRequest(new { message = "Informe o codigo da hospedagem." });
        }
        var authorizationError = await ValidateAccessAsync(espacoId, cancellationToken);
        if (authorizationError is not null) return authorizationError;

        var query = _context.SyncEntityStates
            .AsNoTracking()
            .Where(x => x.EspacoId == espacoId);
        if (!incluirExcluidos)
        {
            query = query.Where(x => !x.Excluido);
        }

        var states = await query
            .OrderBy(x => x.Entidade)
            .ThenBy(x => x.DataHoraAtualizado)
            .Select(x => new
            {
                entidade = x.Entidade,
                entidadeId = x.EntidadeId,
                excluido = x.Excluido,
                dados = x.Dados,
                dataHoraAtualizado = x.DataHoraAtualizado
            })
            .ToListAsync(cancellationToken);
        return Ok(new { total = states.Count, entidades = states });
    }

    private string GetSuppliedKey() => Request.Headers["X-EasyStay-Key"].ToString();

    private static bool IsValidKey(string key) => key.Length is >= 32 and <= 256;

    private static string NormalizeIdentifier(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string OnlyDigits(string value)
    {
        var digits = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is >= '0' and <= '9') digits.Append(character);
        }
        return digits.ToString();
    }

    private static string NormalizeCompanyDocument(string? value) =>
        OnlyDigits(value ?? string.Empty);

    private static bool IsValidCompanyDocument(string document)
    {
        if (document.Length is not (11 or 14) ||
            document.Any(character => character is < '0' or > '9') ||
            document.All(character => character == document[0]))
        {
            return false;
        }

        var digits = document.Select(character => character - '0').ToArray();
        if (digits.Length == 11)
        {
            var firstSum = Enumerable.Range(0, 9)
                .Sum(index => digits[index] * (10 - index));
            var firstDigit = firstSum * 10 % 11;
            if (firstDigit == 10) firstDigit = 0;
            var secondSum = Enumerable.Range(0, 10)
                .Sum(index => digits[index] * (11 - index));
            var secondDigit = secondSum * 10 % 11;
            if (secondDigit == 10) secondDigit = 0;
            return digits[9] == firstDigit && digits[10] == secondDigit;
        }

        int CalculateCnpjDigit(int count)
        {
            var sum = 0;
            var weight = count == 12 ? 5 : 6;
            for (var index = 0; index < count; index++)
            {
                sum += digits[index] * weight;
                weight = weight == 2 ? 9 : weight - 1;
            }
            var remainder = sum % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }

        return digits[12] == CalculateCnpjDigit(12) &&
               digits[13] == CalculateCnpjDigit(13);
    }

    private static bool IsValidIdentifier(string identifier)
    {
        return identifier.Contains('@') && identifier.Length <= 254 &&
               MailAddress.TryCreate(identifier, out var address) && address.Address == identifier &&
               !identifier.Any(char.IsWhiteSpace);
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string expectedHex, string suppliedHex)
    {
        if (expectedHex.Length != suppliedHex.Length) return false;
        var expected = Encoding.UTF8.GetBytes(expectedHex);
        var supplied = Encoding.UTF8.GetBytes(suppliedHex);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static string GenerateAccessKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private async Task LockAuthAsync(string identifierHash, string document, CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"auth-contact:" + identifierHash}, 0))", cancellationToken);
        if (!string.IsNullOrEmpty(document))
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"auth-company:" + HashText(document)}, 0))", cancellationToken);
    }

    private async Task<EspacoSincronizacaoEntity> GetOrCreateWorkspaceAsync(
        string identifier,
        string identifierHash,
        string? companyDocumentHash,
        string? companyName,
        CancellationToken cancellationToken)
    {
        var workspace = await _context.EspacosSincronizacao
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => companyDocumentHash != null
                    ? x.DocumentoEmpresaHash == companyDocumentHash
                    : x.IdentificadorHash == identifierHash,
                cancellationToken);
        if (workspace is not null)
        {
            if (workspace.DataHoraDeletado is not null || !workspace.Ativo)
            {
                workspace.Ativo = true;
                workspace.DataHoraDeletado = null;
            }
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                workspace.Nome = Truncate(companyName.Trim(), 160);
            }
            else if (string.IsNullOrWhiteSpace(workspace.Nome))
            {
                workspace.Nome = BuildWorkspaceName(identifier);
            }
            if (workspace.IdentificadorHash is null)
            {
                workspace.IdentificadorHash = identifierHash;
            }
            if (companyDocumentHash is not null &&
                workspace.DocumentoEmpresaHash is null)
            {
                workspace.DocumentoEmpresaHash = companyDocumentHash;
            }
            workspace.DataHoraAtualizado = DateTime.UtcNow;
            return workspace;
        }

        var now = DateTime.UtcNow;
        workspace = new EspacoSincronizacaoEntity
        {
            Id = Guid.NewGuid(),
            IdentificadorHash = identifierHash,
            DocumentoEmpresaHash = companyDocumentHash,
            ChaveHash = HashKey(GenerateAccessKey()),
            Nome = string.IsNullOrWhiteSpace(companyName)
                ? BuildWorkspaceName(identifier)
                : Truncate(companyName.Trim(), 160),
            Ativo = true,
            DataHoraCriado = now,
            DataHoraAtualizado = now
        };
        await _context.EspacosSincronizacao.AddAsync(workspace, cancellationToken);
        return workspace;
    }

    private async Task<string> UpsertCurrentStateAsync(
        Guid workspaceId,
        string entity,
        Guid entityId,
        string operation,
        string data,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        var state = _context.SyncEntityStates.Local.FirstOrDefault(
            x => x.EspacoId == workspaceId &&
                 x.Entidade == entity &&
                 x.EntidadeId == entityId);
        state ??= await _context.SyncEntityStates.FirstOrDefaultAsync(
            x => x.EspacoId == workspaceId &&
                 x.Entidade == entity &&
                 x.EntidadeId == entityId,
            cancellationToken);
        if (state is null)
        {
            await _context.SyncEntityStates.AddAsync(new SyncEntityStateEntity
            {
                Id = Guid.NewGuid(),
                EspacoId = workspaceId,
                Entidade = entity,
                EntidadeId = entityId,
                Dados = data,
                Excluido = operation == "excluir" || SyncPayload.IsDeleted(data),
                DataHoraOrigem = timestamp,
                DataHoraCriado = timestamp,
                DataHoraAtualizado = timestamp
            }, cancellationToken);
            return data;
        }

        data = SyncPayload.Merge(state.Dados, data);
        state.Dados = data;
        state.Excluido = operation == "excluir" || SyncPayload.IsDeleted(data);
        state.DataHoraOrigem = timestamp;
        state.DataHoraAtualizado = timestamp;
        return data;
    }

    private async Task<SyncEntityStateEntity?> FindCurrentStateAsync(
        Guid workspaceId, string entity, Guid entityId, CancellationToken cancellationToken) =>
        _context.SyncEntityStates.Local.FirstOrDefault(x =>
            x.EspacoId == workspaceId && x.Entidade == entity && x.EntidadeId == entityId) ??
        await _context.SyncEntityStates.FirstOrDefaultAsync(x =>
            x.EspacoId == workspaceId && x.Entidade == entity && x.EntidadeId == entityId, cancellationToken);

    private async Task<Guid?> FindLatestVersionAsync(Guid workspaceId, string entity, Guid entityId, CancellationToken cancellationToken)
    {
        var pending = _context.Sincronizacoes.Local.Where(x => x.EspacoId == workspaceId &&
            x.Entidade == entity && x.EntidadeId == entityId).OrderByDescending(x => x.DataHoraCriado).FirstOrDefault();
        return pending?.EventoId ?? await _context.Sincronizacoes.AsNoTracking()
            .Where(x => x.EspacoId == workspaceId && x.Entidade == entity && x.EntidadeId == entityId)
            .OrderByDescending(x => x.DataHoraCriado).ThenByDescending(x => x.Id)
            .Select(x => (Guid?)x.EventoId).FirstOrDefaultAsync(cancellationToken);
    }

    private static string BuildWorkspaceName(string identifier)
    {
        if (identifier.Contains('@'))
        {
            var parts = identifier.Split('@', 2);
            var name = parts[0].Length <= 2
                ? parts[0]
                : $"{parts[0][..2]}***";
            return $"Easy Stay - {name}@{parts[1]}";
        }

        var masked = identifier.Length <= 4
            ? identifier
            : $"***{identifier[^4..]}";
        return $"Easy Stay - {masked}";
    }


    private async Task<SyncedUser?> FindSyncedUserAsync(
        Guid workspaceId,
        string identifier,
        CancellationToken cancellationToken)
    {
        var states = await _context.SyncEntityStates
            .AsNoTracking()
            .Where(x => x.EspacoId == workspaceId && x.Entidade == "usuarios" && !x.Excluido)
            .OrderBy(x => x.EntidadeId)
            .ToListAsync(cancellationToken);

        foreach (var state in states)
        {
            using var document = JsonDocument.Parse(state.Dados);
            var data = document.RootElement;
            if (HasNonNullProperty(data, "DataHoraDeletado") ||
                TryGetProperty(data, "ativo", out var activeValue) && activeValue.ValueKind == JsonValueKind.False ||
                TryGetInt(data, "ativo", out var active) && active == 0)
            {
                continue;
            }

            var email = TryGetString(data, "email", out var foundEmail)
                ? foundEmail.Trim()
                : null;
            var user = new SyncedUser(state.EntidadeId, email);
            if (identifier.Contains('@') &&
                email is not null &&
                string.Equals(email, identifier, StringComparison.OrdinalIgnoreCase))
            {
                return user;
            }
        }

        return null;
    }

    private static bool HasNonNullProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

    private static string ReadUserEmail(string data)
    {
        using var json = JsonDocument.Parse(data);
        return TryGetString(json.RootElement, "email", out var email) ? email.Trim() : "";
    }

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
        return await LoadEntityState(workspaceId, "reservas", cancellationToken);
    }

    private async Task<Dictionary<Guid, SincronizacaoEntity>> LoadCustomerState(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        return await LoadEntityState(workspaceId, "clientes", cancellationToken);
    }

    private async Task<Dictionary<Guid, SincronizacaoEntity>> LoadEntityState(
        Guid workspaceId,
        string entity,
        CancellationToken cancellationToken)
    {
        var states = await _context.SyncEntityStates.AsNoTracking()
            .Where(x => x.EspacoId == workspaceId && x.Entidade == entity)
            .ToListAsync(cancellationToken);
        var current = states.ToDictionary(x => x.EntidadeId);
        foreach (var tracked in _context.SyncEntityStates.Local.Where(x => x.EspacoId == workspaceId && x.Entidade == entity))
            current[tracked.EntidadeId] = tracked;
        return current.Values.ToDictionary(x => x.EntidadeId, x => new SincronizacaoEntity
        {
            Entidade = x.Entidade,
            EntidadeId = x.EntidadeId,
            Operacao = x.Excluido ? "excluir" : "atualizar",
            Dados = x.Dados
        });
    }

    private static SyncConflictDto? FindReservationConflict(
        SyncPushEventDto incoming,
        IEnumerable<SincronizacaoEntity> currentReservations)
    {
        var local = ParseReservation(incoming.Dados, incoming.EntidadeId, incoming.Operacao);
        if (local is null || !local.IsActive) return null;

        foreach (var current in currentReservations)
        {
            if (current.EntidadeId == incoming.EntidadeId ||
                string.IsNullOrWhiteSpace(current.Dados))
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

    private static SyncConflictDto? FindCustomerConflict(
        SyncPushEventDto incoming,
        IEnumerable<SincronizacaoEntity> currentCustomers)
    {
        var local = ParseCustomer(incoming.Dados, incoming.EntidadeId, incoming.Operacao);
        if (local is null || !local.IsActive) return null;

        foreach (var current in currentCustomers)
        {
            if (current.EntidadeId == incoming.EntidadeId ||
                string.IsNullOrWhiteSpace(current.Dados))
            {
                continue;
            }
            using var document = JsonDocument.Parse(current.Dados);
            var remote = ParseCustomer(document.RootElement, current.EntidadeId, current.Operacao);
            if (remote is null || !remote.IsActive ||
                !string.Equals(local.Document, remote.Document, StringComparison.Ordinal))
            {
                continue;
            }
            return new SyncConflictDto
            {
                FilaId = incoming.Id,
                Entidade = "clientes",
                EntidadeId = incoming.EntidadeId,
                Tipo = "cliente_cpf_duplicado",
                Mensagem = "Já existe um cliente com este CPF nesta hospedagem.",
                DadosLocais = incoming.Dados.Clone(),
                DadosRemotos = document.RootElement.Clone()
            };
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
        if (start >= end || !Guid.TryParse(room, out var roomId) || roomId == Guid.Empty) return null;
        var status = TryGetInt(data, "status", out var value) ? value : 1;
        var deleted = TryGetProperty(data, "DataHoraDeletado", out var deletedValue) &&
                      deletedValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        return new ReservationSnapshot(id, roomId, start, end, status != 4 && !deleted);
    }

    private static CustomerSnapshot? ParseCustomer(
        JsonElement data,
        Guid? id,
        string? operation)
    {
        if (operation == "excluir" || data.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetString(data, "CPF", out var document) &&
            !TryGetString(data, "documentNumber", out document))
        {
            return null;
        }
        document = OnlyDigits(document);
        if (document.Length != 11) return null;
        var deleted = TryGetProperty(data, "DataHoraDeletado", out var deletedValue) &&
                      deletedValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        return new CustomerSnapshot(id, document, !deleted);
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(element, name, out var property)) return false;
        value = property.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetDate(JsonElement element, string name, out DateTime value)
    {
        value = default;
        return TryGetProperty(element, name, out var property) &&
               DateTime.TryParse(property.ToString(), CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out value);
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = default;
        return TryGetProperty(element, name, out var property) &&
               int.TryParse(property.ToString(), out value);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
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
        Guid RoomId,
        DateTime Start,
        DateTime End,
        bool IsActive);

    private sealed record CustomerSnapshot(
        Guid? Id,
        string Document,
        bool IsActive);

    private sealed record SyncedUser(Guid UserId, string? Email);
}
