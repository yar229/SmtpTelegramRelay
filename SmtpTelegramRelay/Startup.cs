using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.OpenApi;
using SmtpTelegramRelay.Configuration;
using SmtpTelegramRelay.Services;
using SmtpTelegramRelay.Services.TelegramStores;
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
            .AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SmtpTelegramRelay", Version = "v1" });
            });

        services
            .AddSingleton<TelegramStore>()
            .AddSingleton<SmtpServerBuilder>()
            .AddSingleton<Relay>()
            .AddHostedService(provider => provider.GetService<Relay>()!);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            services.AddWindowsService(options => options.ServiceName = "SMTP Telegram Relay");
        else
            services.AddSystemd();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
    {
        logger.LogInformation(
            $"Starting service in {env.EnvironmentName} environment on {Environment.MachineName} " +
            $"({Environment.UserDomainName}\\{Environment.UserName}) " +
            $"Platform: {Environment.OSVersion.Platform} {Environment.OSVersion.VersionString}");
        
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SmtpTelegramRelay API"));
        app.UseRouting();
        app.UseEndpoints(endpoints => endpoints.MapDefaultControllerRoute());
    }
}