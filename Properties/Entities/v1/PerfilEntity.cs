using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class PerfilEntity : BaseEntity
{
    public string? Nome { get; set; }

    public bool PerfilInterno { get; set; }

    public ICollection<UsuarioEntity> Usuarios { get; set; } = [];

    public ICollection<PerfilPermissaoEntity> PerfilPermissoes { get; set; } = [];
}
