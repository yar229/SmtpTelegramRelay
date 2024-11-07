using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

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

        IWebHost webHost = WebHost
            .CreateDefaultBuilder()
            .UseConfiguration(config)
            .UseStartup<Startup>()
            .UseUrls($"http://{config.GetValue<string>("HttpAddress")}:{config.GetValue<int>("HttpPort")}/")
            .Build();

        webHost.Run();
    }
}