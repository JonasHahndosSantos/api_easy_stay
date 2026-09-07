using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/usuarios")]
public class UsuariosController : BaseController<UsuarioEntity, UsuarioDto>
{
    public UsuariosController(IBaseService<UsuarioEntity, UsuarioDto> service) : base(service)
    {
    }
}