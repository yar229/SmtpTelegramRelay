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
using SmtpTelegramRelay.Configuration;

namespace SmtpTelegramRelay.Services;

public sealed class Store : MessageStore
{
    private readonly IOptionsMonitor<RelayConfiguration> _options;
    private readonly Dictionary<KeyValuePair<string, string>, RelayConfiguration.Route> _routes;
    private TelegramBotClient? _bot;
    private string? _token;

    private const string Asterisk = "*";

    public Store(IOptionsMonitor<RelayConfiguration> options)
    {
        _options = options;
        _routes = options.CurrentValue.Routing
            .ToDictionary(r => new KeyValuePair<string, string>(r.EmailFrom, r.EmailTo), r => r);
    }

    public async Task<SmtpResponse> SaveAsync(string? subject, string? message, string? from, string? to, CancellationToken cancellationToken)
    {
        PrepareBot(_options.CurrentValue, cancellationToken);
        foreach (var chat in GetChats(from, to))
        {
            var sb = new StringBuilder();
            foreach (var prefix in chat.Prefixes)
                if (!string.IsNullOrWhiteSpace(prefix.RegexpSubject) && !string.IsNullOrEmpty(subject) && Regex.IsMatch(subject, prefix.RegexpSubject) ||
                    !string.IsNullOrWhiteSpace(prefix.RegexpBody) && !string.IsNullOrEmpty(message) && Regex.IsMatch(message, prefix.RegexpBody))
                    sb.Append(prefix.Prefix);

            if (!string.IsNullOrEmpty(subject))
                sb.Append($"{subject}\r\n");
            if (!string.IsNullOrEmpty(from))
                sb.Append($"From: {from}\r\n");
            if (!string.IsNullOrEmpty(to))
                sb.Append($"To: {to}\r\n");
            if (!string.IsNullOrEmpty(message))
                sb.Append(message);

            if (sb.Length > 0)
                await _bot!.SendMessage(chat.TelegramChatId, sb.ToString(), linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return SmtpResponse.Ok;
    }

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
                if (!string.IsNullOrWhiteSpace(prefix.RegexpSubject) && Regex.IsMatch(message.Subject, prefix.RegexpSubject) ||
                     !string.IsNullOrWhiteSpace(prefix.RegexpBody) && Regex.IsMatch(message.Subject, prefix.RegexpBody))
                    sb.Append(prefix.Prefix);

            sb.Append($"{message.Subject}\nFrom: {message.From}\nTo: {message.To}\n{message.TextBody}");

            await _bot!.SendMessage(chat.TelegramChatId, sb.ToString(), linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: cancellationToken).ConfigureAwait(false);
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
        List<string> Selector(InternetAddress addr) =>
            addr switch
            {
                MailboxAddress mba => Enumerable.Repeat(mba.Address, 1).ToList(),
                GroupAddress group => group.Members.Cast<MailboxAddress>().Select(mba => mba.Address).ToList(),
                _ => throw new NotImplementedException()
            };

        var xemailsFrom = emailsFrom.SelectMany(Selector);
        var xemailsTo = emailsTo.SelectMany(Selector).ToList();

        return GetChats(xemailsFrom, xemailsTo);
    }

    private List<RelayConfiguration.Route> GetChats(IEnumerable<string> emailsFrom, IEnumerable<string> emailsTo)
    {
        var result = new List<RelayConfiguration.Route>();

        foreach (var emailFrom2 in emailsFrom)
        {
            foreach (var emailTo2 in emailsTo)
            {
                if (_routes.TryGetValue(new KeyValuePair<string, string>(emailFrom2, emailTo2), out var route))
                    result.Add(route);
                else if (_routes.TryGetValue(new KeyValuePair<string, string>(Asterisk, emailTo2), out var route1))
                    result.Add(route1);
                else if (_routes.TryGetValue(new KeyValuePair<string, string>(emailFrom2, Asterisk), out var route2))
                    result.Add(route2);
                else if (_routes.TryGetValue(new KeyValuePair<string, string>(Asterisk, Asterisk), out var route3))
                    result.Add(route3);
            }
        }
        return result;
    }

    private List<RelayConfiguration.Route> GetChats(string? emailFrom, string? emailTo)
        => GetChats(Enumerable.Repeat(emailFrom ?? Asterisk, 1), Enumerable.Repeat(emailTo ?? Asterisk, 1));
}
