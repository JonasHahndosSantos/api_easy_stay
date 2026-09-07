using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class ConfigPerfilEntity : BaseEntity
{
    public string? Nome { get; set; }

    public bool PerfilInterno { get; set; }

    public ICollection<UsuarioEntity> Usuarios { get; set; } = [];

    public ICollection<ConfigPerfilPermissaoEntity> PerfilPermissoes { get; set; } = [];
}
