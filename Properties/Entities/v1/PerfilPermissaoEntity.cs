using ApiEasyStay.Properties.Entities;

namespace ApiEasyStay.Properties.Entities.v1;

public class PerfilPermissaoEntity : BaseEntity
{
    public Guid PerfilId { get; set; }

    public PerfilEntity? Perfil { get; set; }

    public Guid PermissaoId { get; set; }

    public PermissaoEntity? Permissao { get; set; }
}
