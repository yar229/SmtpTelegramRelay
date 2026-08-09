using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using NLog.Web;

namespace SmtpTelegramRelay;

public static class Program
{
    private static void Main()
    {
        LoadDotEnv();

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment.ToLowerInvariant()}.json", optional: true)
            .Build();

        IWebHost webHost = WebHost
            .CreateDefaultBuilder()
            .UseConfiguration(config)
            .UseNLog()
            .UseStartup<Startup>()
            .UseUrls($"http://{config.GetValue<string>("HttpAddress")}:{config.GetValue<int>("HttpPort")}/")
            .Build();

        webHost.Run();
    }

    private static void LoadDotEnv()
    {
        var path = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            .Select(p => Path.Combine(p, ".env"))
            .FirstOrDefault(File.Exists);

        if (path is null)
            return;

        DotNetEnv.Env.Load(path, new DotNetEnv.LoadOptions(
            setEnvVars: true,
            clobberExistingVars: false,
            onlyExactPath: true));
    }
}
