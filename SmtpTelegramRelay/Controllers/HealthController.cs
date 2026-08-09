using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmtpTelegramRelay.Models;

namespace SmtpTelegramRelay.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthCheckResponse>> Get(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

        var response = new HealthCheckResponse
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration,
            Entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new HealthCheckEntryResponse
                {
                    Status = entry.Value.Status.ToString(),
                    Description = entry.Value.Description,
                    Duration = entry.Value.Duration,
                    Error = entry.Value.Exception?.Message
                })
        };

        return report.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
