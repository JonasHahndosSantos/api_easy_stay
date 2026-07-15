using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class ClienteDto : BaseDto
{
    [Required] 
    [JsonPropertyName("nome")] 
    public string Nome { get; set; } = string.Empty;

    [Required] 
    [JsonPropertyName("cpf")] 
    public string Cpf { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("telefone")]
    public string Telefone { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("endereco")]
    public string Endereco { get; set; } = string.Empty;
}