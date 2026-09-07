using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/empresas")]
public class EmpresasController : BaseController<EmpresaEntity, EmpresaDto>
{
    public EmpresasController(IBaseService<EmpresaEntity, EmpresaDto> service) : base(service)
    {
    }
}