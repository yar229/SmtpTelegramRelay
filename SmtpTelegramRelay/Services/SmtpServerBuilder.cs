using Microsoft.Extensions.Options;
using SmtpServer;
using SmtpServer.Net;
using SmtpServer.Tracing;
using SmtpTelegramRelay.Configuration;

namespace SmtpTelegramRelay.Services;

public sealed class SmtpServerBuilder
{
    private readonly ILogger<SmtpServerBuilder> _logger;
    private readonly SmtpServer.SmtpServer? _server;

    public SmtpServerBuilder(Store store, IOptionsMonitor<RelayConfiguration> options, ILogger<SmtpServerBuilder> logger)
    {
        _logger = logger;

        var serverOptions = new SmtpServerOptionsBuilder()
            .ServerName(options.CurrentValue.SmtpIp)
            .Port(options.CurrentValue.SmtpPort)
            .Build();

        var telegramStore = new SmtpServer.ComponentModel.ServiceProvider();
        telegramStore.Add(store);

        _server = new SmtpServer.SmtpServer(serverOptions, telegramStore);
        _server.SessionCreated += OnSessionCreated;
        _server.SessionCompleted += OnSessionCompleted;
        _server.SessionFaulted += OnSessionFaulted;
        _server.SessionCancelled += OnSessionCancelled;
    }

    public SmtpServer.SmtpServer? SmtpServer => _server;

    void OnSessionCreated(object? sender, SessionEventArgs e)
    {
        _logger.LogDebug("{Session} session created",
            e.Context.Properties[EndpointListener.RemoteEndPointKey]);

        e.Context.CommandExecuting += OnCommandExecuting;
    }

    void OnSessionCompleted(object? sender, SessionEventArgs e)
    {
        _logger.LogDebug("{Session} session completed",
            e.Context.Properties[EndpointListener.RemoteEndPointKey]);

        e.Context.CommandExecuting -= OnCommandExecuting;
    }

    void OnSessionFaulted(object? sender, SessionFaultedEventArgs e)
    {
        _logger.LogDebug(e.Exception, "{Session} session faulted",
            e.Context.Properties[EndpointListener.RemoteEndPointKey]);

        e.Context.CommandExecuting -= OnCommandExecuting;
    }

    void OnSessionCancelled(object? sender, SessionEventArgs e)
    {
        _logger.LogDebug("{Session} session cancelled",
            e.Context.Properties[EndpointListener.RemoteEndPointKey]);

        e.Context.CommandExecuting -= OnCommandExecuting;
    }

    void OnCommandExecuting(object? sender, SmtpCommandEventArgs e)
    {
        var writer = new StringWriter();
        new TracingSmtpCommandVisitor(writer).Visit(e.Command);
        _logger.LogDebug("{Session} command {Command}",
            e.Context.Properties[EndpointListener.RemoteEndPointKey],
            writer.ToString());
    }
}