using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmtpTelegramRelay.Models;

namespace SmtpTelegramRelay.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    public HealthController()
    {
    }

    [HttpGet()]
    public async Task<IActionResult> Alive([FromQuery] WebMessage webMessage, [FromForm] IFormCollection formCollection)
    {
        return Ok();
    }
}
