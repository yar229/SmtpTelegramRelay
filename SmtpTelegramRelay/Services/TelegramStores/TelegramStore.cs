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
using SmtpTelegramRelay.Services.TelegramStores.Models;
using Telegram.Bot.Types.Enums;
using System.Diagnostics;
using YamlDotNet.Serialization.NodeDeserializers;
using SmtpTelegramRelay.Common;
using Microsoft.AspNetCore.Http;

namespace SmtpTelegramRelay.Services.TelegramStores;

public sealed class TelegramStore : MessageStore
{
    private readonly IOptionsMonitor<RelayConfiguration> _options;
    private readonly ILogger<TelegramStore> _logger;
    private readonly Dictionary<(string, string), IEnumerable<RouteItem>> _routes;
    private readonly Dictionary<string, Regex> _regexes;
    private TelegramBotClient? _bot;

    private const string Asterisk = "*";

    public TelegramStore(IOptionsMonitor<RelayConfiguration> options, ILogger<TelegramStore> logger)
    {
        _options = options;
        _logger = logger;
        _routes = options.CurrentValue.Routing.GroupBy(r => (r.EmailFrom, r.EmailTo))
            .ToDictionary(r => r.Key, r => r.AsEnumerable());
        _regexes = CompileRegexes(options);
        PrepareBot(default);
    }

    public async Task<SmtpResponse> SaveAsync(TelegramMessage message, CancellationToken cancellationToken)
    {
        var text = new StringBuilder()
            .AppendNotEmpty(message.Subject, str => $"{str}\r\n")
            .AppendNotEmpty(string.Join(",", message.From).Trim(), str => $"From: {str}\r\n")
            .AppendNotEmpty(string.Join(",", message.To).Trim(), str => $"To: {str}\r\n")
            .Append(message.Body);

        Regex? GetRegex(string? rx) => rx.IsNotNullOrEmpty(r => _regexes.TryGetValue(r, out var regex) ? regex : null);
        bool IsMatch(string? str, string rgx) => str.IsNotNullOrEmpty(_ => GetRegex(rgx)) is { } irgx && irgx.IsMatch(str!);
        foreach (var chat in GetChats(message.From, message.To, message.ChatId))
        {
            var sb = new StringBuilder();
            foreach (var prefix in chat.Prefixes)
                if (IsMatch(message.Subject, prefix.RegexpSubject) || IsMatch(message.Body, prefix.RegexpBody))
                    sb.Append(prefix.Prefix);
            sb.Append(text);

            foreach (var part in sb.ToString().Chunk(4096))
                await _bot!.SendMessage(chat.TelegramChatId,  new string(part), parseMode: message.ParseMode,  linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            var medias = message.Files
                .Select(f => new { MediaType = InputMediaHelper.GetMediaType(Path.GetExtension(f.Name)), File = f })
                .GroupBy(mt => mt.MediaType, mt =>
                {
                    mt.File.Stream.Position = 0;
                    var ms = new MemoryStream();
                    {
                        mt.File.Stream.CopyTo(ms);
                        ms.Position = 0;
                    }
                    return InputMediaHelper.GetInputMedia(mt.MediaType, new InputFileStream(ms, mt.File.Name));
                }).ToList();
            if (medias.Count <= 0) 
                continue;

            await _bot!.SendChatAction(chat.TelegramChatId, ChatAction.UploadDocument,
                cancellationToken: cancellationToken);

            foreach (var mediaList in medias)
                await _bot!.SendMediaGroup(chat.TelegramChatId, mediaList, disableNotification: true,
                        cancellationToken: cancellationToken) //TODO: upload files once, then send by ids
                    .ConfigureAwait(false);
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

        return await SaveAsync(new TelegramMessage
            {
                From = xemailsFrom, 
                To = xemailsTo, 
                Subject = message.Subject, 
                Body = text, 
                Files = files, 
                ParseMode = parseMode
            }, cancellationToken)
            .ConfigureAwait(false);
    }

    private void PrepareBot(CancellationToken cancellationToken)
    {
        _bot = new TelegramBotClient(_options.CurrentValue.TelegramBotToken);
        _bot.OnMessage += async (message, _) =>
        {
            if (message.Text is null || !message.Text.StartsWith('/'))
                return;
            if (message.Text == "/chatid")
                await _bot.SendMessage(message.Chat, $"{message.Chat.Id}", cancellationToken: cancellationToken);

            foreach (var action in _routes.Values
                         .SelectMany(r => r)
                         .Where(r => r.TelegramChatId == message.Chat.Id)
                         .SelectMany(r => r.Actions.Where(a => $"/{a.Name}" == message.Text.Trim())))
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = @"powershell.exe";
                startInfo.Arguments = $" {action.Command} {message.Chat.Id}";
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                Process process = new Process();
                process.StartInfo = startInfo;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
            }
        };
    }

    private List<RouteItem> GetChats(IEnumerable<string?> emailsFrom, IEnumerable<string?> emailsTo, long? chatId)
    {
        var result = new List<RouteItem>();

        foreach (var emailFrom2 in emailsFrom)
        {
            foreach (var emailTo2 in emailsTo)
            {
                if (_routes.TryGetValue((emailFrom2, emailTo2), out var routes))
                    result.AddRange(routes);
                else if (_routes.TryGetValue((Asterisk, emailTo2), out var routes1))
                    result.AddRange(routes1);
                else if (_routes.TryGetValue((emailFrom2, Asterisk), out var routes2))
                    result.AddRange(routes2);
                else if (_routes.TryGetValue((Asterisk, Asterisk), out var routes3))
                    result.AddRange(routes3);
            }
        }

        return result
            .Where(r => null == chatId || r.TelegramChatId == chatId)
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
