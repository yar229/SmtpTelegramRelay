using Microsoft.Extensions.Options;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using SmtpTelegramRelay.Common;
using SmtpTelegramRelay.Configuration;
using SmtpTelegramRelay.Extensions;
using SmtpTelegramRelay.Services.TelegramStores.Models;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace SmtpTelegramRelay.Services.TelegramStores;

public sealed class TelegramStore : MessageStore
{
    private readonly IOptions<RelayConfiguration> _options;
    private readonly ILogger<TelegramStore> _logger;
    private readonly Dictionary<(string, string), IEnumerable<RouteItem>> _routes;
    private readonly Dictionary<string, Regex> _regexes;
    private TelegramBotClient? _bot;

    private const string Asterisk = "*";

    public TelegramStore(IOptions<RelayConfiguration> options, ILogger<TelegramStore> logger)
    {
        _options = options;
        _logger = logger;
        _routes = options.Value.Routing.GroupBy(r => (r.EmailFrom, r.EmailTo))
            .ToDictionary(r => r.Key, r => r.AsEnumerable());
        _regexes = CompileRegexes(options);
        PrepareBot();
    }

    public async Task<SmtpResponse> SaveAsync(TelegramMessage message, CancellationToken cancellationToken)
    {
        var text = new StringBuilder()
            .AppendNotEmpty(message.Subject, str => $"{str}\r\n")
            .AppendIf(!message.DoHideFrom, string.Join(",", message.From).Trim(), str => $"From: {str}\r\n")
            .AppendNotEmpty(string.Join(",", message.To).Trim(), str => $"To: {str}\r\n")
            .Append(message.Body);

        Regex? GetRegex(string? rx) => rx.IsNotNullOrEmpty(r => _regexes.TryGetValue(r, out var regex) ? regex : null);
        bool IsMatch(string? str, string rgx) => str.IsNotNullOrEmpty(_ => GetRegex(rgx)) is { } irgx && irgx.IsMatch(str!);
        foreach (var chat in GetChats(message.From, message.To, message.ChatId))
        {
            var sb = new StringBuilder();
            foreach (var prefix in chat.Prefixes)
                if (!message.DoHideFrom && (IsMatch(message.Subject, prefix.RegexpSubject) || IsMatch(message.Body, prefix.RegexpBody)))
                    sb.Append(prefix.Prefix);
            sb.Append(text);

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

            //dirty
            if (medias.Count == 1 && medias[0].Count() == 1 && sb.Length < 1024)
            {
                var doc = (medias[0].First() as InputMedia)?.Media;
                var sentMessage = medias[0].Key switch
                {
                    InputMediaType.Document => await SendWithRetryAsync(() =>
                    {
                        ResetMediaPositions(medias);
                        return _bot!.SendDocument(chat.TelegramChatId, doc, sb.ToString(), parseMode: message.ParseMode,
                            cancellationToken: cancellationToken);
                    }, cancellationToken),
                    InputMediaType.Photo => await SendWithRetryAsync(() =>
                    {
                        ResetMediaPositions(medias);
                        return _bot!.SendPhoto(chat.TelegramChatId, doc, sb.ToString(), showCaptionAboveMedia: true, parseMode: message.ParseMode,
                            cancellationToken: cancellationToken);
                    }, cancellationToken),
                    _ => null
                };
                if (sentMessage != null)
                    continue;
            }

            foreach (var part in sb.ToString().Chunk(4096))
                await SendWithRetryAsync(() =>
                        _bot!.SendMessage(chat.TelegramChatId, new string(part), parseMode: message.ParseMode, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                            cancellationToken: cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            if (medias.Count <= 0)
                continue;

            await SendWithRetryAsync(() =>
                    _bot!.SendChatAction(chat.TelegramChatId, ChatAction.UploadDocument,
                        cancellationToken: cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

            foreach (var mediaList in medias)
                foreach (var chunk in mediaList.Chunk(MaxAlbumSize))
                {
                    if (chunk.Length == 1)
                    {
                        var single = (InputMedia)chunk[0];
                        await SendWithRetryAsync(async () =>
                            {
                                ResetMediaPositions(medias);
                                if (single.Type == InputMediaType.Photo)
                                    await _bot!.SendPhoto(chat.TelegramChatId, single.Media).ConfigureAwait(false);
                                else
                                    await _bot!.SendDocument(chat.TelegramChatId, single.Media).ConfigureAwait(false);
                            }, cancellationToken)
                        .ConfigureAwait(false);
                        continue;
                    }

                    await SendWithRetryAsync(() =>
                        {
                            ResetMediaPositions(medias);
                            return _bot!.SendMediaGroup(chat.TelegramChatId, chunk, disableNotification: true,
                                cancellationToken: cancellationToken); //TODO: upload files once, then send by ids
                        }, cancellationToken)
                    .ConfigureAwait(false);
                }
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
        if (!string.IsNullOrEmpty(message. TextBody))
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

    public async Task TestTelegramAsync(CancellationToken cancellationToken)
    {
        if (_bot is null)
            throw new InvalidOperationException("Telegram bot is not initialized");

        await _bot.GetMe(cancellationToken).ConfigureAwait(false);
    }

    private const int MaxTelegramRetries = 3;
    private const int MaxAlbumSize = 10;

    private static bool IsRetryable(Exception ex) => ex switch
    {
        ApiRequestException api when api.ErrorCode is >= 400 and < 500 and not 408 and not 429 => false,
        _ => true
    };

    private async Task<T> SendWithRetryAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxTelegramRetries && IsRetryable(ex) && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1)));
                _logger.LogWarning(ex, "Telegram API call failed (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}",
                    attempt, MaxTelegramRetries, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task SendWithRetryAsync(Func<Task> action, CancellationToken cancellationToken)
        => SendWithRetryAsync(
            async () => { await action().ConfigureAwait(false); return true; },
            cancellationToken);

    private static void ResetMediaPositions(List<IGrouping<InputMediaType, IAlbumInputMedia>> medias)
    {
        foreach (var media in medias.SelectMany(group => group).OfType<InputMedia>())
            if (media.Media is InputFileStream stream && stream.Content.CanSeek)
                stream.Content.Position = 0;
    }

    private void PrepareBot()
    {
        if (_options.Value.UseProxy)
        {
            WebProxy proxy = new(_options.Value.Proxy);
            HttpClient httpClient = new(new SocketsHttpHandler { Proxy = proxy, UseProxy = true });
            _bot = new TelegramBotClient(_options.Value.TelegramBotToken, httpClient);
        }
        else
            _bot = new TelegramBotClient(_options.Value.TelegramBotToken);

        _bot.OnMessage += async (message, _) =>
        {
            try
            {
                if (message.Text is null || !message.Text.StartsWith('/'))
                    return;
                if (message.Text == "/chatid")
                    await _bot.SendMessage(message.Chat, $"{message.Chat.Id}").ConfigureAwait(false);

                foreach (var action in _routes.Values
                             .SelectMany(r => r)
                             .Where(r => r.TelegramChatId == message.Chat.Id)
                             .SelectMany(r => r.Actions.Where(a => $"/{a.Name}" == message.Text.Trim())))
                {
                    await RunActionAsync(action, message.Chat.Id).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle bot command '{Command}'", message.Text);
            }
        };
    }

    private async Task RunActionAsync(ActionItem action, long chatId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = @"powershell.exe",
            Arguments = $" {action.Command} {chatId}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

        var output = await outputTask.ConfigureAwait(false);
        var errors = await errorTask.ConfigureAwait(false);

        _logger.LogDebug("Action '{Command}' completed. Output: '{Output}'", action.Command, output);
        if (!string.IsNullOrEmpty(errors))
            _logger.LogWarning("Action '{Command}' wrote to stderr: '{Errors}'", action.Command, errors);
    }

    private List<RouteItem> GetChats(IEnumerable<string?> emailsFrom, IEnumerable<string?> emailsTo, long? chatId)
    {
        var result = new List<RouteItem>();

        foreach (var emailFrom2 in emailsFrom)
        {
            foreach (var emailTo2 in emailsTo)
            {
                if (_routes.TryGetValue((emailFrom2, emailTo2 ?? Asterisk), out var routes))
                    result.AddRange(routes);
                else if (_routes.TryGetValue((Asterisk, emailTo2 ?? Asterisk), out var routes1))
                    result.AddRange(routes1);
                else if (_routes.TryGetValue((emailFrom2, Asterisk), out var routes2))
                    result.AddRange(routes2);
                else if (_routes.TryGetValue((Asterisk, Asterisk), out var routes3))
                    result.AddRange(routes3);
            }
        }

        result = result
            .Where(r => null == chatId || r.TelegramChatId == chatId)
            .GroupBy(ch => ch.TelegramChatId)
            .Select(group => group.First())
            .ToList();

        return result;
    }

    private Dictionary<string, Regex> CompileRegexes(IOptions<RelayConfiguration> options)
    {
        Regex? Compile(string rg) => rg
            .TryCatch(r => r.IsNotNullOrEmpty(str => new Regex(str, RegexOptions.Compiled)),
                r => _logger.LogError($"Cannot compile regex '{r}'"));

        var result = new Dictionary<string, Regex>();
        foreach (var prefix in options.Value.Routing.SelectMany(r => r.Prefixes))
        {
            if (Compile(prefix.RegexpSubject) is { } subj)
                result.TryAdd(prefix.RegexpSubject, subj);
            if (Compile(prefix.RegexpBody) is { } body)
                result.TryAdd(prefix.RegexpBody, body);
        }
        return result;
    }
}
