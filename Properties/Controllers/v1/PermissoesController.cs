using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/permissoes")]
public class PermissoesController : BaseController<ConfigPermissaoEntity, PermissaoDto>
{
    public PermissoesController(IBaseService<ConfigPermissaoEntity, PermissaoDto> service) : base(service)
    {
    }
}