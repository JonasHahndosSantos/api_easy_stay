using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly TimeSpan AuthCodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> AllowedEntities =
    [
        "dados_empresa", "perfis", "clientes", "quartos", "usuarios", "reservas", "lancamentos_financeiros"
    ];

    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        AppDbContext context,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<SyncController> logger)
    {
        _context = context;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

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
            hasData,
            espacoId = workspace?.Id,
            workspaceId = workspace?.Id,
            nomeEmpresa = workspace?.Nome,
            workspaceName = workspace?.Nome
        });
    }

    [HttpPost("auth/request-code")]
    public async Task<IActionResult> RequestCode(
        [FromBody] SyncAuthCodeRequestDto request,
        CancellationToken cancellationToken)
    {
        var identifier = NormalizeIdentifier(request.Identificador);
        if (!IsValidIdentifier(identifier))
        {
            return BadRequest(new { message = "Informe um e-mail ou celular válido." });
        }
        var now = DateTime.UtcNow;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
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
            CodigoHash = HashAuthCode(identifier, code),
            ExpiraEm = now.Add(AuthCodeLifetime),
            NomeDispositivo = Truncate(request.NomeDispositivo?.Trim(), 160),
            DataHoraCriado = now,
            DataHoraAtualizado = now
        }, cancellationToken);

        var deliveredBy = await SendAuthCodeAsync(identifier, code, cancellationToken);

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
    public async Task<IActionResult> VerifyCode(
        [FromBody] SyncAuthCodeVerifyDto request,
        CancellationToken cancellationToken)
    {
        var identifier = NormalizeIdentifier(request.Identificador);
        var code = OnlyDigits(request.Codigo);
        if (!IsValidIdentifier(identifier) || code.Length != 6)
        {
            return BadRequest(new { message = "Código ou contato inválido." });
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
        if (!FixedTimeEquals(authCode.CodigoHash, HashAuthCode(identifier, code)))
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
            userId = user?.UserId,
            userEmail = user?.Email,
            email = user?.Email
        });
    }

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
                    var fullData = JsonNode.Parse(original)!.AsObject();
                    foreach (var item in data) fullData[item.Key] = item.Value?.DeepClone();
                    data = fullData;
                }
                if (state.Excluido && data["DataHoraDeletado"] is null)
                    data["DataHoraDeletado"] = state.DataHoraOrigem;
                return new SyncPullEventDto
                {
                    IdEvento = state.Id,
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
            var reservationState = await LoadReservationState(request.EspacoId, cancellationToken);
            var customerState = await LoadCustomerState(request.EspacoId, cancellationToken);
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
                await UpsertCurrentStateAsync(
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
        if (!IsValidKey(suppliedKey))
        {
            return Unauthorized(new { message = "Chave de sincronização inválida." });
        }

        var keyHash = HashKey(suppliedKey);
        var validDeviceToken = await _context.SyncAccessTokens
            .AsNoTracking()
            .AnyAsync(
                x => x.EspacoId == workspaceId &&
                     x.Ativo &&
                     x.TokenHash == keyHash,
                cancellationToken);
        if (!MatchesKey(workspace.ChaveHash, suppliedKey) && !validDeviceToken)
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

    private static string NormalizeIdentifier(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Contains('@'))
        {
            return text.ToLowerInvariant();
        }
        return OnlyDigits(text);
    }

    private static string OnlyDigits(string value)
    {
        var digits = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsDigit(character)) digits.Append(character);
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
        if (identifier.Contains('@'))
        {
            return identifier.Length <= 254 &&
                   identifier.Count(character => character == '@') == 1 &&
                   identifier.IndexOf('@') > 0 &&
                   identifier.LastIndexOf('@') < identifier.Length - 1;
        }
        return identifier.Length is >= 10 and <= 15;
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string HashAuthCode(string identifier, string code) =>
        HashText($"{identifier}:{code}");

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

    private async Task UpsertCurrentStateAsync(
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
                Excluido = operation == "excluir",
                DataHoraOrigem = timestamp,
                DataHoraCriado = timestamp,
                DataHoraAtualizado = timestamp
            }, cancellationToken);
            return;
        }

        if (operation == "excluir")
        {
            var previous = JsonNode.Parse(state.Dados)!.AsObject();
            foreach (var item in JsonNode.Parse(data)!.AsObject())
                previous[item.Key] = item.Value?.DeepClone();
            data = previous.ToJsonString();
        }
        state.Dados = data;
        state.Excluido = operation == "excluir";
        state.DataHoraOrigem = timestamp;
        state.DataHoraAtualizado = timestamp;
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

    private async Task<string> SendAuthCodeAsync(
        string identifier,
        string code,
        CancellationToken cancellationToken)
    {
        var smtpHost = _configuration["Smtp:Host"];
        if (!identifier.Contains('@') || string.IsNullOrWhiteSpace(smtpHost))
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "Código local Easy Stay para {Identifier}: {Code}",
                    identifier,
                    code);
                return "development-console";
            }

            throw new InvalidOperationException(
                "Configure Smtp:Host para enviar o código por e-mail.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(
                _configuration["Smtp:From"] ?? "noreply@easystay.local",
                _configuration["Smtp:FromName"] ?? "Easy Stay"),
            Subject = "Código de acesso Easy Stay",
            Body = $"Seu código de acesso Easy Stay é {code}. Ele expira em 10 minutos.",
            IsBodyHtml = false
        };
        message.To.Add(identifier);

        using var smtp = new SmtpClient(smtpHost)
        {
            Port = int.TryParse(_configuration["Smtp:Port"], out var port) ? port : 587,
            EnableSsl = bool.TryParse(_configuration["Smtp:EnableSsl"], out var ssl) && ssl
        };
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            smtp.Credentials = new NetworkCredential(username, password);
        }

        await smtp.SendMailAsync(message, cancellationToken);
        return "email";
    }

    private async Task<SyncedUser?> FindSyncedUserAsync(
        Guid workspaceId,
        string identifier,
        CancellationToken cancellationToken)
    {
        var events = await _context.Sincronizacoes
            .AsNoTracking()
            .Where(x => x.EspacoId == workspaceId && x.Entidade == "usuarios")
            .OrderByDescending(x => x.DataHoraCriado)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var seenUsers = new HashSet<Guid>();
        SyncedUser? firstActiveUser = null;
        foreach (var syncEvent in events)
        {
            if (syncEvent.EntidadeId is null ||
                !seenUsers.Add(syncEvent.EntidadeId.Value))
            {
                continue;
            }
            using var document = JsonDocument.Parse(syncEvent.Dados ?? "{}");
            var data = document.RootElement;
            if (syncEvent.Operacao == "excluir" ||
                HasNonNullProperty(data, "DataHoraDeletado") ||
                TryGetInt(data, "ativo", out var active) && active == 0)
            {
                continue;
            }

            var email = TryGetString(data, "email", out var foundEmail)
                ? foundEmail.Trim()
                : null;
            var user = new SyncedUser(syncEvent.EntidadeId.Value, email);
            firstActiveUser ??= user;
            if (identifier.Contains('@') &&
                email is not null &&
                string.Equals(email, identifier, StringComparison.OrdinalIgnoreCase))
            {
                return user;
            }
        }

        return identifier.Contains('@') ? null : firstActiveUser;
    }

    private static bool HasNonNullProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

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

    private async Task<Dictionary<Guid, SincronizacaoEntity>> LoadCustomerState(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var events = await _context.Sincronizacoes
            .FromSqlInterpolated($"""
                SELECT DISTINCT ON (entidade_id) *
                FROM sincronizacoes
                WHERE espaco_id = {workspaceId}
                  AND entidade = 'clientes'
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
        if (start >= end || !Guid.TryParse(room, out _)) return null;
        var status = TryGetInt(data, "status", out var value) ? value : 1;
        var deleted = data.TryGetProperty("DataHoraDeletado", out var deletedValue) &&
                      deletedValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        return new ReservationSnapshot(id, room, start, end, status != 4 && !deleted);
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
        var deleted = data.TryGetProperty("DataHoraDeletado", out var deletedValue) &&
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
               DateTime.TryParse(property.ToString(), out value);
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
        string RoomId,
        DateTime Start,
        DateTime End,
        bool IsActive);

    private sealed record CustomerSnapshot(
        Guid? Id,
        string Document,
        bool IsActive);

    private sealed record SyncedUser(Guid UserId, string? Email);
}
