using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class PerfilDto : BaseDto
{
    [Required]
    [JsonPropertyName("nome")]
    public string Nome { get; set; } = string.Empty;

    [JsonPropertyName("perfil_interno")]
    public bool PerfilInterno { get; set; }

    [JsonPropertyName("usuarios")]
    public ICollection<UsuarioDto> Usuarios { get; set; } = [];

    [Required]
    [JsonPropertyName("perfil_permissoes")]
    public ICollection<PerfilPermissaoDto> PerfilPermissoes { get; set; } = [];
    
}