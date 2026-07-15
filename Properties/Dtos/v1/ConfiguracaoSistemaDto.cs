using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ApiEasyStay.Properties.Entities.v1;

namespace ApiEasyStay.Properties.Dtos.v1;

public class ConfiguracaoSistemaDto : BaseDto
{
    [Required]
    [JsonPropertyName("nome_sistema")]
    public string NomeSistema { get; set; } = "Easy Stay"; 

    [Required]
    [JsonPropertyName("cor_primaria_modo_claro")]
    public string CorPrimariaModoClaro { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("cor_primaria_modo_escuro")]
    public string CorPrimariaModoEscuro { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("modo_escuro_padrao")]
    public bool ModoEscuroPadrao { get; set; }

    [JsonPropertyName("id_empresa")]
    public Guid? EmpresaId { get; set; }

    [JsonPropertyName("empresa")]
    public EmpresaDto? Empresa { get; set; }
}