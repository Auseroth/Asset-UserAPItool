using LdapCloudSync.Core.Services;
using LdapCloudSync.Service.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LdapCloudSync.Service;

public static class Program
{
    public static void Main(string[] args)
    {
        // Early console logging before config is loaded
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            Log.Information("LdapCloudSync Service starting...");

            var builder = Host.CreateApplicationBuilder(args);

            // Register services
            builder.Services.AddSingleton<ConfigService>();
            builder.Services.AddHostedService<SyncWorker>();

            // Enable running as a Windows Service
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "LdapCloudSync";
            });

            var host = builder.Build();
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Service terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}