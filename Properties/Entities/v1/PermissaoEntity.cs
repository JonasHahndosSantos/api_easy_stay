using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class PermissaoEntity : BaseEntity
{
    public string? Nome { get; set; }

    public ICollection<PerfilPermissaoEntity> PerfilPermissoes { get; set; } = [];
}
