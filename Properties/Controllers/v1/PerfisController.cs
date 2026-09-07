using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/perfis")]
public class PerfisController : BaseController<ConfigPerfilEntity, PerfilDto>
{
    public PerfisController(IBaseService<ConfigPerfilEntity, PerfilDto> service) : base(service)
    {
    }
}