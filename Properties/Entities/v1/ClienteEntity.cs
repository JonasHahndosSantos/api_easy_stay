using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class ClienteEntity : BaseEntity
{
    public string? Nome { get; set; }

    public string? Cpf { get; set; }

    public string? Telefone { get; set; }

    public string? Endereco { get; set; }
}
