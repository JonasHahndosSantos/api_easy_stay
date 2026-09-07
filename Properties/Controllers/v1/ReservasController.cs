using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/reservas")]
public class ReservasController : BaseController<ReservaEntity, ReservaDto>
{
    public ReservasController(IBaseService<ReservaEntity, ReservaDto> service) : base(service)
    {
    }
}