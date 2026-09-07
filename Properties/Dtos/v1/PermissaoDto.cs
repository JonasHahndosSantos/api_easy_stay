using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos.v1;

public class PermissaoDto : BaseDto
{
    [Required]
    [JsonPropertyName("nome")]
    public string Nome { get; set; } = string.Empty;
}