using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class ReservaDto : BaseDto
{
    [Required]
    [JsonPropertyName("id_quarto")]
    public Guid QuartoId { get; set; }

    [JsonPropertyName("quarto")]
    public QuartoDto? Quarto { get; set; }

    [Required]
    [JsonPropertyName("id_cliente")]
    public Guid ClienteId { get; set; }

    [JsonPropertyName("cliente")]
    public ClienteDto? Cliente { get; set; }

    [Required]
    [JsonPropertyName("id_criado_por_usuario")]
    public Guid CriadoPorUsuarioId { get; set; }

    [JsonPropertyName("criado_por_usuario")]
    public UsuarioDto? CriadoPorUsuario { get; set; }

    [Required]
    [JsonPropertyName("data_entrada")]
    public DateTime DataEntrada { get; set; }

    [Required]
    [JsonPropertyName("data_saida")]
    public DateTime DataSaida { get; set; }

    [Required]
    [JsonPropertyName("quantidade_hospedes")]
    public int QuantidadeHospedes { get; set; }

    [Required]
    [JsonPropertyName("valor_total")]
    public decimal ValorTotal { get; set; }

    [Required]
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("observacoes")]
    public string? Observacoes { get; set; }
}