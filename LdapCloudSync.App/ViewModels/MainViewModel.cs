using System.Windows.Input;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Ipc;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Providers;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly SourceFileService _sourceFileService;

    public MainViewModel()
    {
        _configService   = App.ConfigService;
        _sourceFileService = new SourceFileService();

        SourcesVm      = new SourcesViewModel(_configService);
        CloudTargetsVm = new CloudTargetsViewModel(_configService, SourcesVm, _sourceFileService);
        LogViewerVm    = new LogViewerViewModel();

        SaveCommand                = new AsyncRelayCommand(SaveAsync);
        CheckServiceStatusCommand  = new AsyncRelayCommand(CheckServiceStatusAsync);
        TriggerSyncCommand         = new AsyncRelayCommand(TriggerSyncAsync, () => IsServiceRunning);
        TriggerTestSyncCommand     = new AsyncRelayCommand(TriggerTestSyncAsync);
        ReloadServiceConfigCommand = new AsyncRelayCommand(ReloadServiceConfigAsync, () => IsServiceRunning);
        OpenAssetCheckInCommand    = new RelayCommand(OpenAssetCheckInWindow);

        // When any AD source discovers OUs, rebuild the target OU selectors
        // for targets that use that source (or have no source assigned).
        SourcesVm.OUsDiscovered += RebuildTargetOUSelections;

        _ = CheckServiceStatusWithRetryAsync();
        _ = AutoDiscoverOUsAsync();
    }

    // -- Child ViewModels 

    public SourcesViewModel      SourcesVm      { get; }
    public CloudTargetsViewModel CloudTargetsVm { get; }
    public LogViewerViewModel    LogViewerVm    { get; }

    // -- Service status ----------------------------------------------------

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

    // -- Commands ----------------------------------------------------------

    public ICommand SaveCommand                { get; }
    public ICommand CheckServiceStatusCommand  { get; }
    public ICommand TriggerSyncCommand         { get; }
    public ICommand TriggerTestSyncCommand     { get; }
    public ICommand ReloadServiceConfigCommand { get; }
    public ICommand OpenAssetCheckInCommand    { get; }

    // -- OU discovery ------------------------------------------------------

    /// <summary>
    /// Retries on startup to give the service time to initialize its IPC pipe.
    /// </summary>
    private async Task CheckServiceStatusWithRetryAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(attempt == 0 ? 500 : 2000);
            await CheckServiceStatusAsync();
            if (IsServiceRunning) { StatusMessage = "Connected to service."; return; }
        }
        StatusMessage = "Service not detected. Start it manually or check the installation.";
    }

    /// <summary>
    /// Runs OU discovery for all configured AD sources on startup,
    /// so target OU selectors are pre-populated if credentials are already saved.
    /// </summary>
    private async Task AutoDiscoverOUsAsync()
    {
        foreach (var source in SourcesVm.Sources.Where(s => s.IsAdSource))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(source.Config.Ad.Domain) &&
                    !string.IsNullOrWhiteSpace(source.Config.Ad.EncryptedPassword))
                {
                    await source.DiscoverOUsAsync();
                    RebuildTargetOUSelections(
                        source.Config.Id,
                        source.DiscoveredComputerOUs,
                        source.DiscoveredUserOUs);
                }
            }
            catch
            {
                // Silent — OUs just won't be pre-populated until Test Connection
            }
        }
    }

    /// <summary>
    /// Called when any AD source fires OUsDiscovered.
    /// Rebuilds OU selectors only for targets that reference this specific source,
    /// or targets with no SourceId assigned (which default to the first AD source).
    /// </summary>
    private void RebuildTargetOUSelections(
        string sourceId,
        IEnumerable<string> computerOUs,
        IEnumerable<string> userOUs)
    {
        CloudTargetsVm.RebuildAllOUSelections(sourceId, computerOUs, userOUs);
    }

    private void ApplyDraftConfigurationToCurrent()
    {
        SourcesVm.ApplyToConfig();
        CloudTargetsVm.ApplyToConfig();
    }

    private void OpenAssetCheckInWindow()
    {
        try
        {
            var owner = System.Windows.Application.Current.MainWindow;
            App.ShowOrActivateAssetCheckInWindow(owner);
            StatusMessage = "Asset Check-In window opened.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to open Asset Check-In window: {ex.Message}";
        }
    }

    // -- Save --------------------------------------------------------------

    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Saving configuration...";

            ApplyDraftConfigurationToCurrent();

            _configService.Save();
            StatusMessage = "Configuration saved successfully.";

            if (IsServiceRunning)
            {
                var response = await IpcClient.ReloadConfigAsync();
                StatusMessage = response.Success
                    ? "Saved and service config reloaded."
                    : $"Saved, but service reload failed: {response.Message}";
            }
        }
        catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    // -- Service commands --------------------------------------------------

    private async Task CheckServiceStatusAsync()
    {
        try
        {
            var response = await IpcClient.SendCommandAsync(
                new IpcCommand { Type = IpcCommandType.GetStatus }, timeoutMs: 3000);
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

    private async Task ReloadServiceConfigAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Reloading service configuration...";
            var response = await IpcClient.ReloadConfigAsync();
            StatusMessage = response.Success
                ? "Service configuration reloaded."
                : $"Reload failed: {response.Message}";
        }
        catch (Exception ex) { StatusMessage = $"Reload failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    // -- Test Sync (dry-run preview) ---------------------------------------

    private async Task TriggerTestSyncAsync()
    {
        IsBusy = true;
        StatusMessage = "Building test sync preview...";

        try
        {
            SourcesVm.ApplyToConfig();
            CloudTargetsVm.ApplyToConfig();

            var config = _configService.Current;
            var captures = new List<DryRunCapture>();
            var enabledTargets = config.CloudTargets.Where(t => t.Enabled).ToList();

            if (enabledTargets.Count == 0)
            {
                StatusMessage = "No enabled targets. Check the checkbox next to each target.";
                return;
            }

            StatusMessage = $"Testing {enabledTargets.Count} enabled target(s)...";

            foreach (var target in enabledTargets)
            {
                StatusMessage = $"Testing target: {target.Name}...";

                var categoriesToTest = new List<(string Name, SyncCategoryConfig Config, DirectoryObjectType ObjType)>();
                if (target.Assets.Enabled && target.Assets.FieldMappings.Count > 0)
                    categoriesToTest.Add(("assets", target.Assets, DirectoryObjectType.Computer));
                if (target.Users.Enabled && target.Users.FieldMappings.Count > 0)
                    categoriesToTest.Add(("users", target.Users, DirectoryObjectType.User));

                if (categoriesToTest.Count == 0)
                {
                    captures.Add(new DryRunCapture
                    {
                        Method = "INFO",
                        Endpoint = $"[{target.Name}]",
                        JsonBody = "\"No categories enabled or no field mappings configured.\""
                    });
                    continue;
                }

                foreach (var (category, categoryConfig, objectType) in categoriesToTest)
                {
                    try
                    {
                        var sourceRecords = await ResolveSourceRecordsAsync(
                            target, config, category, categoryConfig, objectType, maxRecords: 1);

                        if (sourceRecords is null)
                        {
                            captures.Add(new DryRunCapture
                            {
                                Method   = "ERROR",
                                Endpoint = $"[{target.Name}] /{category}",
                                JsonBody = $"\"Source not found for target '{target.Name}'. Check Sources tab.\""
                            });
                            continue;
                        }

                        if (sourceRecords.Count == 0)
                        {
                            captures.Add(new DryRunCapture
                            {
                                Method   = "INFO",
                                Endpoint = $"[{target.Name}] /{category}",
                                JsonBody = $"\"No source records found for {category}. Check source configuration.\""
                            });
                            continue;
                        }

                        var engine      = new TransformEngine();
                        var cloudRecords = engine.TransformBatch(sourceRecords, categoryConfig.FieldMappings);

                        using var client = CloudClientFactory.CreateClient(target);
                        if (client is BaseCloudClient baseClient)
                        {
                            baseClient.DryRunMode = true;
                            await client.PushRecordsAsync(category, cloudRecords);
                            foreach (var capture in baseClient.DryRunCaptures)
                                capture.Endpoint = $"[{target.Name}] {capture.Endpoint}";
                            captures.AddRange(baseClient.DryRunCaptures);
                        }
                    }
                    catch (Exception ex)
                    {
                        captures.Add(new DryRunCapture
                        {
                            Method   = "ERROR",
                            Endpoint = $"[{target.Name}] /{category}",
                            JsonBody = $"\"{ex.Message}\""
                        });
                    }
                }
            }

            if (captures.Count == 0)
            {
                StatusMessage = "No enabled targets with mappings found.";
                return;
            }

            var previewVm = new TestSyncPreviewViewModel
            {
                Summary = $"{captures.Count} request(s) across {enabledTargets.Count} target(s)  |  {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };
            foreach (var c in captures) previewVm.Captures.Add(c);

            var dialog = new Views.TestSyncPreviewWindow
            {
                DataContext = previewVm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();

            if (dialog.Confirmed)
            {
                if (!IsServiceRunning)
                {
                    StatusMessage = "Preview complete but service is not running - start the service first.";
                    return;
                }
                StatusMessage = "Sending test sync (10 records) to service...";
                var response = await IpcClient.TriggerTestSyncAsync(maxRecords: 10);
                StatusMessage = response.Success
                    ? $"Test sync complete. {response.Data}"
                    : $"Test sync failed: {response.Message}";
            }
            else
            {
                StatusMessage = "Test sync cancelled.";
            }
        }
        catch (Exception ex) { StatusMessage = $"Test sync preview failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Resolves source records for a target, routing to AD, cloud API, or file source.
    /// Returns null if the source cannot be found; returns an empty list if no records exist.
    /// </summary>
    private async Task<IReadOnlyList<Dictionary<string, string>>?> ResolveSourceRecordsAsync(
        CloudTargetConfig target,
        SyncConfig config,
        string category,
        SyncCategoryConfig categoryConfig,
        DirectoryObjectType objectType,
        int maxRecords)
    {
        var sourceId = target.SourceId;

        // File source
        if (sourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
        {
            var fileName = sourceId[SourceFileService.FileSourcePrefix.Length..];
            var records  = await _sourceFileService.ReadRecordsAsync(fileName, category);
            return maxRecords > 0 ? records.Take(maxRecords).ToList() : records;
        }

        // Resolve configured source
        var source = string.IsNullOrEmpty(sourceId)
            ? null
            : config.Sources.Find(s => s.Id == sourceId);

        if (!string.IsNullOrEmpty(sourceId) && source is null)
            return null;

        if (source?.SourceType == SourceType.Cloud)
        {
            using var client = CloudSourceFactory.CreateClient(source);
            var readConfig   = category == "assets" ? source.Assets : source.Users;
            var filter       = string.IsNullOrWhiteSpace(readConfig.Filter) ? null : readConfig.Filter;
            var (records, _) = await client.GetRecordsAsync(category, filter, maxRecords);
            return records;
        }

        // AD source (explicit or fallback)
        var adConfig = source?.Ad
            ?? config.Sources.FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad;

        if (adConfig is null) return null;

        var requiredAttributes = SyncOrchestrator.GetRequiredAdAttributesPublic(categoryConfig);
        var effectiveAd        = SyncOrchestrator.BuildEffectiveAdConfigPublic(adConfig, categoryConfig, objectType);
        using var provider     = new ActiveDirectoryProvider(effectiveAd);
        return await provider.QueryAsync(objectType, requiredAttributes, maxRecords);
    }
}