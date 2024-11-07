using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using NLog.Web;

namespace SmtpTelegramRelay;

public static class Program
{
    private static void Main()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .SetBasePath(AppContext.BaseDirectory)
            .AddYamlFile("appsettings.yaml", optional: true)
            .Build();

        var builder = WebHost
            .CreateDefaultBuilder();

        IWebHost webHost = builder
            .UseConfiguration(config)
            .UseNLog()
            .UseStartup<Startup>()
            .UseUrls($"http://{config.GetValue<string>("HttpAddress")}:{config.GetValue<int>("HttpPort")}/")
            .Build();

        webHost.Run();
    }
}