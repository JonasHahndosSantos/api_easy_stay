using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class ConfigPermissaoEntity : BaseEntity
{
    public string? Nome { get; set; }

    public ICollection<ConfigPerfilPermissaoEntity> PerfilPermissoes { get; set; } = [];
}
