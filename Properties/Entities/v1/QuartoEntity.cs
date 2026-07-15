using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class QuartoEntity : BaseEntity
{
    public string? Nome { get; set; }

    public string? Descricao { get; set; }

    public int Capacidade { get; set; }

    public int Status { get; set; }

    public decimal PrecoDiaria { get; set; }

    public string? TipoQuartoBooking { get; set; }
}
