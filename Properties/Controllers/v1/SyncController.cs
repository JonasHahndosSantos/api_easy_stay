using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiEasyStay.Properties.Data;
using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiEasyStay.Properties.Controllers.v1;

[ApiController]
[Route("api/v1/sync")]
public sealed class SyncController : ControllerBase
{
    private const int MaxBatchSize = 250;
    private const int MaxEventBytes = 512_000;
    private static readonly HashSet<string> AllowedEntities =
    [
        "perfis", "clientes", "quartos", "usuarios", "reservas", "lancamentos_financeiros"
    ];

    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public SyncController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpGet("health")]
    public IActionResult Health([FromQuery] Guid espacoId)
    {
        var authorizationError = ValidateAccess();
        if (authorizationError is not null) return authorizationError;
        if (espacoId == Guid.Empty)
        {
            return BadRequest(new { message = "Informe o codigo da hospedagem." });
        }

        return Ok(new { online = true, serverTime = DateTime.UtcNow });
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push(
        [FromBody] SyncPushRequestDto request,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateAccess();
        if (authorizationError is not null) return authorizationError;
        if (request.EspacoId == Guid.Empty || request.DispositivoId == Guid.Empty)
        {
            return BadRequest(new { message = "Hospedagem e dispositivo sao obrigatorios." });
        }
        if (request.Eventos.Count > MaxBatchSize)
        {
            return BadRequest(new { message = $"Envie no maximo {MaxBatchSize} eventos por vez." });
        }

        var response = new SyncPushResponseDto();
        var knownEventIds = await _context.Sincronizacoes
            .AsNoTracking()
            .Where(x => x.EspacoId == request.EspacoId)
            .Select(x => x.EventoId)
            .ToHashSetAsync(cancellationToken);
        var reservationState = await LoadReservationState(request.EspacoId, cancellationToken);
        var timestamp = DateTime.UtcNow;
        var tick = 0L;

        foreach (var incoming in request.Eventos)
        {
            var entityName = incoming.Entidade.Trim().ToLowerInvariant();
            if (incoming.Id == Guid.Empty || !AllowedEntities.Contains(entityName))
            {
                return BadRequest(new { message = "O lote possui um evento invalido." });
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

            if (entityName == "reservas" && !incoming.Forcar && incoming.Operacao != "excluir")
            {
                var conflict = FindReservationConflict(incoming, reservationState.Values);
                if (conflict is not null)
                {
                    response.Conflitos.Add(conflict);
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
                Operacao = incoming.Operacao.Trim().ToLowerInvariant(),
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
        return Ok(response);
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull(
        [FromQuery] Guid espacoId,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var authorizationError = ValidateAccess();
        if (authorizationError is not null) return authorizationError;
        if (espacoId == Guid.Empty)
        {
            return BadRequest(new { message = "Informe o codigo da hospedagem." });
        }

        DateTime? after = null;
        if (!string.IsNullOrWhiteSpace(cursor) && DateTime.TryParse(cursor, out var parsed))
        {
            after = parsed.ToUniversalTime();
        }

        var query = _context.Sincronizacoes
            .AsNoTracking()
            .Where(x => x.EspacoId == espacoId);
        if (after.HasValue)
        {
            query = query.Where(x => x.DataHoraCriado > after.Value);
        }

        var events = await query
            .OrderBy(x => x.DataHoraCriado)
            .ThenBy(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        return Ok(new SyncPullResponseDto
        {
            Eventos = events.Select(ToPullDto).ToList(),
            Cursor = events.Count == 0 ? cursor : events[^1].DataHoraCriado.ToString("O")
        });
    }

    private IActionResult? ValidateAccess()
    {
        var configuredKey = _configuration["Sync:AccessKey"];
        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Length < 32)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Sincronizacao nao configurada no servidor." });
        }

        var suppliedKey = Request.Headers["X-EasyStay-Key"].ToString();
        if (suppliedKey.Length < 32)
        {
            return Unauthorized(new { message = "Chave de sincronizacao invalida." });
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        return CryptographicOperations.FixedTimeEquals(expected, supplied)
            ? null
            : Unauthorized(new { message = "Chave de sincronizacao invalida." });
    }

    private async Task<Dictionary<Guid, SincronizacaoEntity>> LoadReservationState(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var events = await _context.Sincronizacoes
            .AsNoTracking()
            .Where(x => x.EspacoId == workspaceId && x.Entidade == "reservas" && x.EntidadeId != null)
            .OrderBy(x => x.DataHoraCriado)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return events
            .GroupBy(x => x.EntidadeId!.Value)
            .ToDictionary(group => group.Key, group => group.Last());
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
                    Mensagem = "Ja existe outra reserva para este quarto no periodo informado.",
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
