using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class EmpresaDto : BaseDto
{
    [Required]
    [JsonPropertyName("nome")]
    public string Nome { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("documento")]
    public string Documento { get; set; } = string.Empty;

    [Required]
    [Phone]
    [JsonPropertyName("telefone")]
    public string Telefone { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("endereco")]
    public string Endereco { get; set; } = string.Empty;

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; set; }
}