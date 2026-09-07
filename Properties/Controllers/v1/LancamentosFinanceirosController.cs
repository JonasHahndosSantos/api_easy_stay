using ApiEasyStay.Properties.Dtos.v1;
using ApiEasyStay.Properties.Entities.v1;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers.v1;

[Route("api/v1/lancamentos-financeiros")]
public class LancamentosFinanceirosController : BaseController<LancamentoFinanceiroEntity, LancamentoFinanceiroDto>
{
    public LancamentosFinanceirosController(IBaseService<LancamentoFinanceiroEntity, LancamentoFinanceiroDto> service) : base(service)
    {
    }
}