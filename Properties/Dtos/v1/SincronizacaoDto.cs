using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class SincronizacaoDto : BaseDto
{
    [Required]
    [JsonPropertyName("entidade")]
    public string Entidade { get; set; } = string.Empty;

    [JsonPropertyName("id_entidade")]
    public Guid? EntidadeId { get; set; }

    [Required]
    [JsonPropertyName("operacao")]
    public string? Operacao { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("dados")]
    public string? Dados { get; set; } = string.Empty;
    
    [JsonPropertyName("data_hora_sincronizado")]
    public DateTime? DataHoraSincronizado { get; set; }
}