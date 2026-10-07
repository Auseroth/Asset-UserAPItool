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

    public IReadOnlyList<string> AssetTurnInOptions { get; } = ["Yes", "No"];

    private string _windowTitle = "Create A Ticket Or Drop Off An Asset";
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

    private string _turningInAssetAnswer = string.Empty;
    public string TurningInAssetAnswer
    {
        get => _turningInAssetAnswer;
        set
        {
            if (!SetProperty(ref _turningInAssetAnswer, value))
                return;

            OnPropertyChanged(nameof(HasTurnInSelection));
            OnPropertyChanged(nameof(IsTurningInAsset));
            if (!IsTurningInAsset)
            {
                AssetTag = string.Empty;
                SerialNumber = string.Empty;
            }
        }
    }

    public bool HasTurnInSelection =>
        string.Equals(TurningInAssetAnswer, "Yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(TurningInAssetAnswer, "No", StringComparison.OrdinalIgnoreCase);

    public bool IsTurningInAsset => string.Equals(TurningInAssetAnswer, "Yes", StringComparison.OrdinalIgnoreCase);

    private string _firstName = string.Empty;
    public string FirstName
    {
        get => _firstName;
        set => SetProperty(ref _firstName, NormalizeName(value));
    }

    private string _lastName = string.Empty;
    public string LastName
    {
        get => _lastName;
        set => SetProperty(ref _lastName, NormalizeName(value));
    }

    private string _phoneNumber = string.Empty;
    public string PhoneNumber
    {
        get => _phoneNumber;
        set => SetProperty(ref _phoneNumber, FormatPhoneNumber(value));
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

    private string _requestIssueDescription = string.Empty;
    public string RequestIssueDescription
    {
        get => _requestIssueDescription;
        set => SetProperty(ref _requestIssueDescription, value);
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


        LaunchArgument = string.IsNullOrWhiteSpace(kiosk.AssetCheckInLaunchArgument)
            ? KioskConfig.DefaultAssetCheckInArgument
            : kiosk.AssetCheckInLaunchArgument.Trim();

        var kioskSourceId = $"{SourceFileService.FileSourcePrefix}{KioskConfig.AssetCheckInSourceFileName}";
        var targetNames = _configService.Current.CloudTargets
            .Where(t => t.Enabled)
            .Where(t => string.Equals(t.SourceId, kioskSourceId, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.Equals(t.ProviderType, "Email", StringComparison.OrdinalIgnoreCase)
                || (t.Assets.Enabled && t.Assets.FieldMappings.Count > 0))
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
            StatusMessage = "Submitting request...";

            if (!HasTurnInSelection)
            {
                StatusMessage = "Select Yes or No for 'Are you turning in an asset?' before submitting.";
                return;
            }

            var submissionKey = BuildSubmissionKey();
            if (IsBlockedDuplicate(submissionKey))
            {
                var retrySeconds = GetRemainingGuardSeconds();
                StatusMessage = $"Duplicate blocked. Wait {retrySeconds}s before resubmitting the same request.";
                return;
            }

            var result = await _assetCheckInService.CheckInAsync(
                IsTurningInAsset,
                FirstName,
                LastName,
                PhoneNumber,
                AssetTag,
                SerialNumber,
                RequestIssueDescription);

            StatusMessage = result.Message;

            if (!result.Success)
                return;

            _lastSubmissionKey = submissionKey;
            _lastSubmissionUtc = DateTimeOffset.UtcNow;

            FirstName = string.Empty;
            LastName = string.Empty;
            PhoneNumber = string.Empty;
            AssetTag = string.Empty;
            SerialNumber = string.Empty;
            RequestIssueDescription = string.Empty;

            CheckInCompleted?.Invoke(string.IsNullOrWhiteSpace(result.Message) ? "Request submitted." : result.Message);

            if (AutoCloseSeconds > 0)
            {
                StatusMessage = $"Submitted. Closing in {AutoCloseSeconds}s...";
                await Task.Delay(TimeSpan.FromSeconds(AutoCloseSeconds));
                CloseRequested?.Invoke();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Submission failed: {ex.Message}";
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

    private string BuildSubmissionKey()
    {
        if (IsTurningInAsset)
            return $"YES|{AssetTag.Trim()}|{SerialNumber.Trim()}|{FirstName.Trim()}|{LastName.Trim()}|{PhoneNumber.Trim()}|{RequestIssueDescription.Trim()}";

        return $"NO|{FirstName.Trim()}|{LastName.Trim()}|{PhoneNumber.Trim()}|{RequestIssueDescription.Trim()}";
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Trim().ToLowerInvariant().ToCharArray();
        var capitalizeNext = true;

        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetter(chars[i]))
            {
                if (chars[i] is ' ' or '-' or '\'')
                    capitalizeNext = true;

                continue;
            }

            if (capitalizeNext)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalizeNext = false;
            }
        }

        return new string(chars);
    }

    private static string FormatPhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var digitsOnly = new string(value.Where(char.IsDigit).Take(10).ToArray());

        if (digitsOnly.Length <= 3)
            return digitsOnly;

        if (digitsOnly.Length <= 6)
            return $"{digitsOnly[..3]}-{digitsOnly[3..]}";

        return $"{digitsOnly[..3]}-{digitsOnly[3..6]}-{digitsOnly[6..]}";
    }
}
