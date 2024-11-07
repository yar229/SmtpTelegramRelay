using Microsoft.AspNetCore.Mvc;
using SmtpTelegramRelay.Services;

namespace SmtpTelegramRelay.Controllers;

public class TelegramController : Controller
{
    private readonly TelegramStore _store;

    public TelegramController(TelegramStore store)
    {
        _store = store;
    }

    [HttpGet("send")]
    public Task Send([FromQuery] string? subject, [FromQuery] string? message, [FromQuery] string? from, [FromQuery] string? to)
        => _store.SaveAsync(subject, message, from, to, CancellationToken.None);
}

