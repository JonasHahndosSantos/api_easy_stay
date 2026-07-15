using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEasyStay.Properties.Dtos;

public abstract class BaseDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("data_hora_criado")]
    public DateTime DataHoraCriado { get; set; }

    [JsonPropertyName("data_hora_atualizado")]
    public DateTime? DataHoraAtualizado { get; set; }

    [JsonPropertyName("data_hora_deletado")]
    public DateTime? DataHoraDeletado { get; set; }
}