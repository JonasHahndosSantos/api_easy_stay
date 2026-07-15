using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class UsuarioEntity : BaseEntity
{
    public string? Nome { get; set; }

    public string? Email { get; set; }

    public string? Senha { get; set; }

    public bool Ativo { get; set; } = true;

    public Guid PerfilId { get; set; }

    public PerfilEntity? Perfil { get; set; }
}
