namespace SmtpTelegramRelay.Services;

public sealed class Relay : BackgroundService
{
    private readonly ILogger<Relay> _logger;
    private readonly SmtpServer.SmtpServer? _server;

    public Relay(ILogger<Relay> logger, SmtpServerBuilder smtpServerBuilder)
    {
        _logger = logger;
        _server = smtpServerBuilder.SmtpServer;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = _server.StartAsync(stoppingToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
            Environment.Exit(1);
        }

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested && _server is not null)
        {
            _server.Shutdown();
            await _server.ShutdownTask.ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
