using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmtpTelegramRelay.Services.TelegramStores;

namespace SmtpTelegramRelay.Services;

public sealed class TelegramHealthCheck : IHealthCheck
{
    private readonly TelegramStore _store;

    public TelegramHealthCheck(TelegramStore store)
    {
        _store = store;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            await _store.TestTelegramAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Telegram API is unavailable", ex);
        }
    }
}
