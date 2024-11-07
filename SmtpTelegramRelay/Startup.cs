using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using SmtpTelegramRelay.Configuration;
using SmtpTelegramRelay.Services;
using System.Runtime.InteropServices;

namespace SmtpTelegramRelay;

public class Startup
{
    private IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services
            .Configure<RelayConfiguration>(Configuration)
            .AddControllers();

        services
            .AddSingleton<Store>()
            .AddSingleton<SmtpServerBuilder>()
            .AddSingleton<Relay>()
            .AddHostedService(provider => provider.GetService<Relay>()!);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddWindowsService(options => options.ServiceName = "SMTP Telegram Relay");
            LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(services);
        }
        else
            services.AddSystemd();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
    {
        logger.LogInformation(
            $"Starting service in {env.EnvironmentName} environment on {Environment.MachineName} " +
            $"({Environment.UserDomainName}\\{Environment.UserName}) " +
            $"Platform: {Environment.OSVersion.Platform} {Environment.OSVersion.VersionString}");
        
        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapDefaultControllerRoute());
    }
}