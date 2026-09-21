using System.Windows.Input;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

public sealed class AssetCheckInViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly AssetCheckInService _assetCheckInService;
    private DateTimeOffset _lastSubmissionUtc = DateTimeOffset.MinValue;
    private string _lastSubmissionKey = string.Empty;

    public AssetCheckInViewModel()
    {
        _configService = App.ConfigService;
        _assetCheckInService = new AssetCheckInService(_configService);

        CheckInCommand = new AsyncRelayCommand(CheckInAsync);
        RefreshSummary();
    }

    private string _windowTitle = "Asset Check-In";
    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    private string _configuredTargetName = "Not configured";
    public string ConfiguredTargetName
    {
        get => _configuredTargetName;
        set => SetProperty(ref _configuredTargetName, value);
    }

    private string _launchArgument = KioskConfig.DefaultAssetCheckInArgument;
    public string LaunchArgument
    {
        get => _launchArgument;
        set => SetProperty(ref _launchArgument, value);
    }

    private int _duplicateGuardSeconds = 5;
    public int DuplicateGuardSeconds
    {
        get => _duplicateGuardSeconds;
        set => SetProperty(ref _duplicateGuardSeconds, value);
    }

    private int _autoCloseSeconds;
    public int AutoCloseSeconds
    {
        get => _autoCloseSeconds;
        set => SetProperty(ref _autoCloseSeconds, value);
    }

    private string _assetTag = string.Empty;
    public string AssetTag
    {
        get => _assetTag;
        set => SetProperty(ref _assetTag, value);
    }

    private string _serialNumber = string.Empty;
    public string SerialNumber
    {
        get => _serialNumber;
        set => SetProperty(ref _serialNumber, value);
    }

    private string _checkInNote = string.Empty;
    public string CheckInNote
    {
        get => _checkInNote;
        set => SetProperty(ref _checkInNote, value);
    }

    private string _statusMessage = "Ready.";
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

    public ICommand CheckInCommand { get; }

    public event Action? CloseRequested;
    public event Action<string>? CheckInCompleted;

    private void RefreshSummary()
    {
        var kiosk = _configService.Current.Kiosk ?? new KioskConfig();

        // Keep operator-facing title fixed even if old config has legacy wording.
        WindowTitle = "Asset Check-In";

        LaunchArgument = string.IsNullOrWhiteSpace(kiosk.AssetCheckInLaunchArgument)
            ? KioskConfig.DefaultAssetCheckInArgument
            : kiosk.AssetCheckInLaunchArgument.Trim();

        var kioskSourceId = $"{SourceFileService.FileSourcePrefix}{KioskConfig.AssetCheckInSourceFileName}";
        var targetNames = _configService.Current.CloudTargets
            .Where(t => t.Enabled)
            .Where(t => t.Assets.Enabled && t.Assets.FieldMappings.Count > 0)
            .Where(t => string.Equals(t.SourceId, kioskSourceId, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();

        ConfiguredTargetName = targetNames.Count == 0
            ? "No enabled targets using 'Check-in Kiosk' source"
            : string.Join(", ", targetNames);

        DuplicateGuardSeconds = Math.Max(0, kiosk.DuplicateGuardSeconds);
        AutoCloseSeconds = Math.Max(0, kiosk.AutoCloseSeconds);
    }

    private async Task CheckInAsync()
    {
        try
        {
            IsBusy = true;
            RefreshSummary();
            StatusMessage = "Submitting check-in...";

            var submissionKey = BuildSubmissionKey(AssetTag, SerialNumber);
            if (IsBlockedDuplicate(submissionKey))
            {
                var retrySeconds = GetRemainingGuardSeconds();
                StatusMessage = $"Duplicate blocked. Wait {retrySeconds}s before resubmitting the same scan.";
                return;
            }

            var result = await _assetCheckInService.CheckInAsync(AssetTag, SerialNumber, CheckInNote);
            StatusMessage = result.Success ? "Check-in completed." : "Check-in failed.";

            if (result.Success)
            {
                _lastSubmissionKey = submissionKey;
                _lastSubmissionUtc = DateTimeOffset.UtcNow;

                AssetTag = string.Empty;
                SerialNumber = string.Empty;
                CheckInNote = string.Empty;

                CheckInCompleted?.Invoke("Check-in complete.");

                if (AutoCloseSeconds > 0)
                {
                    StatusMessage = $"Check-in completed. Closing in {AutoCloseSeconds}s...";
                    await Task.Delay(TimeSpan.FromSeconds(AutoCloseSeconds));
                    CloseRequested?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Check-in failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool IsBlockedDuplicate(string submissionKey)
    {
        if (DuplicateGuardSeconds <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(submissionKey) ||
            !string.Equals(submissionKey, _lastSubmissionKey, StringComparison.OrdinalIgnoreCase))
            return false;

        var elapsed = DateTimeOffset.UtcNow - _lastSubmissionUtc;
        return elapsed < TimeSpan.FromSeconds(DuplicateGuardSeconds);
    }

    private int GetRemainingGuardSeconds()
    {
        var elapsed = DateTimeOffset.UtcNow - _lastSubmissionUtc;
        var remaining = TimeSpan.FromSeconds(DuplicateGuardSeconds) - elapsed;
        return Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
    }

    private static string BuildSubmissionKey(string assetTag, string serialNumber)
        => $"{assetTag?.Trim()}|{serialNumber?.Trim()}";
}
