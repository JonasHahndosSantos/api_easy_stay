using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class UsuarioDto : BaseDto
{
    [Required]
    [JsonPropertyName("nome")]
    public string Nome { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [JsonPropertyName("senha")]
    public string Senha { get; set; } = string.Empty;

    [JsonPropertyName("ativo")]
    public bool Ativo { get; set; } = true;
    
    [Required]
    [JsonPropertyName("id_perfil")]
    public Guid PerfilId { get; set; }
}