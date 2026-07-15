using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class QuartoDto : BaseDto
{
    [Required]
    [JsonPropertyName("nome")]
    public string Nome { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("descricao")]
    public string Descricao { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("capacidade")]
    public int Capacidade { get; set; }

    [Required]
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("preco_diaria")]
    public decimal PrecoDiaria { get; set; }

    [JsonPropertyName("tipo_quarto_booking")]
    public string? TipoQuartoBooking { get; set; }
}