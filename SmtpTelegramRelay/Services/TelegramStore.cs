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
using SmtpTelegramRelay.Extensions;
using Telegram.Bot.Types.Enums;

namespace SmtpTelegramRelay.Services;

public sealed class TelegramStore : MessageStore
{
    private readonly IOptionsMonitor<RelayConfiguration> _options;
    private readonly ILogger<TelegramStore> _logger;
    private readonly Dictionary<(string, string), RouteItem> _routes;
    private readonly Dictionary<string, Regex> _regexes;
    private TelegramBotClient? _bot;
    private string? _token;

    private const string Asterisk = "*";

    public TelegramStore(IOptionsMonitor<RelayConfiguration> options, ILogger<TelegramStore> logger)
    {
        _options = options;
        _logger = logger;
        _routes = options.CurrentValue.Routing
            .ToDictionary(r => (r.EmailFrom, r.EmailTo), r => r);
        _regexes = CompileRegexes(options);
    }

    public async Task<SmtpResponse> SaveAsync(IEnumerable<string?> from, IEnumerable<string?> to,
        string? subject, string? body, 
        IEnumerable<(string Name, Stream Stream)> files,
        ParseMode parseMode, CancellationToken cancellationToken)
    {
        PrepareBot(_options.CurrentValue, cancellationToken);

        var froms = from.Cast<string>().ToList();
        var tos = to.Cast<string>().ToList();
        var medias = files
            .Select(f => new InputMediaPhoto(new InputFileStream(f.Stream, f.Name)))
            .ToList();

        var strFrom = string.Join(",", froms).Trim();
        var strTo = string.Join(",", tos).Trim();
        var text = new StringBuilder();
        if (!string.IsNullOrEmpty(subject))
            text.Append($"{subject}\r\n");
        if (!string.IsNullOrEmpty(strFrom))
            text.Append($"From: {strFrom}\r\n");
        if (!string.IsNullOrEmpty(strTo))
            text.Append($"To: {strTo}\r\n");
        if (!string.IsNullOrEmpty(body))
            text.Append(body);

        Regex? GetRegex(string? rx) => rx.IsNotNullOrEmpty(r => _regexes.TryGetValue(r, out var regex) ? regex : null);
        bool IsMatch(string? str, string rgx) => str.IsNotNullOrEmpty(_ => GetRegex(rgx)) is { } irgx && irgx.IsMatch(str!);
        foreach (var chat in GetChats(froms, tos))
        {
            var sb = new StringBuilder();
            foreach (var prefix in chat.Prefixes)
                if (IsMatch(subject, prefix.RegexpSubject) || IsMatch(body, prefix.RegexpBody))
                    sb.Append(prefix.Prefix);
            sb.Append(text);

            for (int i = 0; i <= sb.Length / 4096; i++)
            {
                var part = sb.ToString(i * 4096, Math.Min(sb.Length - i * 4096, 4096));
                if (part.Length > 0)
                    await _bot!.SendMessage(chat.TelegramChatId, part, parseMode: parseMode, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
            }

            if (medias.Count > 0)
            {
                await _bot!.SendChatAction(chat.TelegramChatId, ChatAction.UploadDocument,
                    cancellationToken: cancellationToken);
                await _bot!.SendMediaGroup(chat.TelegramChatId, medias, disableNotification: true,
                        cancellationToken: cancellationToken) //TODO: upload files once, then send by ids
                    .ConfigureAwait(false);
            }
        }

        return SmtpResponse.Ok;
    }

    public Task<SmtpResponse> SaveAsync(string? from, string? to, string? subject, string? body, 
        ParseMode parseMode, CancellationToken cancellationToken)
        => SaveAsync((from ?? string.Empty).Enumerate(), (to ?? string.Empty).Enumerate(), subject, body, Enumerable.Empty<(string, Stream)>(), 
            parseMode, cancellationToken);


    public override async Task<SmtpResponse> SaveAsync(
        ISessionContext context,
        IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(buffer.ToArray(), writable: false);
        var message = await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
        var files = message.Attachments.Select(attachment =>
        {
            var fstream = new MemoryStream();
            string fileName;
            if (attachment is MessagePart rfc822)
            {
                fileName = rfc822.ContentDisposition.FileName;
                if (string.IsNullOrEmpty(fileName))
                    fileName = "attached-message.eml";
                rfc822.Message.WriteTo(fstream, cancellationToken);
            }
            else
            {
                var part = (MimePart)attachment;
                fileName = part.FileName;
                part.Content.DecodeTo(fstream, cancellationToken);
            }

            fstream.Position = 0;
            return (fileName, (Stream)fstream);
        }).ToList();

        List<string> Selector(InternetAddress addr) =>
            addr switch
            {
                MailboxAddress mba => Enumerable.Repeat(mba.Address, 1).ToList(),
                GroupAddress group => group.Members.Cast<MailboxAddress>().Select(mba => mba.Address).ToList(),
                _ => throw new NotImplementedException()
            };

        var xemailsFrom = message.From.SelectMany(Selector);
        var xemailsTo = message.To.SelectMany(Selector);

        string text = string.Empty;
        var parseMode = ParseMode.None;
        if (!string.IsNullOrEmpty(message.TextBody))
            text = message.TextBody;
        else if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            text = message.HtmlBody.ConvertToTelegramHtml();
            parseMode = ParseMode.Html;
        }

        return await SaveAsync(xemailsFrom, xemailsTo, message.Subject, text, files, parseMode, cancellationToken)
            .ConfigureAwait(false);
    }

    private void PrepareBot(RelayConfiguration currentOptions, CancellationToken cancellationToken)
    {
        if (_token != currentOptions.TelegramBotToken)
        {
            _bot?.Close(cancellationToken);
            _bot = null;
        }

        if (_bot != null) 
            return;

        _bot = new TelegramBotClient(currentOptions.TelegramBotToken);
        _token = currentOptions.TelegramBotToken;
    }

    private List<RouteItem> GetChats(List<string?> emailsFrom, List<string?> emailsTo)
    {
        var result = new List<RouteItem>();

        foreach (var emailFrom2 in emailsFrom)
        {
            foreach (var emailTo2 in emailsTo)
            {
                if (_routes.TryGetValue((emailFrom2, emailTo2), out var route))
                    result.Add(route);
                else if (_routes.TryGetValue((Asterisk, emailTo2), out var route1))
                    result.Add(route1);
                else if (_routes.TryGetValue((emailFrom2, Asterisk), out var route2))
                    result.Add(route2);
                else if (_routes.TryGetValue((Asterisk, Asterisk), out var route3))
                    result.Add(route3);
            }
        }

        return result
            .GroupBy(ch => ch.TelegramChatId)
            .Select(group => group.First())
            .ToList();
    }

    private Dictionary<string, Regex> CompileRegexes(IOptionsMonitor<RelayConfiguration> options)
    {
        Regex? Compile(string rg) => rg
            .TryCatch(r => r.IsNotNullOrEmpty(str => new Regex(str, RegexOptions.Compiled)),
                r => _logger.LogError($"Cannot compile regex '{r}'"));

        var result = new Dictionary<string, Regex>();
        foreach (var prefix in options.CurrentValue.Routing.SelectMany(r => r.Prefixes))
        {
            if (Compile(prefix.RegexpSubject) is { } subj)
                result.TryAdd(prefix.RegexpSubject, subj);
            if (Compile(prefix.RegexpBody) is { } body)
                result.TryAdd(prefix.RegexpBody, body);
        }
        return result;
    }
}
