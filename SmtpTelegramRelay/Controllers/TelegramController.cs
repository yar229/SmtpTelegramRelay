using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmtpTelegramRelay.Models;
using SmtpTelegramRelay.Services.TelegramStores;
using SmtpTelegramRelay.Services.TelegramStores.Models;

namespace SmtpTelegramRelay.Controllers;

[ApiController]
public class TelegramController : ControllerBase
{
    private readonly TelegramStore _store;

    public TelegramController(TelegramStore store)
    {
        _store = store;
    }

    [HttpGet("message")]
    public async Task<IActionResult> Send([FromQuery] WebMessage webMessage)
    {
        await _store.SaveAsync(new TelegramMessage(webMessage), default);
        return Ok();
    }

    [HttpPost("message")]
    public async Task<IActionResult> Photos([FromQuery] WebMessage webMessage, [FromForm] IFormCollection formCollection)
    {
        var files = formCollection.Files.Select(f => (f.FileName, f.OpenReadStream()));
        await _store.SaveAsync(new TelegramMessage(webMessage, files), default);
        return Ok();
    }
}
