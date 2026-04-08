using System.Windows;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App;

public partial class App : Application
{
    public static ConfigService ConfigService { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigService.Load();
        LoggingService.Initialize(ConfigService.Current.Logging);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggingService.Shutdown();
        base.OnExit(e);
    }
}