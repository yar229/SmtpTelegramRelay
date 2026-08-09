namespace SmtpTelegramRelay.Models;

public class HealthCheckResponse
{
    public string Status { get; set; } = string.Empty;
    public TimeSpan TotalDuration { get; set; }
    public Dictionary<string, HealthCheckEntryResponse> Entries { get; set; } = new();
}

public class HealthCheckEntryResponse
{
    public string Status { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Error { get; set; }
}
