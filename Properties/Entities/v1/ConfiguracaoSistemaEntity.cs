using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class ConfiguracaoSistemaEntity : BaseEntity
{
    public string NomeSistema { get; set; } = "Easy Stay";

    public string? CorPrimariaModoClaro { get; set; }

    public string? CorPrimariaModoEscuro { get; set; }

    public bool ModoEscuroPadrao { get; set; }

    public Guid? EmpresaId { get; set; }

    public EmpresaEntity? Empresa { get; set; }
}
