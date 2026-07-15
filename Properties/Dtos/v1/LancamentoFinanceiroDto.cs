using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class LancamentoFinanceiroDto : BaseDto
{
    [Required]
    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("descricao")]    
    public string Descricao { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("valor")]
    public decimal Valor { get; set; }

    [Required]
    [JsonPropertyName("data_lancamento")]
    public DateTime DataLancamento { get; set; }

    [JsonPropertyName("id_reserva")]
    public Guid? ReservaId { get; set; }
 
    [JsonPropertyName("reserva")]
    public ReservaDto? Reserva { get; set; }
    
}