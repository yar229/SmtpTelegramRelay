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
using HtmlAgilityPack;
using MimeKit.Text;
using Telegram.Bot.Types.Enums;

namespace SmtpTelegramRelay.Services;

public sealed class TelegramStore : MessageStore
{
    private readonly IOptionsMonitor<RelayConfiguration> _options;
    private readonly Dictionary<KeyValuePair<string, string>, RouteItem> _routes;
    private TelegramBotClient? _bot;
    private string? _token;

    private const string Asterisk = "*";

    public TelegramStore(IOptionsMonitor<RelayConfiguration> options)
    {
        _options = options;
        _routes = options.CurrentValue.Routing
            .ToDictionary(r => new KeyValuePair<string, string>(r.EmailFrom, r.EmailTo), r => r);
    }

    public async Task<SmtpResponse> SaveAsync(string? subject, string? message, IEnumerable<KeyValuePair<string, Stream>> files, IEnumerable<string> from, IEnumerable<string> to,
        ParseMode parseMode,
        CancellationToken cancellationToken)
    {
        PrepareBot(_options.CurrentValue, cancellationToken);

        var medias = files
            .Select(f => new InputMediaDocument(new InputFileStream(f.Value, f.Key)))
            .ToList();

        var froms = string.Join(",", from).Trim();
        var tos = string.Join(",", to).Trim();
        var text = new StringBuilder();
        if (!string.IsNullOrEmpty(subject))
            text.Append($"{subject}\r\n");
        if (!string.IsNullOrEmpty(froms))
            text.Append($"From: {froms}\r\n");
        if (!string.IsNullOrEmpty(tos))
            text.Append($"To: {tos}\r\n");
        if (!string.IsNullOrEmpty(message))
            text.Append(message);

        foreach (var chat in GetChats(from, to))
        {
            var sb = new StringBuilder();
            foreach (var prefix in chat.Prefixes)
                if (!string.IsNullOrWhiteSpace(prefix.RegexpSubject) && !string.IsNullOrEmpty(subject) && Regex.IsMatch(subject, prefix.RegexpSubject) ||
                     !string.IsNullOrWhiteSpace(prefix.RegexpBody) && !string.IsNullOrEmpty(message) && Regex.IsMatch(message, prefix.RegexpBody))
                    sb.Append(prefix.Prefix);
            sb.Append(text);

            for (int i = 0 ; i <= sb.Length / 4096; i++)
                await _bot!.SendMessage(chat.TelegramChatId, sb.ToString(i * 4096, Math.Min(sb.Length - i * 4096, 4096)), parseMode: parseMode, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            if (medias.Count > 0)
                await _bot!.SendMediaGroup(chat.TelegramChatId, medias, disableNotification:true, cancellationToken: cancellationToken)    //TODO: upload files once, then send by ids
                    .ConfigureAwait(false);
        }

        return SmtpResponse.Ok;
    }

    public Task<SmtpResponse> SaveAsync(string? subject, string? message, string? from, string? to, CancellationToken cancellationToken)
        => SaveAsync(subject, message, Enumerable.Empty<KeyValuePair<string, Stream>>(), (from ?? string.Empty).Enumerate(), (to ?? string.Empty).Enumerate(),
            ParseMode.None,
            cancellationToken);


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
            return new KeyValuePair<string, Stream>(fileName, fstream);
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

        return await SaveAsync(message.Subject, text, files, xemailsFrom, xemailsTo, parseMode,
            cancellationToken).ConfigureAwait(false);
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

    private List<RouteItem> GetChats(IEnumerable<string> emailsFrom, IEnumerable<string> emailsTo)
    {
        var result = new List<RouteItem>();

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

        return result
            .GroupBy(ch => ch.TelegramChatId)
            .Select(group => group.First())
            .ToList();
    }
}
