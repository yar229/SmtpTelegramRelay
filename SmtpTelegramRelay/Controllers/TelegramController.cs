using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmtpTelegramRelay.Services;
using Telegram.Bot.Types.Enums;

namespace SmtpTelegramRelay.Controllers;

public class TelegramController : Controller
{
    private readonly TelegramStore _store;

    public TelegramController(TelegramStore store)
    {
        _store = store;
    }

    [HttpGet("message")]
    public Task Send([FromQuery] string? subject, [FromQuery] string? message, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] ParseMode parseMode)
        => _store.SaveAsync(subject, message, from, to, parseMode, CancellationToken.None);

    [HttpPost("message")]
    public Task Photos([FromQuery] string? subject, [FromQuery] string? message, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] ParseMode parseMode,
        [FromForm] IFormCollection files)
    {
        var streams = files.Files.Select(f => new KeyValuePair<string, Stream>(f.FileName, f.OpenReadStream()));

        return _store.SaveAsync(subject, message, streams, Enumerable.Repeat(from, 1), Enumerable.Repeat(to, 1), parseMode, CancellationToken.None);
    }
}
