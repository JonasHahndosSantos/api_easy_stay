using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/sincronizacoes")]
public class SincronizacoesController : BaseController<SincronizacaoEntity, SincronizacaoDto>
{
    public SincronizacoesController(IBaseService<SincronizacaoEntity, SincronizacaoDto> service) : base(service)
    {
    }
}