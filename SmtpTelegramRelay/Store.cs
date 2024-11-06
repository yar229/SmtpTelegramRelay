using MimeKit;
using SmtpServer;
using SmtpServer.Storage;
using SmtpServer.Protocol;
using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace SmtpTelegramRelay;

internal sealed class Store : MessageStore
{
    private readonly IOptionsMonitor<RelayConfiguration> _options;
    private readonly Dictionary<KeyValuePair<string, string>, RelayConfiguration.Route> _routes;

    public Store(IOptionsMonitor<RelayConfiguration> options)
    {
        _options = options;
        _routes = options.CurrentValue.Routing
            .ToDictionary(r => new KeyValuePair<string, string>(r.EmailFrom, r.EmailTo), r => r);
    }

    private const string Asterisk = "*";
    private TelegramBotClient? _bot;
    private string? _token;

    public override async Task<SmtpResponse> SaveAsync(
        ISessionContext context, 
        IMessageTransaction transaction, 
        ReadOnlySequence<byte> buffer, 
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(buffer.ToArray(), writable: false);
        var message = await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);

        var currentOptions = _options.CurrentValue;
        PrepareBot(currentOptions, cancellationToken);
        foreach (var chat in GetChats(message.From, message.To))
        {
            var sb = new StringBuilder();
            foreach (var prefix in chat.Prefixes)
                if ( (!string.IsNullOrWhiteSpace(prefix.RegexpSubject) && Regex.IsMatch(message.Subject, prefix.RegexpSubject)) ||
                     (!string.IsNullOrWhiteSpace(prefix.RegexpBody) && Regex.IsMatch(message.Subject, prefix.RegexpBody)))
                        sb.Append(prefix.Prefix);

            sb.Append($"{message.Subject}\nFrom: {message.From}\nTo: {message.To}\n{message.TextBody}");

            await _bot!.SendMessage(chat.TelegramChatId, sb.ToString(), linkPreviewOptions:new LinkPreviewOptions{IsDisabled = true}, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        
        return SmtpResponse.Ok;
    }

    private void PrepareBot(RelayConfiguration currentOptions, CancellationToken cancellationToken)
    {
        if (_bot != null && _token != currentOptions.TelegramBotToken)
        {
            _ = _bot.Close(cancellationToken);
            _bot = null;
        }

        if (_bot == null)
        {
            _bot = new TelegramBotClient(currentOptions.TelegramBotToken);
            _token = currentOptions.TelegramBotToken;
        }
    }

    private List<RelayConfiguration.Route> GetChats(InternetAddressList emailsFrom, InternetAddressList emailsTo)
    {
        List<MailboxAddress> Selector(InternetAddress addr) =>
            addr switch
            {
                MailboxAddress mba => Enumerable.Repeat(mba, 1).ToList(),
                GroupAddress group => group.Members.Cast<MailboxAddress>().ToList(),
                _ => throw new NotImplementedException()
            };

        var xemailsFrom = emailsFrom.SelectMany(Selector);
        var xemailsTo = emailsTo.SelectMany(Selector).ToList();
        var result = new List<RelayConfiguration.Route>();

        foreach (var emailFrom2 in xemailsFrom)
        {
            foreach (var emailTo2 in xemailsTo)
            {
                if (_routes.TryGetValue(new KeyValuePair<string, string>(emailFrom2.Address, emailTo2.Address), out var route))
                    result.Add(route);
                else if (_routes.TryGetValue(new KeyValuePair<string, string>(Asterisk, emailTo2.Address), out var route1))
                    result.Add(route1);
                else if (_routes.TryGetValue(new KeyValuePair<string, string>(emailFrom2.Address, Asterisk), out var route2))
                    result.Add(route2);
                else if (_routes.TryGetValue(new KeyValuePair<string, string>(Asterisk, Asterisk), out var route3))
                    result.Add(route3);
            }
        }
        return result;
    }
}
