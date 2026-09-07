using ApiEasyStay.Properties.Dtos;
using ApiEasyStay.Properties.Entities;
using ApiEasyStay.Properties.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers;

[ApiController]
public abstract class BaseController<TEntity, TDto> : ControllerBase
    where TEntity : BaseEntity
    where TDto : BaseDto
{
    private readonly IBaseService<TEntity, TDto> _service;

    protected BaseController(IBaseService<TEntity, TDto> service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TDto>>> Listar(CancellationToken cancellationToken)
    {
        var data = await _service.ListarAsync(cancellationToken);
        return Ok(data);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TDto>> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var data = await _service.ObterPorIdAsync(id, cancellationToken);
        if (data is null)
        {
            return NotFoundResponse();
        }

        return Ok(data);
    }

    [HttpPost]
    public async Task<ActionResult<TDto>> Adicionar([FromBody] TDto dto, CancellationToken cancellationToken)
    {
        var created = await _service.AdicionarAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(ObterPorId), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TDto>> Atualizar(Guid id, [FromBody] TDto dto, CancellationToken cancellationToken)
    {
        var updated = await _service.AtualizarAsync(id, dto, cancellationToken);
        if (updated is null)
        {
            return NotFoundResponse();
        }

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remover(Guid id, CancellationToken cancellationToken)
    {
        var removed = await _service.RemoverAsync(id, cancellationToken);
        if (!removed)
        {
            return NotFoundResponse();
        }

        return NoContent();
    }

    protected IActionResult Success(object? data = null)
    {
        return Ok(data);
    }

    protected IActionResult CreatedSuccess(object? data = null)
    {
        return StatusCode(StatusCodes.Status201Created, data);
    }

    protected ActionResult NotFoundResponse(string message = "Registro nao encontrado.")
    {
        return NotFound(new { message });
    }
}
