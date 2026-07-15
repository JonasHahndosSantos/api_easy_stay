namespace ApiEasyStay.Properties.Entities;

public abstract class BaseEntity
{
    public Guid Id { get; set; }

    public DateTime DataHoraCriado { get; set; }

    public DateTime? DataHoraAtualizado { get; set; }

    public DateTime? DataHoraDeletado { get; set; }
}
