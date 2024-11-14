using SmtpTelegramRelay.Models;
using Telegram.Bot.Types.Enums;

namespace SmtpTelegramRelay.Services.TelegramStores.Models
{
    public class TelegramMessage
    {
        public TelegramMessage()
        {
        }

        public TelegramMessage(WebMessage msg) : this()
        {
            From = Enumerable.Repeat(msg.From, 1);
            To = Enumerable.Repeat(msg.To, 1);
            Subject = msg.Subject;
            Body = msg.Body;
            ParseMode = msg.ParseMode;
            ChatId = msg.ChatId;
        }

        public TelegramMessage(WebMessage msg, IEnumerable<(string Name, Stream Stream)> files) : this(msg)
        {
            Files = files;
        }

        public IEnumerable<string?> From { get; init; } = Enumerable.Empty<string?>();

        public IEnumerable<string?> To { get; init; } = Enumerable.Empty<string?>();

        public string? Subject { get; init; } = string.Empty;

        public string? Body { get; init; } = string.Empty;

        public IEnumerable<(string Name, Stream Stream)> Files { get; init; } = Enumerable.Empty<(string Name, Stream Stream)>();

        public ParseMode ParseMode { get; init; } = ParseMode.None;

        public long? ChatId { get; init; }
    }
}
