namespace SmtpTelegramRelay;

internal sealed class RelayConfiguration
{
    public ushort SmtpPort { get; set; } = 25;
    public string TelegramBotToken { get; set; } = default!;
    public List<Route> Routing { get; set; } = new();

    public sealed class Route
    {
        public string EmailTo { get; set; } = default!;
        public string EmailFrom { get; set; } = default!;

        public int TelegramChatId { get; set; }

        public List<PrefixItem> Prefixes { get; set; } = new();
    }

    public sealed class PrefixItem
    {
        public string RegexpSubject { get; set; } = default!;
        public string RegexpBody { get; set; } = default!;
        public string Prefix { get; set; } = default!;
    }
}
