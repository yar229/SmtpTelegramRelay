using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types.Enums;

namespace SmtpTelegramRelay.Models;

public class WebMessage
{
    [FromQuery(Name = "from")]
    public string? From { get; set; }

    [FromQuery(Name = "to")]
    public string? To { get; set; }

    [FromQuery(Name = "subject")]
    public string? Subject { get; set; }

    [FromQuery(Name = "message")]
    public string? Body { get; set; }

    [FromQuery(Name = "parseMode")]
    public ParseMode ParseMode { get; set; }

    [FromQuery(Name = "chatId")]
    public long? ChatId { get; set; }

}
