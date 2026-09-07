using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/clientes")]
public class ClientesController : BaseController<ClienteEntity, ClienteDto>
{
    public ClientesController(IBaseService<ClienteEntity, ClienteDto> service) : base(service)
    {
    }
}