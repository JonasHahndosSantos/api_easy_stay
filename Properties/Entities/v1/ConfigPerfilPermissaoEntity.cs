namespace ApiEasyStay.Properties.Entities.v1;

public class ConfigPerfilPermissaoEntity : BaseEntity
{
    public Guid PerfilId { get; set; }

    public ConfigPerfilEntity? Perfil { get; set; }

    public Guid PermissaoId { get; set; }

    public ConfigPermissaoEntity? Permissao { get; set; }
}
