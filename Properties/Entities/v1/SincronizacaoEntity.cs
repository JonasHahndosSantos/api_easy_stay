using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class SincronizacaoEntity : BaseEntity
{
    public string? Entidade { get; set; }

    public Guid? EntidadeId { get; set; }

    public string? Operacao { get; set; }

    public string? Dados { get; set; }

    public DateTime? DataHoraSincronizado { get; set; }
}
