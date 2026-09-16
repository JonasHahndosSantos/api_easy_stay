using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public sealed class SyncPushRequestDto
{
    [JsonPropertyName("espacoId")]
    public Guid EspacoId { get; set; }

    [JsonPropertyName("dispositivoId")]
    public Guid DispositivoId { get; set; }

    [JsonPropertyName("nomeDispositivo")]
    public string? NomeDispositivo { get; set; }

    [JsonPropertyName("eventos")]
    public List<SyncPushEventDto> Eventos { get; set; } = [];
}

public sealed class SyncPushEventDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("entidade")]
    public string Entidade { get; set; } = string.Empty;

    [JsonPropertyName("entidadeId")]
    public Guid? EntidadeId { get; set; }

    [JsonPropertyName("operacao")]
    public string Operacao { get; set; } = string.Empty;

    [JsonPropertyName("dados")]
    public JsonElement Dados { get; set; }

    [JsonPropertyName("dataHoraCriado")]
    public DateTime? DataHoraCriado { get; set; }

    [JsonPropertyName("forcar")]
    public bool Forcar { get; set; }
}

public sealed class SyncPushResponseDto
{
    [JsonPropertyName("eventosAceitos")]
    public List<Guid> EventosAceitos { get; set; } = [];

    [JsonPropertyName("conflitos")]
    public List<SyncConflictDto> Conflitos { get; set; } = [];

    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

public sealed class SyncConflictDto
{
    [JsonPropertyName("filaId")]
    public Guid FilaId { get; set; }

    [JsonPropertyName("entidade")]
    public string Entidade { get; set; } = string.Empty;

    [JsonPropertyName("entidadeId")]
    public Guid? EntidadeId { get; set; }

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("mensagem")]
    public string Mensagem { get; set; } = string.Empty;

    [JsonPropertyName("dadosLocais")]
    public JsonElement DadosLocais { get; set; }

    [JsonPropertyName("dadosRemotos")]
    public JsonElement DadosRemotos { get; set; }
}

public sealed class SyncPullResponseDto
{
    [JsonPropertyName("eventos")]
    public List<SyncPullEventDto> Eventos { get; set; } = [];

    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

public sealed class SyncPullEventDto
{
    [JsonPropertyName("idEvento")]
    public Guid IdEvento { get; set; }

    [JsonPropertyName("entidade")]
    public string Entidade { get; set; } = string.Empty;

    [JsonPropertyName("entidadeId")]
    public Guid? EntidadeId { get; set; }

    [JsonPropertyName("operacao")]
    public string Operacao { get; set; } = string.Empty;

    [JsonPropertyName("dados")]
    public JsonElement Dados { get; set; }

    [JsonPropertyName("dispositivoId")]
    public Guid DispositivoId { get; set; }

    [JsonPropertyName("nomeDispositivo")]
    public string? NomeDispositivo { get; set; }

    [JsonPropertyName("dataHoraServidor")]
    public DateTime DataHoraServidor { get; set; }
}
