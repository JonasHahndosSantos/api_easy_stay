using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class PerfilPermissaoDto : BaseDto
{
    [JsonPropertyName("id_perfil")]
    public Guid PerfilId { get; set; }

    [JsonPropertyName("id_permissao")]
    public Guid PermissaoId { get; set; }

}