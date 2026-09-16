using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class SincronizacaoEntity : BaseEntity
{
    public Guid EspacoId { get; set; }

    public Guid DispositivoId { get; set; }

    public string? NomeDispositivo { get; set; }

    public Guid EventoId { get; set; }

    public string? Entidade { get; set; }

    public Guid? EntidadeId { get; set; }

    public string? Operacao { get; set; }

    public string? Dados { get; set; }

    public DateTime? DataHoraSincronizado { get; set; }

    public bool Forcado { get; set; }
}
