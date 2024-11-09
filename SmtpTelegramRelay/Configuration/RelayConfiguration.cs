using System.Collections.ObjectModel;

namespace SmtpTelegramRelay.Configuration;

public sealed class RelayConfiguration
{
    public string HttpIp { get; set; } = default!;
    public ushort HttpPort { get; set; } = 80;

    public string SmtpIp { get; set; } = default!;
    public ushort SmtpPort { get; set; } = 25;

    public string TelegramBotToken { get; set; } = default!;
    public Collection<RouteItem> Routing { get; } = new();
}

public sealed class RouteItem
{
    public string EmailTo { get; set; } = default!;
    public string EmailFrom { get; set; } = default!;

    public long TelegramChatId { get; set; }

    public Collection<PrefixItem> Prefixes { get; } = new();
}

public sealed class PrefixItem
{
    public string RegexpSubject { get; set; } = default!;
    public string RegexpBody { get; set; } = default!;
    public string Prefix { get; set; } = default!;
}

