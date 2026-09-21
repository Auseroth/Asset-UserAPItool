using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Services;
using LdapCloudSync.App.Views;

namespace LdapCloudSync.App;

public partial class App : Application
{
    public static ConfigService ConfigService { get; } = new();

    private static MainWindow? _mainWindowInstance;
    private static AssetCheckInWindow? _assetCheckInWindowInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigService.Load();
        LoggingService.Initialize(ConfigService.Current.Logging);

        var argsText = e.Args.Length == 0 ? "<none>" : string.Join(" ", e.Args);
        Serilog.Log.Information("App startup arguments: {Args}", argsText);
        Serilog.Log.Information("Kiosk launch arg from config: {Arg}", ConfigService.Current.Kiosk?.AssetCheckInLaunchArgument ?? "<null>");

        if (ShouldLaunchAssetCheckIn(e.Args))
        {
            Serilog.Log.Information("Launching Asset Check-In window.");
            ShowOrActivateAssetCheckInWindow(owner: null);
            return;
        }

        if (ShouldLaunchConfigWindow(e.Args) && !IsRunningAsAdministrator())
        {
            Serilog.Log.Information("Config launch requested without elevation; relaunching elevated.");
            if (TryLaunchElevatedConfigProcess())
                Shutdown();
            else
                ShowOrActivateMainWindow();
            return;
        }

        if (ShouldLaunchConfigWindow(e.Args))
        {
            Serilog.Log.Information("Launching Main window.");
            ShowOrActivateMainWindow();
            return;
        }

        Serilog.Log.Information("Launching Main window.");
        ShowOrActivateMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggingService.Shutdown();
        base.OnExit(e);
    }

    public static MainWindow ShowOrActivateMainWindow()
    {
        if (_mainWindowInstance is null || !_mainWindowInstance.IsLoaded)
        {
            _mainWindowInstance = new MainWindow();
            _mainWindowInstance.Closed += (_, _) => _mainWindowInstance = null;
        }

        if (_mainWindowInstance.WindowState == WindowState.Minimized)
            _mainWindowInstance.WindowState = WindowState.Normal;

        _mainWindowInstance.Show();
        _mainWindowInstance.Activate();
        Current.MainWindow = _mainWindowInstance;

        return _mainWindowInstance;
    }

    public static AssetCheckInWindow ShowOrActivateAssetCheckInWindow(Window? owner)
    {
        if (_assetCheckInWindowInstance is null || !_assetCheckInWindowInstance.IsLoaded)
        {
            _assetCheckInWindowInstance = new AssetCheckInWindow();
            _assetCheckInWindowInstance.Closed += (_, _) => _assetCheckInWindowInstance = null;
        }

        if (owner is not null && owner != _assetCheckInWindowInstance && !_assetCheckInWindowInstance.IsVisible)
            _assetCheckInWindowInstance.Owner = owner;

        if (_assetCheckInWindowInstance.WindowState == WindowState.Minimized)
            _assetCheckInWindowInstance.WindowState = WindowState.Normal;

        _assetCheckInWindowInstance.Show();
        _assetCheckInWindowInstance.Activate();

        return _assetCheckInWindowInstance;
    }

    public static bool TryLaunchElevatedConfigProcess()
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Unable to determine application path.");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--config",
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Unable to launch elevated config process.");
            return false;
        }
    }

    private static bool ShouldLaunchAssetCheckIn(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase)))
            return false;

        var configuredArgument = ConfigService.Current.Kiosk?.AssetCheckInLaunchArgument;
        var effectiveArgument = string.IsNullOrWhiteSpace(configuredArgument)
            ? KioskConfig.DefaultAssetCheckInArgument
            : configuredArgument.Trim();

        return args.Any(arg =>
            string.Equals(arg, KioskConfig.DefaultAssetCheckInArgument, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, effectiveArgument, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldLaunchConfigWindow(string[] args) =>
        args.Any(arg => string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase));

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
