using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/quartos")]
public class QuartosController : BaseController<QuartoEntity, QuartoDto>
{
    public QuartosController(IBaseService<QuartoEntity, QuartoDto> service) : base(service)
    {
    }
}