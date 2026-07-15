using Microsoft.AspNetCore.Mvc;

namespace ApiEasyStay.Properties.Controllers;

public abstract class BaseController : ControllerBase
{
    [HttpGet]
    protected async Task<IActionResult> Get()
    {
        await  Task.CompletedTask;
        return Ok();
        
    }

    [HttpPost]
    protected async Task<IActionResult> Post([FromBody] object value)
    {
        await Task.CompletedTask;
        return Ok();
    }

    [HttpPut]
    protected async Task<IActionResult> Put([FromBody] object value)
    {
        await Task.CompletedTask;
        return Ok();
        
    }

    [HttpDelete]
    protected async Task<IActionResult> Delete([FromBody] object value)
    {
        await Task.CompletedTask;
        return Ok();
    }
    
}