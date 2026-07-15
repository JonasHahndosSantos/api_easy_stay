using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class LancamentoFinanceiroEntity : BaseEntity
{
    public string? Tipo { get; set; }

    public string? Descricao { get; set; }

    public decimal Valor { get; set; }

    public DateTime DataLancamento { get; set; }

    public Guid? ReservaId { get; set; }

    public ReservaEntity? Reserva { get; set; }
}
