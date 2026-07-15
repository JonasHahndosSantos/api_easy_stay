using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class EmpresaEntity : BaseEntity
{
    public string? Nome { get; set; }

    public string? Documento { get; set; }

    public string? Telefone { get; set; }

    public string? Email { get; set; }

    public string? Endereco { get; set; }

    public string? LogoUrl { get; set; }
}
