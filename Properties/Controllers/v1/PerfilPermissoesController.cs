using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/perfil-permissoes")]
public class PerfilPermissoesController : BaseController<ConfigPerfilPermissaoEntity, PerfilPermissaoDto>
{
    public PerfilPermissoesController(IBaseService<ConfigPerfilPermissaoEntity, PerfilPermissaoDto> service) : base(service)
    {
    }
}