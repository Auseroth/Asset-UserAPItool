using System.Collections.ObjectModel;
using System.Windows.Input;
using LdapCloudSync.Core.Ipc;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ConfigService _configService;

    public MainViewModel()
    {
        _configService = App.ConfigService;

        AdSettingsVm = new AdSettingsViewModel(_configService);
        CloudTargetsVm = new CloudTargetsViewModel(_configService);
        LogViewerVm = new LogViewerViewModel();

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CheckServiceStatusCommand = new AsyncRelayCommand(CheckServiceStatusAsync);
        TriggerSyncCommand = new AsyncRelayCommand(TriggerSyncAsync, () => IsServiceRunning);
        TriggerTestSyncCommand = new AsyncRelayCommand(TriggerTestSyncAsync, () => IsServiceRunning);
        ReloadServiceConfigCommand = new AsyncRelayCommand(ReloadServiceConfigAsync, () => IsServiceRunning);

        // Check service status on load with retries (service may still be starting)
        _ = CheckServiceStatusWithRetryAsync();
    }

    // Child ViewModels
    public AdSettingsViewModel AdSettingsVm { get; }
    public CloudTargetsViewModel CloudTargetsVm { get; }
    public LogViewerViewModel LogViewerVm { get; }

    // Service status
    private bool _isServiceRunning;
    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        set => SetProperty(ref _isServiceRunning, value);
    }

    private string _serviceStatusText = "Checking...";
    public string ServiceStatusText
    {
        get => _serviceStatusText;
        set => SetProperty(ref _serviceStatusText, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    // Commands
    public ICommand SaveCommand { get; }
    public ICommand CheckServiceStatusCommand { get; }
    public ICommand TriggerSyncCommand { get; }
    public ICommand TriggerTestSyncCommand { get; }
    public ICommand ReloadServiceConfigCommand { get; }

    /// <summary>
    /// Retries the service status check a few times on startup,
    /// giving the service time to initialize its IPC pipe.
    /// </summary>
    private async Task CheckServiceStatusWithRetryAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(attempt == 0 ? 500 : 2000);
            await CheckServiceStatusAsync();

            if (IsServiceRunning)
            {
                StatusMessage = "Connected to service.";
                return;
            }
        }

        StatusMessage = "Service not detected. Start it manually or check the installation.";
    }

    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Saving configuration...";

            AdSettingsVm.ApplyToConfig();
            CloudTargetsVm.ApplyToConfig();

            _configService.Save();
            StatusMessage = "Configuration saved successfully.";

            // Notify service to reload if it's running
            if (IsServiceRunning)
            {
                var response = await IpcClient.ReloadConfigAsync();
                StatusMessage = response.Success
                    ? "Saved and service config reloaded."
                    : $"Saved, but service reload failed: {response.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckServiceStatusAsync()
    {
        try
        {
            var response = await IpcClient.SendCommandAsync(
                new IpcCommand { Type = IpcCommandType.GetStatus },
                timeoutMs: 3000);

            IsServiceRunning = response.Success;
            ServiceStatusText = response.Success ? "Running" : "Stopped";
        }
        catch
        {
            IsServiceRunning = false;
            ServiceStatusText = "Stopped";
        }
    }

    private async Task TriggerSyncAsync()
    {
        IsBusy = true;
        StatusMessage = "Triggering full sync...";

        var response = await IpcClient.TriggerSyncAsync();
        StatusMessage = response.Success
            ? $"Sync complete. {response.Data}"
            : $"Sync failed: {response.Message}";

        IsBusy = false;
    }

    private async Task TriggerTestSyncAsync()
    {
        IsBusy = true;
        StatusMessage = "Running test sync (10 records)...";

        var response = await IpcClient.TriggerTestSyncAsync(maxRecords: 10);
        StatusMessage = response.Success
            ? $"Test sync complete. {response.Data}"
            : $"Test sync failed: {response.Message}";

        IsBusy = false;
    }

    private async Task ReloadServiceConfigAsync()
    {
        var response = await IpcClient.ReloadConfigAsync();
        StatusMessage = response.Success
            ? "Service configuration reloaded."
            : $"Reload failed: {response.Message}";
    }
}