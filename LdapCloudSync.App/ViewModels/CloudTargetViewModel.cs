using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Presets;
using LdapCloudSync.Core.Providers;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

public sealed class CloudTargetViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    internal CloudTargetConfig Config { get; }

    // Saved selections loaded from config, applied when OUs are discovered
    internal List<string> _savedAssetsOUs = [];
    internal List<string> _savedUsersOUs = [];

    private readonly SourcesViewModel _sourcesVm;
    private readonly SourceFileService _sourceFileService;

    public CloudTargetViewModel(
        CloudTargetConfig config,
        ConfigService configService,
        SourcesViewModel sourcesVm,
        SourceFileService sourceFileService)
    {
        Config             = config;
        _configService     = configService;
        _sourcesVm         = sourcesVm;
        _sourceFileService = sourceFileService;
        LoadFromConfig();

        ApplyPresetCommand            = new RelayCommand<string>(ApplyPreset);
        DiscoverAssetFieldsCommand    = new AsyncRelayCommand(DiscoverAssetFieldsAsync);
        DiscoverUserFieldsCommand     = new AsyncRelayCommand(DiscoverUserFieldsAsync);
        DiscoverAdComputerFieldsCommand = new AsyncRelayCommand(DiscoverAdComputerFieldsAsync);
        DiscoverAdUserFieldsCommand   = new AsyncRelayCommand(DiscoverAdUserFieldsAsync);
        DiscoverSourceStatusOptionsCommand = new AsyncRelayCommand(DiscoverSourceStatusOptionsAsync);
        DiscoverTargetStatusOptionsCommand = new AsyncRelayCommand(DiscoverTargetStatusOptionsAsync);
        TestCloudConnectionCommand    = new AsyncRelayCommand(TestCloudConnectionAsync);
        AddAssetMappingCommand = new RelayCommand(() =>
        {
            var m = new FieldMappingViewModel { SampleRecord = _sampleComputerRecord };
            AssetMappings.Add(m);
        });
        AddUserMappingCommand = new RelayCommand(() =>
        {
            var m = new FieldMappingViewModel { SampleRecord = _sampleUserRecord };
            UserMappings.Add(m);
        });
        AddAssetStatusMappingCommand = new RelayCommand(() =>
        {
            var m = new ReftabStatusMappingViewModel();
            m.SetSampleRecord(_sampleComputerRecord);
            m.SetSourceStatusOptions(DiscoveredSourceStatusOptions);
            m.SetTargetStatusOptions(TargetStatusOptions);
            AssetStatusMappings.Add(m);
        });
        RemoveAssetMappingCommand = new RelayCommand<FieldMappingViewModel>(m => { if (m is not null) AssetMappings.Remove(m); });
        RemoveUserMappingCommand  = new RelayCommand<FieldMappingViewModel>(m => { if (m is not null) UserMappings.Remove(m); });
        RemoveAssetStatusMappingCommand = new RelayCommand<ReftabStatusMappingViewModel>(m => { if (m is not null) AssetStatusMappings.Remove(m); });
        RefreshCategoriesCommand  = new AsyncRelayCommand(RefreshCategoriesAsync);
        RefreshLocationsCommand   = new AsyncRelayCommand(RefreshLocationsAsync);
        SelectAllAssetsOUsCommand = new RelayCommand<bool>(SelectAllAssetsOUs);
        SelectAllUsersOUsCommand  = new RelayCommand<bool>(SelectAllUsersOUs);
        PreviewAssetMappingsCommand = new AsyncRelayCommand(PreviewAssetMappingsAsync);
        PreviewUserMappingsCommand  = new AsyncRelayCommand(PreviewUserMappingsAsync);
        RunLocalTestSyncCommand     = new AsyncRelayCommand(RunLocalTestSyncAsync);
        RefreshSourceOptionsCommand = new RelayCommand(RefreshSourceOptions);


        RefreshApAccountsCommand           = new AsyncRelayCommand(RefreshApAccountsAsync);
        RefreshApModulesCommand            = new AsyncRelayCommand(RefreshApModulesAsync);
        RefreshApAssetsCollectionsCommand  = new AsyncRelayCommand(() => RefreshApCollectionsAsync("assets"));
        RefreshApUsersCollectionsCommand   = new AsyncRelayCommand(() => RefreshApCollectionsAsync("users"));
        RefreshApAssetColumnsCommand       = new AsyncRelayCommand(() => RefreshApColumnsAsync("assets"));
        RefreshApUserColumnsCommand        = new AsyncRelayCommand(() => RefreshApColumnsAsync("users"));

        RefreshPairedSourceOUsCommand = new AsyncRelayCommand(() => RefreshPairedSourceOUsAsync());

        RefreshSourceOptions();
    }

    #region Identity & Preset

    private string _name = "New Target";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    private string _presetOrigin = "Generic";
    public string PresetOrigin
    {
        get => _presetOrigin;
        set => SetProperty(ref _presetOrigin, value);
    }

    #endregion

    #region Connection

    private string _baseUrl = string.Empty;
    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    private AuthType _authType = AuthType.ApiKey;
    public AuthType AuthType
    {
        get => _authType;
        set => SetProperty(ref _authType, value);
    }

    private string _apiKey = string.Empty;
    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    private string _apiSecret = string.Empty;
    public string ApiSecret
    {
        get => _apiSecret;
        set => SetProperty(ref _apiSecret, value);
    }

    private string _apiKeyHeader = "Authorization";
    public string ApiKeyHeader
    {
        get => _apiKeyHeader;
        set => SetProperty(ref _apiKeyHeader, value);
    }

    private string _apiKeyFormat = "Bearer {key}";
    public string ApiKeyFormat
    {
        get => _apiKeyFormat;
        set => SetProperty(ref _apiKeyFormat, value);
    }

    private string _basicUsername = string.Empty;
    public string BasicUsername
    {
        get => _basicUsername;
        set => SetProperty(ref _basicUsername, value);
    }

    private string _basicPassword = string.Empty;
    public string BasicPassword
    {
        get => _basicPassword;
        set => SetProperty(ref _basicPassword, value);
    }

    private string _hmacAlgorithm = "HMACSHA256";
    public string HmacAlgorithm
    {
        get => _hmacAlgorithm;
        set => SetProperty(ref _hmacAlgorithm, value);
    }

    private string _contentType = "application/json";
    public string ContentType
    {
        get => _contentType;
        set => SetProperty(ref _contentType, value);
    }

    private string _cloudConnectionStatus = string.Empty;
    public string CloudConnectionStatus
    {
        get => _cloudConnectionStatus;
        set => SetProperty(ref _cloudConnectionStatus, value);
    }

    #endregion

    #region Assets Config

    private bool _assetsEnabled;
    public bool AssetsEnabled
    {
        get => _assetsEnabled;
        set
        {
            if (SetProperty(ref _assetsEnabled, value))
                OnPropertyChanged(nameof(ShowReftabAssetStatusMappings));
        }
    }

    private string _assetsGetEndpoint = string.Empty;
    public string AssetsGetEndpoint
    {
        get => _assetsGetEndpoint;
        set => SetProperty(ref _assetsGetEndpoint, value);
    }

    private string _assetsPostEndpoint = string.Empty;
    public string AssetsPostEndpoint
    {
        get => _assetsPostEndpoint;
        set => SetProperty(ref _assetsPostEndpoint, value);
    }

    private string _assetsPutEndpoint = string.Empty;
    public string AssetsPutEndpoint
    {
        get => _assetsPutEndpoint;
        set => SetProperty(ref _assetsPutEndpoint, value);
    }

    private string _assetsResponsePath = "$";
    public string AssetsResponsePath
    {
        get => _assetsResponsePath;
        set => SetProperty(ref _assetsResponsePath, value);
    }

    private string _assetsCloudIdField = "id";
    public string AssetsCloudIdField
    {
        get => _assetsCloudIdField;
        set => SetProperty(ref _assetsCloudIdField, value);
    }

    private string _assetsAdMatchField = "cn";
    public string AssetsAdMatchField
    {
        get => _assetsAdMatchField;
        set => SetProperty(ref _assetsAdMatchField, value);
    }

    private string _assetsCloudMatchField = string.Empty;
    public string AssetsCloudMatchField
    {
        get => _assetsCloudMatchField;
        set => SetProperty(ref _assetsCloudMatchField, value);
    }

    private string _assetsAdSearchBaseOverride = string.Empty;
    public string AssetsAdSearchBaseOverride
    {
        get => _assetsAdSearchBaseOverride;
        set
        {
            if (SetProperty(ref _assetsAdSearchBaseOverride, value))
                OnPropertyChanged(nameof(AssetsEffectiveFilter));
        }
    }

    private string _assetsAdFilterOverride = string.Empty;
    public string AssetsAdFilterOverride
    {
        get => _assetsAdFilterOverride;
        set
        {
            if (SetProperty(ref _assetsAdFilterOverride, value))
                OnPropertyChanged(nameof(AssetsEffectiveFilter));
        }
    }

    // Update match fields for PUT fallback (user-configurable)
    private string _assetsUpdateMatchAdField = string.Empty;
    public string AssetsUpdateMatchAdField
    {
        get => _assetsUpdateMatchAdField;
        set => SetProperty(ref _assetsUpdateMatchAdField, value);
    }

    private string _assetsUpdateMatchCloudField = string.Empty;
    public string AssetsUpdateMatchCloudField
    {
        get => _assetsUpdateMatchCloudField;
        set => SetProperty(ref _assetsUpdateMatchCloudField, value);
    }

    private string _assetsSecondaryUpdateMatchAdField = string.Empty;
    public string AssetsSecondaryUpdateMatchAdField
    {
        get => _assetsSecondaryUpdateMatchAdField;
        set => SetProperty(ref _assetsSecondaryUpdateMatchAdField, value);
    }

    private string _assetsSecondaryUpdateMatchCloudField = string.Empty;
    public string AssetsSecondaryUpdateMatchCloudField
    {
        get => _assetsSecondaryUpdateMatchCloudField;
        set => SetProperty(ref _assetsSecondaryUpdateMatchCloudField, value);
    }

    #endregion

    #region Users Config

    private bool _usersEnabled;
    public bool UsersEnabled
    {
        get => _usersEnabled;
        set => SetProperty(ref _usersEnabled, value);
    }

    private string _usersGetEndpoint = string.Empty;
    public string UsersGetEndpoint
    {
        get => _usersGetEndpoint;
        set => SetProperty(ref _usersGetEndpoint, value);
    }

    private string _usersPostEndpoint = string.Empty;
    public string UsersPostEndpoint
    {
        get => _usersPostEndpoint;
        set => SetProperty(ref _usersPostEndpoint, value);
    }

    private string _usersPutEndpoint = string.Empty;
    public string UsersPutEndpoint
    {
        get => _usersPutEndpoint;
        set => SetProperty(ref _usersPutEndpoint, value);
    }

    private string _usersResponsePath = "$";
    public string UsersResponsePath
    {
        get => _usersResponsePath;
        set => SetProperty(ref _usersResponsePath, value);
    }

    private string _usersCloudIdField = "id";
    public string UsersCloudIdField
    {
        get => _usersCloudIdField;
        set => SetProperty(ref _usersCloudIdField, value);
    }

    private string _usersAdMatchField = "sAMAccountName";
    public string UsersAdMatchField
    {
        get => _usersAdMatchField;
        set => SetProperty(ref _usersAdMatchField, value);
    }

    private string _usersCloudMatchField = string.Empty;
    public string UsersCloudMatchField
    {
        get => _usersCloudMatchField;
        set => SetProperty(ref _usersCloudMatchField, value);
    }

    private string _usersAdSearchBaseOverride = string.Empty;
    public string UsersAdSearchBaseOverride
    {
        get => _usersAdSearchBaseOverride;
        set
        {
            if (SetProperty(ref _usersAdSearchBaseOverride, value))
                OnPropertyChanged(nameof(UsersEffectiveFilter));
        }
    }

    private string _usersAdFilterOverride = string.Empty;
    public string UsersAdFilterOverride
    {
        get => _usersAdFilterOverride;
        set
        {
            if (SetProperty(ref _usersAdFilterOverride, value))
                OnPropertyChanged(nameof(UsersEffectiveFilter));
        }
    }

    private bool _usersAdSourceIsGroup;
    public bool UsersAdSourceIsGroup
    {
        get => _usersAdSourceIsGroup;
        set
        {
            if (SetProperty(ref _usersAdSourceIsGroup, value))
                OnPropertyChanged(nameof(UsersEffectiveFilter));
        }
    }

    // Update match fields for PUT fallback (user-configurable)
    private string _usersUpdateMatchAdField = string.Empty;
    public string UsersUpdateMatchAdField
    {
        get => _usersUpdateMatchAdField;
        set => SetProperty(ref _usersUpdateMatchAdField, value);
    }

    private string _usersUpdateMatchCloudField = string.Empty;
    public string UsersUpdateMatchCloudField
    {
        get => _usersUpdateMatchCloudField;
        set => SetProperty(ref _usersUpdateMatchCloudField, value);
    }

    private string _usersSecondaryUpdateMatchAdField = string.Empty;
    public string UsersSecondaryUpdateMatchAdField
    {
        get => _usersSecondaryUpdateMatchAdField;
        set => SetProperty(ref _usersSecondaryUpdateMatchAdField, value);
    }

    private string _usersSecondaryUpdateMatchCloudField = string.Empty;
    public string UsersSecondaryUpdateMatchCloudField
    {
        get => _usersSecondaryUpdateMatchCloudField;
        set => SetProperty(ref _usersSecondaryUpdateMatchCloudField, value);
    }

    #endregion

    #region Schedule

    private bool _coupledSchedule = true;
    public bool CoupledSchedule
    {
        get => _coupledSchedule;
        set => SetProperty(ref _coupledSchedule, value);
    }

    private ScheduleType _primaryScheduleType = ScheduleType.Interval;
    public ScheduleType PrimaryScheduleType
    {
        get => _primaryScheduleType;
        set => SetProperty(ref _primaryScheduleType, value);
    }

    private double _primaryIntervalHours = 4;
    public double PrimaryIntervalHours
    {
        get => _primaryIntervalHours;
        set => SetProperty(ref _primaryIntervalHours, value);
    }

    private string _primaryCronExpression = "0 2 * * *";
    public string PrimaryCronExpression
    {
        get => _primaryCronExpression;
        set => SetProperty(ref _primaryCronExpression, value);
    }

    private TimeOnly _primaryDailyAtTime = new(2, 0);
    public TimeOnly PrimaryDailyAtTime
    {
        get => _primaryDailyAtTime;
        set
        {
            if (SetProperty(ref _primaryDailyAtTime, value))
                OnPropertyChanged(nameof(PrimaryDailyAtTimeText));
        }
    }

    private ScheduleType _usersScheduleType = ScheduleType.Interval;
    public ScheduleType UsersScheduleType
    {
        get => _usersScheduleType;
        set => SetProperty(ref _usersScheduleType, value);
    }

    private double _usersIntervalHours = 4;
    public double UsersIntervalHours
    {
        get => _usersIntervalHours;
        set => SetProperty(ref _usersIntervalHours, value);
    }

    private string _usersCronExpression = "0 2 * * *";
    public string UsersCronExpression
    {
        get => _usersCronExpression;
        set => SetProperty(ref _usersCronExpression, value);
    }

    private TimeOnly _usersDailyAtTime = new(2, 0);
    public TimeOnly UsersDailyAtTime
    {
        get => _usersDailyAtTime;
        set
        {
            if (SetProperty(ref _usersDailyAtTime, value))
                OnPropertyChanged(nameof(UsersDailyAtTimeText));
        }
    }

    /// <summary>
    /// String wrapper for PrimaryDailyAtTime for easy TextBox binding. Format: HH:mm
    /// </summary>
    public string PrimaryDailyAtTimeText
    {
        get => PrimaryDailyAtTime.ToString("HH:mm");
        set
        {
            if (TimeOnly.TryParse(value, out var time))
                PrimaryDailyAtTime = time;
        }
    }

    /// <summary>
    /// String wrapper for UsersDailyAtTime for easy TextBox binding. Format: HH:mm
    /// </summary>
    public string UsersDailyAtTimeText
    {
        get => UsersDailyAtTime.ToString("HH:mm");
        set
        {
            if (TimeOnly.TryParse(value, out var time))
                UsersDailyAtTime = time;
        }
    }

    #endregion

    #region Field Mappings

    public ObservableCollection<FieldMappingViewModel> AssetMappings { get; } = [];
    public ObservableCollection<FieldMappingViewModel> UserMappings { get; } = [];
    public ObservableCollection<ReftabStatusMappingViewModel> AssetStatusMappings { get; } = [];

    /// <summary>Discovered cloud field names from test pull.</summary>
    public ObservableCollection<string> DiscoveredAssetCloudFields { get; } = [];
    public ObservableCollection<string> DiscoveredUserCloudFields { get; } = [];

    /// <summary>Discovered AD attribute names.</summary>
    public ObservableCollection<string> DiscoveredAdComputerAttributes { get; } = [];
    public ObservableCollection<string> DiscoveredAdUserAttributes { get; } = [];

    // Reftab status lookup helper options
    public ObservableCollection<string> SourceStatusFields { get; } = [];
    public ObservableCollection<string> TargetStatusFields { get; } = [];
    public ObservableCollection<string> DiscoveredSourceStatusOptions { get; } = [];
    public ObservableCollection<string> DiscoveredTargetStatusOptions { get; } = [];

    private string _selectedSourceStatusField = string.Empty;
    public string SelectedSourceStatusField
    {
        get => _selectedSourceStatusField;
        set => SetProperty(ref _selectedSourceStatusField, value);
    }

    private string _selectedTargetStatusField = string.Empty;
    public string SelectedTargetStatusField
    {
        get => _selectedTargetStatusField;
        set => SetProperty(ref _selectedTargetStatusField, value);
    }

    private IReadOnlyDictionary<string, string> TargetStatusOptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Reftab Status Lookup Discovery

    private async Task DiscoverSourceStatusOptionsAsync()
    {
        CloudConnectionStatus = "Discovering source status options...";
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedSourceStatusField))
            {
                CloudConnectionStatus = "Select a source status field first.";
                return;
            }

            var records = await ResolveSourceStatusRecordsAsync(SelectedSourceStatusField);
            if (records.Count == 0)
            {
                CloudConnectionStatus = "No source records found for status discovery.";
                return;
            }

            SetAssetSampleRecord(records[0]);

            var statuses = records
                .SelectMany(r => ExtractNestedStatusValues(r, SelectedSourceStatusField))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            DiscoveredSourceStatusOptions.Clear();
            foreach (var status in statuses)
                DiscoveredSourceStatusOptions.Add(status);

            foreach (var mapping in AssetStatusMappings)
                mapping.SetSourceStatusOptions(DiscoveredSourceStatusOptions);

            CloudConnectionStatus = $"Discovered {DiscoveredSourceStatusOptions.Count} source status options.";
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Source status discovery failed: {ex.Message}";
        }
    }

    private async Task DiscoverTargetStatusOptionsAsync()
    {
        CloudConnectionStatus = "Discovering target status options...";
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedTargetStatusField))
            {
                CloudConnectionStatus = "Select a target status field first.";
                return;
            }

            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is not BaseCloudClient baseClient)
            {
                CloudConnectionStatus = "Target status discovery is only supported for cloud clients.";
                return;
            }

            var (records, _) = await baseClient.GetRecordsAsync("assets", null, maxRecords: 500);
            if (records.Count == 0)
            {
                CloudConnectionStatus = "No target records found for status discovery.";
                return;
            }

            var statusLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                var names = ExtractNestedStatusValues(record, SelectedTargetStatusField);
                foreach (var name in names)
                {
                    if (!statusLookup.ContainsKey(name))
                    {
                        var resolvedStatId = ResolveTargetStatId(record, SelectedTargetStatusField, name);
                        statusLookup[name] = resolvedStatId ?? string.Empty;
                    }
                }
            }

            TargetStatusOptions = statusLookup;
            DiscoveredTargetStatusOptions.Clear();
            foreach (var status in statusLookup.Keys.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
                DiscoveredTargetStatusOptions.Add(status);

            foreach (var mapping in AssetStatusMappings)
                mapping.SetTargetStatusOptions(TargetStatusOptions);

            CloudConnectionStatus = $"Discovered {DiscoveredTargetStatusOptions.Count} target status options.";
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Target status discovery failed: {ex.Message}";
        }
    }

    private async Task<IReadOnlyList<Dictionary<string, string>>> ResolveSourceStatusRecordsAsync(string selectedField)
    {
        ApplyToConfig();

        if (SourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
        {
            var fileName = SourceId[SourceFileService.FileSourcePrefix.Length..];
            var all = await _sourceFileService.ReadRecordsAsync(fileName, "assets");
            return all.Take(500).ToList();
        }

        var cloudSource = ResolveCloudSource();
        if (cloudSource is not null)
        {
            cloudSource.ApplyToConfig();
            using var sourceClient = CloudSourceFactory.CreateClient(cloudSource.Config);
            var filter = cloudSource.AssetsFilter;
            var (records, _) = await sourceClient.GetRecordsAsync(
                "assets", string.IsNullOrWhiteSpace(filter) ? null : filter, maxRecords: 500);
            return records;
        }

        var adConfig = ResolveAdConfig() ?? _configService.Current.Sources
            .FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad;

        if (adConfig is null)
            return [];

        var effectiveAd = SyncOrchestrator.BuildEffectiveAdConfigPublic(adConfig, Config.Assets, DirectoryObjectType.Computer);
        using var provider = new ActiveDirectoryProvider(effectiveAd);
        return await provider.QueryAsync(DirectoryObjectType.Computer, [selectedField], maxResults: 500);
    }

    private static string? ResolveTargetStatId(
        Dictionary<string, string> record,
        string selectedField,
        string selectedStatusName)
    {
        if (record.TryGetValue("statid", out var directStatId) && !string.IsNullOrWhiteSpace(directStatId))
            return directStatId;

        var nestedStatIdKey = selectedField.Trim() + ".statid";
        if (record.TryGetValue(nestedStatIdKey, out var nestedStatId) && !string.IsNullOrWhiteSpace(nestedStatId))
            return nestedStatId;

        var nestedNameKey = selectedField.Trim() + ".name";
        if (record.TryGetValue(nestedNameKey, out var nestedName)
            && string.Equals(nestedName, selectedStatusName, StringComparison.OrdinalIgnoreCase)
            && record.TryGetValue(nestedStatIdKey, out var nestedMappedStatId)
            && !string.IsNullOrWhiteSpace(nestedMappedStatId))
        {
            return nestedMappedStatId;
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractNestedStatusValues(
        Dictionary<string, string> sample,
        string selectedField)
    {
        if (sample.Count == 0 || string.IsNullOrWhiteSpace(selectedField))
            return [];

        var directValues = sample.Keys
            .Where(k => string.Equals(k, selectedField, StringComparison.OrdinalIgnoreCase))
            .Select(k => sample[k])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (directValues.Count > 0)
            return directValues;

        var nameKey = selectedField.Trim() + ".name";
        if (sample.TryGetValue(nameKey, out var nestedNameValue) && !string.IsNullOrWhiteSpace(nestedNameValue))
            return [nestedNameValue];

        var prefix = selectedField.Trim() + ".";
        var nested = sample.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .Select(k =>
            {
                var dot = k.IndexOf('.');
                return dot >= 0 ? k[..dot] : k;
            })
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return nested;
    }

    #endregion

    #region Commands

    public ICommand ApplyPresetCommand { get; }
    public ICommand DiscoverAssetFieldsCommand { get; }
    public ICommand DiscoverUserFieldsCommand { get; }
    public ICommand DiscoverAdComputerFieldsCommand { get; }
    public ICommand DiscoverAdUserFieldsCommand { get; }
    public ICommand DiscoverSourceStatusOptionsCommand { get; }
    public ICommand DiscoverTargetStatusOptionsCommand { get; }
    public ICommand TestCloudConnectionCommand { get; }
    public ICommand AddAssetMappingCommand { get; }
    public ICommand AddUserMappingCommand { get; }
    public ICommand AddAssetStatusMappingCommand { get; }
    public ICommand RemoveAssetMappingCommand { get; }
    public ICommand RemoveUserMappingCommand { get; }
    public ICommand RemoveAssetStatusMappingCommand { get; }
    public ICommand RefreshCategoriesCommand { get; }
    public ICommand RefreshLocationsCommand { get; }
    public ICommand SelectAllAssetsOUsCommand { get; }
    public ICommand SelectAllUsersOUsCommand { get; }
    public ICommand PreviewAssetMappingsCommand { get; }
    public ICommand PreviewUserMappingsCommand { get; }
    public ICommand RunLocalTestSyncCommand { get; }
    public ICommand RefreshSourceOptionsCommand { get; }
    public ICommand RefreshPairedSourceOUsCommand { get; }

    // Asset Panda commands
    public ICommand RefreshApAccountsCommand { get; }
    public ICommand RefreshApModulesCommand { get; }
    public ICommand RefreshApAssetsCollectionsCommand { get; }
    public ICommand RefreshApUsersCollectionsCommand { get; }
    public ICommand RefreshApAssetColumnsCommand { get; }
    public ICommand RefreshApUserColumnsCommand { get; }

    #endregion

    #region Source Selection

    private string _sourceId = string.Empty;
    public string SourceId
    {
        get => _sourceId;
        set
        {
            if (SetProperty(ref _sourceId, value))
            {
                OnPropertyChanged(nameof(IsFileSourceSelected));
                OnPropertyChanged(nameof(IsLinkedFileSourceSelected));

                if (!IsLinkedFileSourceSelected && FileSourceSuccessAction != FileSourceSuccessAction.None)
                    FileSourceSuccessAction = FileSourceSuccessAction.None;

                OnPropertyChanged(nameof(ShowFileSourceRenameSuffix));
            }
        }
    }

    public bool IsFileSourceSelected =>
        SourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal);

    public bool IsLinkedFileSourceSelected
    {
        get
        {
            if (!IsFileSourceSelected)
                return false;

            var fileName = SourceId[SourceFileService.FileSourcePrefix.Length..];
            return _sourceFileService.IsLinkedFileSource(fileName);
        }
    }

    private FileSourceSuccessAction _fileSourceSuccessAction = FileSourceSuccessAction.None;
    public FileSourceSuccessAction FileSourceSuccessAction
    {
        get => _fileSourceSuccessAction;
        set
        {
            if (SetProperty(ref _fileSourceSuccessAction, value))
                OnPropertyChanged(nameof(ShowFileSourceRenameSuffix));
        }
    }

    private string _fileSourceRenameSuffix = "-processed";
    public string FileSourceRenameSuffix
    {
        get => _fileSourceRenameSuffix;
        set => SetProperty(ref _fileSourceRenameSuffix, value);
    }

    public bool ShowFileSourceRenameSuffix =>
        IsLinkedFileSourceSelected && FileSourceSuccessAction == FileSourceSuccessAction.RenameFile;

    private string _selectedSourceDisplay = string.Empty;
    public string SelectedSourceDisplay
    {
        get => _selectedSourceDisplay;
        set
        {
            if (value is null) return; // Guard against null pushed by WPF during ItemsSource refresh
            if (SetProperty(ref _selectedSourceDisplay, value))
                SourceId = _sourcesVm.ResolveSourceId(value, _sourceFileService);
        }
    }

    public ObservableCollection<string> AvailableSourceOptions { get; } = [];


    private void RefreshSourceOptions()
    {
        var current = _selectedSourceDisplay;
        var displayFromSourceId = _sourcesVm.GetDisplayOptionForSourceId(SourceId, _sourceFileService);

        AvailableSourceOptions.Clear();
        foreach (var opt in _sourcesVm.GetAllSourceOptions(_sourceFileService))
            AvailableSourceOptions.Add(opt);

        _selectedSourceDisplay = AvailableSourceOptions.Contains(displayFromSourceId)
            ? displayFromSourceId
            : AvailableSourceOptions.Contains(current)
                ? current
                : AvailableSourceOptions.Count > 0 ? AvailableSourceOptions[0] : string.Empty;

        SourceId = _sourcesVm.ResolveSourceId(_selectedSourceDisplay, _sourceFileService);
        OnPropertyChanged(nameof(SelectedSourceDisplay));
    }

    /// <summary>Resolves the AD config for this target's source, or null if not AD.</summary>
    private AdConnectionConfig? ResolveAdConfig()
    {
        if (SourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
            return null;

        if (string.IsNullOrEmpty(SourceId))
        {
            var firstAd = _sourcesVm.Sources.FirstOrDefault(s => s.IsAdSource);
            firstAd?.ApplyToConfig();
            return firstAd?.Config.Ad;
        }

        var sourceVm = _sourcesVm.Sources.FirstOrDefault(s => s.Config.Id == SourceId);
        if (sourceVm?.IsAdSource == true) { sourceVm.ApplyToConfig(); return sourceVm.Config.Ad; }
        return null;
    }

    /// <summary>Resolves the cloud source VM for this target, or null if AD/file.</summary>
    private CloudSourceViewModel? ResolveCloudSource()
    {
        if (string.IsNullOrEmpty(SourceId) ||
            SourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
            return null;

        var vm = _sourcesVm.Sources.FirstOrDefault(s => s.Config.Id == SourceId);
        return vm?.IsCloudSource == true ? vm : null;
    }

    #endregion

    #region Paired Source OU Refresh


    private async Task RefreshPairedSourceOUsAsync()
    {
        CloudSourceViewModel? adSourceVm = string.IsNullOrEmpty(SourceId)
            ? _sourcesVm.Sources.FirstOrDefault(s => s.IsAdSource)
            : _sourcesVm.Sources.FirstOrDefault(s => s.Config.Id == SourceId && s.IsAdSource);

        if (adSourceVm is null)
        {
            CloudConnectionStatus = "No AD source is paired with this target. Add one on the Sources tab.";
            return;
        }

        CloudConnectionStatus = "Refreshing OUs from paired AD source...";
        adSourceVm.ApplyToConfig();
        await adSourceVm.DiscoverOUsAsync();
        RebuildOUSelections(adSourceVm.DiscoveredComputerOUs, adSourceVm.DiscoveredUserOUs);
        CloudConnectionStatus = $"OUs refreshed: {adSourceVm.DiscoveredComputerOUs.Count} computer OUs, " +
                                $"{adSourceVm.DiscoveredUserOUs.Count} user OUs.";
    }

    #endregion

    #region Provider Type & Visibility

    private string _providerType = "Generic (Standard REST)";
    public string ProviderType
    {
        get => _providerType;
        set
        {
            if (SetProperty(ref _providerType, value))
            {
                OnPropertyChanged(nameof(ShowCategorySelector));
                OnPropertyChanged(nameof(ShowLocationSelector));
                OnPropertyChanged(nameof(ShowApSettings));
                OnPropertyChanged(nameof(ShowApAssetSettings));
                OnPropertyChanged(nameof(ShowApUserSettings));
                OnPropertyChanged(nameof(HideGenericEndpoints));
                OnPropertyChanged(nameof(ShowReftabAssetStatusMappings));
            }
        }
    }

    public bool ShowCategorySelector => ProviderType == "Reftab";
    public bool ShowLocationSelector => ProviderType == "Reftab";
    public bool ShowApSettings => ProviderType == "AssetPanda";
    public bool ShowApAssetSettings => ProviderType == "AssetPanda";
    public bool ShowApUserSettings => ProviderType == "AssetPanda";
    public bool HideGenericEndpoints => ProviderType == "AssetPanda";
    public bool ShowReftabAssetStatusMappings => ProviderType == "Reftab" && AssetsEnabled;

    #endregion

    #region Reftab-Specific (Categories, Locations)

    public class CategoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class LocationItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public string CategorySelector { get; set; } = string.Empty;
    public ObservableCollection<CategoryItem> AssetCategories { get; } = [];
    public ObservableCollection<LocationItem> Locations { get; } = new();

    private int _assetsTargetCategoryId;
    public int AssetsTargetCategoryId
    {
        get => _assetsTargetCategoryId;
        set => SetProperty(ref _assetsTargetCategoryId, value);
    }

    private int _selectedLocationId;
    public int SelectedLocationId
    {
        get => _selectedLocationId;
        set => SetProperty(ref _selectedLocationId, value);
    }

    private async Task RefreshCategoriesAsync()
    {
        try
        {
            if (ProviderType != "Reftab")
            {
                CloudConnectionStatus = "Category discovery not supported for this provider.";
                return;
            }

            CloudConnectionStatus = "Fetching categories...";
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is ReftabClient reftabClient)
            {
                var categories = await reftabClient.GetCategoriesAsync();

                AssetCategories.Clear();
                AssetCategories.Add(new CategoryItem { Id = 0, Name = "(None - use Reftab default)" });
                foreach (var (cid, name) in categories)
                {
                    AssetCategories.Add(new CategoryItem { Id = cid, Name = name });
                }

                CloudConnectionStatus = $"Loaded {categories.Count} categories.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Category fetch failed: {ex.Message}";
        }
    }

    private async Task RefreshLocationsAsync()
    {
        try
        {
            if (ProviderType != "Reftab")
            {
                CloudConnectionStatus = "Location discovery not supported for this provider.";
                return;
            }

            CloudConnectionStatus = "Fetching locations...";
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is ReftabClient reftabClient)
            {
                var locations = await reftabClient.GetLocationsAsync();

                Locations.Clear();
                Locations.Add(new LocationItem { Id = 0, Name = "(None)" });
                foreach (var (id, name) in locations)
                {
                    Locations.Add(new LocationItem { Id = id, Name = name });
                }

                CloudConnectionStatus = $"Loaded {locations.Count} locations.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Location fetch failed: {ex.Message}";
        }
    }

    #endregion

    #region Asset Panda Discovery

    public class ApIdNameItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public ObservableCollection<ApIdNameItem> ApAccounts { get; } = [];
    public ObservableCollection<ApIdNameItem> ApModules { get; } = [];
    public ObservableCollection<ApIdNameItem> ApAssetsCollections { get; } = [];
    public ObservableCollection<ApIdNameItem> ApUsersCollections { get; } = [];

    private string _apAccountId = string.Empty;
    public string ApAccountId
    {
        get => _apAccountId;
        set => SetProperty(ref _apAccountId, value);
    }

    private string _apModuleId = string.Empty;
    public string ApModuleId
    {
        get => _apModuleId;
        set => SetProperty(ref _apModuleId, value);
    }

    private string _apAssetsCollectionId = string.Empty;
    public string ApAssetsCollectionId
    {
        get => _apAssetsCollectionId;
        set => SetProperty(ref _apAssetsCollectionId, value);
    }

    private string _apUsersCollectionId = string.Empty;
    public string ApUsersCollectionId
    {
        get => _apUsersCollectionId;
        set => SetProperty(ref _apUsersCollectionId, value);
    }

    private async Task RefreshApAccountsAsync()
    {
        try
        {
            CloudConnectionStatus = "Fetching Asset Panda accounts...";
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is AssetPandaClient apClient)
            {
                var accounts = await apClient.GetAccountsAsync();

                if (accounts.Count == 0)
                {
                    CloudConnectionStatus = "No accounts returned. Check API Key and Secret.";
                    return;
                }

                ApAccounts.Clear();
                foreach (var (id, name) in accounts)
                    ApAccounts.Add(new ApIdNameItem { Id = id, Name = name });

                CloudConnectionStatus = $"Loaded {accounts.Count} accounts.";
            }
            else
            {
                CloudConnectionStatus = $"Wrong client type: {client.GetType().Name}. Expected AssetPandaClient.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Account fetch failed: {ex.Message}";
            if (ex.InnerException is not null)
                CloudConnectionStatus += $" ({ex.InnerException.Message})";
        }
    }

    private async Task RefreshApModulesAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(ApAccountId))
            {
                CloudConnectionStatus = "Select an Account first.";
                return;
            }

            CloudConnectionStatus = "Fetching Asset Panda modules...";
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is AssetPandaClient apClient)
            {
                var modules = await apClient.GetModulesAsync(ApAccountId);

                if (modules.Count == 0)
                {
                    CloudConnectionStatus = "No modules returned for this account.";
                    return;
                }

                ApModules.Clear();
                foreach (var (id, name) in modules)
                    ApModules.Add(new ApIdNameItem { Id = id, Name = name });

                CloudConnectionStatus = $"Loaded {modules.Count} modules.";
            }
            else
            {
                CloudConnectionStatus = $"Wrong client type: {client.GetType().Name}. Expected AssetPandaClient.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Module fetch failed: {ex.Message}";
            if (ex.InnerException is not null)
                CloudConnectionStatus += $" ({ex.InnerException.Message})";
        }
    }

    private async Task RefreshApCollectionsAsync(string target)
    {
        try
        {
            if (string.IsNullOrEmpty(ApAccountId) || string.IsNullOrEmpty(ApModuleId))
            {
                CloudConnectionStatus = "Select an Account and Module first.";
                return;
            }

            CloudConnectionStatus = "Fetching Asset Panda collections...";
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is AssetPandaClient apClient)
            {
                var collections = await apClient.GetCollectionsAsync(ApAccountId, ApModuleId);
                var targetList = target == "assets" ? ApAssetsCollections : ApUsersCollections;

                targetList.Clear();
                foreach (var (id, name) in collections)
                    targetList.Add(new ApIdNameItem { Id = id, Name = name });

                CloudConnectionStatus = $"Loaded {collections.Count} collections.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Collection fetch failed: {ex.Message}";
        }
    }

    private async Task RefreshApColumnsAsync(string target)
    {
        try
        {
            var collectionId = target == "assets" ? ApAssetsCollectionId : ApUsersCollectionId;
            if (string.IsNullOrEmpty(ApAccountId) || string.IsNullOrEmpty(ApModuleId) || string.IsNullOrEmpty(collectionId))
            {
                CloudConnectionStatus = "Select Account, Module, and Collection first.";
                return;
            }

            CloudConnectionStatus = "Fetching collection columns...";
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is AssetPandaClient apClient)
            {
                var columns = await apClient.GetCollectionColumnsAsync(ApAccountId, ApModuleId, collectionId);
                var fieldList = target == "assets" ? DiscoveredAssetCloudFields : DiscoveredUserCloudFields;

                fieldList.Clear();
                foreach (var (_, displayName) in columns)
                    fieldList.Add(displayName);

                CloudConnectionStatus = $"Loaded {columns.Count} columns as cloud fields.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Column fetch failed: {ex.Message}";
        }
    }

    #endregion

    #region Preset Logic

    private void ApplyPreset(string? presetName)
    {
        if (string.IsNullOrEmpty(presetName) || presetName == "Blank (REST)")
        {
            // Reset everything to blank defaults
            BaseUrl = string.Empty;
            AuthType = AuthType.ApiKey;
            ApiKey = string.Empty;
            ApiSecret = string.Empty;
            ApiKeyHeader = "Authorization";
            ApiKeyFormat = "Bearer {key}";
            BasicUsername = string.Empty;
            BasicPassword = string.Empty;
            HmacAlgorithm = "HMACSHA256";
            ContentType = "application/json";

            AssetsEnabled = false;
            AssetsGetEndpoint = string.Empty;
            AssetsPostEndpoint = string.Empty;
            AssetsPutEndpoint = string.Empty;
            AssetsResponsePath = "$";
            AssetsCloudIdField = "id";
            AssetsAdMatchField = "cn";
            AssetsCloudMatchField = string.Empty;
            AssetMappings.Clear();
            AssetStatusMappings.Clear();

            UsersEnabled = false;
            UsersGetEndpoint = string.Empty;
            UsersPostEndpoint = string.Empty;
            UsersPutEndpoint = string.Empty;
            UsersResponsePath = "$";
            UsersCloudIdField = "id";
            UsersAdMatchField = "sAMAccountName";
            UsersCloudMatchField = string.Empty;
            UserMappings.Clear();

            _presetOrigin = "Blank (REST)";
            OnPropertyChanged(nameof(PresetOrigin));
            Name = $"Target {DateTime.Now:HHmmss}";
            return;
        }

        var preset = PresetRegistry.GetByName(presetName);
        if (preset is null) return;

        var config = PresetRegistry.CreateFromPreset(preset);

        // Connection
        BaseUrl = config.Connection.BaseUrl;
        AuthType = config.Connection.AuthType;
        ApiKeyHeader = config.Connection.ApiKeyHeader;
        ApiKeyFormat = config.Connection.ApiKeyFormat;
        HmacAlgorithm = config.Connection.HmacAlgorithm;
        ContentType = config.Connection.ContentType;

        // Assets endpoints
        AssetsGetEndpoint = config.Assets.GetEndpoint;
        AssetsPostEndpoint = config.Assets.PostEndpoint;
        AssetsPutEndpoint = config.Assets.PutEndpoint;
        AssetsResponsePath = config.Assets.ResponseItemsPath;
        AssetsCloudIdField = config.Assets.CloudIdField;

        // Assets match fields
        AssetsAdMatchField = config.Assets.AdMatchField;
        AssetsCloudMatchField = config.Assets.CloudMatchField;

        // Assets default mappings
        AssetMappings.Clear();
        foreach (var mapping in config.Assets.FieldMappings)
        {
            AssetMappings.Add(new FieldMappingViewModel
            {
                SourceField         = mapping.SourceFields.FirstOrDefault() ?? string.Empty,
                CloudField          = mapping.CloudField,
                TransformExpression = mapping.TransformExpression ?? string.Empty,
                DefaultValue        = mapping.DefaultValue ?? string.Empty
            });
        }

        AssetStatusMappings.Clear();
        foreach (var mapping in config.Assets.ReftabStatusMappings)
            AssetStatusMappings.Add(new ReftabStatusMappingViewModel(mapping));

        // Users endpoints
        UsersGetEndpoint = config.Users.GetEndpoint;
        UsersPostEndpoint = config.Users.PostEndpoint;
        UsersPutEndpoint = config.Users.PutEndpoint;
        UsersResponsePath = config.Users.ResponseItemsPath;
        UsersCloudIdField = config.Users.CloudIdField;

        // Users match fields
        UsersAdMatchField = config.Users.AdMatchField;
        UsersCloudMatchField = config.Users.CloudMatchField;

        // Users default mappings
        UserMappings.Clear();
        foreach (var mapping in config.Users.FieldMappings)
        {
            UserMappings.Add(new FieldMappingViewModel
            {
                SourceField         = mapping.SourceFields.FirstOrDefault() ?? string.Empty,
                CloudField          = mapping.CloudField,
                TransformExpression = mapping.TransformExpression ?? string.Empty,
                DefaultValue        = mapping.DefaultValue ?? string.Empty
            });
        }

        // Enable both categories by default when applying a preset
        AssetsEnabled = true;
        UsersEnabled = true;

        _presetOrigin = preset.Name;
        OnPropertyChanged(nameof(PresetOrigin));
        Name = preset.Name;
        
        // Add this line to set the provider type when applying a preset
        ProviderType = preset.ProviderType;
    }

    #endregion

    #region Field Discovery

    private async Task DiscoverAssetFieldsAsync()
    {
        CloudConnectionStatus = "Discovering asset fields...";
        var fields = await DiscoverCloudFieldsAsync("assets");
        DiscoveredAssetCloudFields.Clear();
        foreach (var f in fields) DiscoveredAssetCloudFields.Add(f);
        CloudConnectionStatus = $"Discovered {fields.Count} asset fields.";

        UpdateTargetStatusFields(fields);
    }

    private async Task DiscoverUserFieldsAsync()
    {
        CloudConnectionStatus = "Discovering user fields...";
        var fields = await DiscoverCloudFieldsAsync("users");
        DiscoveredUserCloudFields.Clear();
        foreach (var f in fields) DiscoveredUserCloudFields.Add(f);
        CloudConnectionStatus = $"Discovered {fields.Count} user fields.";
    }

    private async Task DiscoverAdComputerFieldsAsync()
    {
        // File source path
        if (SourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
        {
            try
            {
                var fileName = SourceId[SourceFileService.FileSourcePrefix.Length..];
                var records = await _sourceFileService.ReadRecordsAsync(fileName, "assets");
                var fields = records
                    .SelectMany(r => r.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                DiscoveredAdComputerAttributes.Clear();
                foreach (var f in fields) DiscoveredAdComputerAttributes.Add(f);
                UpdateSourceStatusFields(fields);

                if (records.Count > 0) SetAssetSampleRecord(records[0]);
                CloudConnectionStatus = $"Discovered {fields.Count} source fields from file source '{fileName}'.";
            }
            catch (Exception ex)
            {
                CloudConnectionStatus = $"File source discovery failed: {ex.Message}";
            }
            return;
        }

        // Cloud source path
        var cloudSource = ResolveCloudSource();
        if (cloudSource is not null)
        {
            CloudConnectionStatus = "Discovering source fields from cloud source...";
            try
            {
                cloudSource.ApplyToConfig();
                using var client = CloudSourceFactory.CreateClient(cloudSource.Config);
                var (fields, _) = await client.DiscoverFieldsAsync("assets");
                DiscoveredAdComputerAttributes.Clear();
                foreach (var f in fields) DiscoveredAdComputerAttributes.Add(f);
                UpdateSourceStatusFields(fields);
                CloudConnectionStatus = $"Discovered {fields.Count} source fields from cloud source '{cloudSource.Name}'.";
            }
            catch (Exception ex) { CloudConnectionStatus = $"Discovery failed: {ex.Message}"; }
            return;
        }

        // AD source path
        var adConfig = ResolveAdConfig();
        if (adConfig is null)
        {
            CloudConnectionStatus = "Source is not an AD source. Use 'Discover Source Fields' on the Sources tab.";
            return;
        }
        CloudConnectionStatus = "Discovering AD computer attributes...";
        try
        {
            using var provider = new ActiveDirectoryProvider(adConfig);
            var attrs = await provider.GetAvailableAttributesAsync(DirectoryObjectType.Computer);
            DiscoveredAdComputerAttributes.Clear();
            foreach (var a in attrs) DiscoveredAdComputerAttributes.Add(a);
            UpdateSourceStatusFields(attrs);

            try
            {
                var sample = await provider.QueryAsync(DirectoryObjectType.Computer, attrs.ToList(), maxResults: 1);
                if (sample.Count > 0) SetAssetSampleRecord(sample[0]);
            }
            catch { }

            CloudConnectionStatus = $"Discovered {attrs.Count} AD computer attributes.";
        }
        catch (Exception ex) { CloudConnectionStatus = $"AD discovery failed: {ex.Message}"; }
    }

    private async Task DiscoverAdUserFieldsAsync()
    {
        // File source path
        if (SourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
        {
            try
            {
                var fileName = SourceId[SourceFileService.FileSourcePrefix.Length..];
                var records = await _sourceFileService.ReadRecordsAsync(fileName, "users");
                var fields = records
                    .SelectMany(r => r.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                DiscoveredAdUserAttributes.Clear();
                foreach (var f in fields) DiscoveredAdUserAttributes.Add(f);

                if (records.Count > 0) SetUserSampleRecord(records[0]);
                CloudConnectionStatus = $"Discovered {fields.Count} source fields from file source '{fileName}'.";
            }
            catch (Exception ex)
            {
                CloudConnectionStatus = $"File source discovery failed: {ex.Message}";
            }
            return;
        }

        // Cloud source path
        var cloudSource = ResolveCloudSource();
        if (cloudSource is not null)
        {
            CloudConnectionStatus = "Discovering source fields from cloud source...";
            try
            {
                cloudSource.ApplyToConfig();
                using var client = CloudSourceFactory.CreateClient(cloudSource.Config);
                var (fields, _) = await client.DiscoverFieldsAsync("users");
                DiscoveredAdUserAttributes.Clear();
                foreach (var f in fields) DiscoveredAdUserAttributes.Add(f);
                CloudConnectionStatus = $"Discovered {fields.Count} source fields from cloud source '{cloudSource.Name}'.";
            }
            catch (Exception ex) { CloudConnectionStatus = $"Discovery failed: {ex.Message}"; }
            return;
        }

        // AD source path
        var adConfig = ResolveAdConfig();
        if (adConfig is null)
        {
            CloudConnectionStatus = "Source is not an AD source. Use 'Discover Source Fields' on the Sources tab.";
            return;
        }
        CloudConnectionStatus = "Discovering AD user attributes...";
        try
        {
            using var provider = new ActiveDirectoryProvider(adConfig);
            var attrs = await provider.GetAvailableAttributesAsync(DirectoryObjectType.User);
            DiscoveredAdUserAttributes.Clear();
            foreach (var a in attrs) DiscoveredAdUserAttributes.Add(a);

            try
            {
                var sample = await provider.QueryAsync(DirectoryObjectType.User, attrs.ToList(), maxResults: 1);
                if (sample.Count > 0) SetUserSampleRecord(sample[0]);
            }
            catch { }

            CloudConnectionStatus = $"Discovered {attrs.Count} AD user attributes.";
        }
        catch (Exception ex) { CloudConnectionStatus = $"AD discovery failed: {ex.Message}"; }
    }

    private async Task<IReadOnlyList<string>> DiscoverCloudFieldsAsync(string category)
    {
        try
        {
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);
            var (fields, _) = await client.DiscoverFieldsAsync(category);
            return fields;
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Discovery failed: {ex.Message}";
            return [];
        }
    }

    private async Task TestCloudConnectionAsync()
    {
        CloudConnectionStatus = "Testing connection...";
        try
        {
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);
            var (success, message) = await client.TestConnectionAsync();
            CloudConnectionStatus = message;
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Error: {ex.Message}";
        }
    }

    #endregion

    #region Config Mapping

    private void LoadFromConfig()
    {
        Name = Config.Name;
        Enabled = Config.Enabled;
        PresetOrigin = Config.PresetOrigin;

        // Connection
        BaseUrl = Config.Connection.BaseUrl;
        AuthType = Config.Connection.AuthType;
        ApiKey = Config.Connection.ApiKey;
        ApiSecret = Config.Connection.ApiSecret;
        ApiKeyHeader = Config.Connection.ApiKeyHeader;
        ApiKeyFormat = Config.Connection.ApiKeyFormat;
        BasicUsername = Config.Connection.BasicUsername;
        BasicPassword = Config.Connection.BasicPassword;
        HmacAlgorithm = Config.Connection.HmacAlgorithm;
        ContentType = Config.Connection.ContentType;

        // Assets
        AssetsEnabled = Config.Assets.Enabled;
        AssetsGetEndpoint = Config.Assets.GetEndpoint;
        AssetsPostEndpoint = Config.Assets.PostEndpoint;
        AssetsPutEndpoint = Config.Assets.PutEndpoint;
        AssetsResponsePath = Config.Assets.ResponseItemsPath;
        AssetsCloudIdField = Config.Assets.CloudIdField;
        AssetsAdMatchField = Config.Assets.AdMatchField;
        AssetsCloudMatchField = Config.Assets.CloudMatchField;
        // AssetsAdSearchBaseOverride = Config.Assets.AdSearchBaseOverride;
        // UsersAdSearchBaseOverride = Config.Users.AdSearchBaseOverride;
        _savedAssetsOUs = Config.Assets.AdSearchBaseOverrides;
        AssetsAdFilterOverride = Config.Assets.AdFilterOverride;

        // Update match fields for PUT fallback
        AssetsUpdateMatchAdField = Config.Assets.UpdateMatchAdField;
        AssetsUpdateMatchCloudField = Config.Assets.UpdateMatchCloudField;
        AssetsSecondaryUpdateMatchAdField = Config.Assets.SecondaryUpdateMatchSourceField;
        AssetsSecondaryUpdateMatchCloudField = Config.Assets.SecondaryUpdateMatchCloudField;

        AssetMappings.Clear();
        foreach (var m in Config.Assets.FieldMappings)
            AssetMappings.Add(new FieldMappingViewModel(m));

        SelectedSourceStatusField = Config.Assets.ReftabStatusSourceField;
        SelectedTargetStatusField = Config.Assets.ReftabStatusTargetField;

        // Backfill legacy saved rows that predate TargetStatusName.
        foreach (var m in Config.Assets.ReftabStatusMappings)
        {
            if (string.IsNullOrWhiteSpace(m.TargetStatusName) && !string.IsNullOrWhiteSpace(m.StatId))
                m.TargetStatusName = m.StatusName;
        }

        AssetStatusMappings.Clear();
        foreach (var m in Config.Assets.ReftabStatusMappings)
            AssetStatusMappings.Add(new ReftabStatusMappingViewModel(m));

        UpdateSourceStatusFields(DiscoveredAdComputerAttributes.Count > 0
            ? DiscoveredAdComputerAttributes
            : [SelectedSourceStatusField]);

        UpdateTargetStatusFields(DiscoveredAssetCloudFields.Count > 0
            ? DiscoveredAssetCloudFields
            : [SelectedTargetStatusField]);

        if (AssetStatusMappings.Count == 0 && string.Equals(Config.ProviderType, "Reftab", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = AssetMappings.Count - 1; i >= 0; i--)
            {
                var mapping = AssetMappings[i];
                if (IsLegacyReftabStatusFieldMapping(mapping))
                {
                    AssetStatusMappings.Insert(0, new ReftabStatusMappingViewModel
                    {
                        SourceStatus = mapping.SourceField,
                        TargetStatus = mapping.SourceField,
                        StatId = mapping.CloudField
                    });
                    AssetMappings.RemoveAt(i);
                }
            }
        }

        foreach (var statusMapping in AssetStatusMappings)
        {
            statusMapping.SetSampleRecord(_sampleComputerRecord);
            statusMapping.SetSourceStatusOptions(DiscoveredSourceStatusOptions);
            statusMapping.SetTargetStatusOptions(TargetStatusOptions);
        }

        // Users
        UsersEnabled = Config.Users.Enabled;
        UsersGetEndpoint = Config.Users.GetEndpoint;
        UsersPostEndpoint = Config.Users.PostEndpoint;
        UsersPutEndpoint = Config.Users.PutEndpoint;
        UsersResponsePath = Config.Users.ResponseItemsPath;
        UsersCloudIdField = Config.Users.CloudIdField;
        UsersAdMatchField = Config.Users.AdMatchField;
        UsersCloudMatchField = Config.Users.CloudMatchField;
        _savedUsersOUs = Config.Users.AdSearchBaseOverrides;
        UsersAdFilterOverride = Config.Users.AdFilterOverride;
        UsersAdSourceIsGroup = Config.Users.AdSourceIsGroup;

        // Update match fields for PUT fallback
        UsersUpdateMatchAdField = Config.Users.UpdateMatchAdField;
        UsersUpdateMatchCloudField = Config.Users.UpdateMatchCloudField;
        UsersSecondaryUpdateMatchAdField = Config.Users.SecondaryUpdateMatchSourceField;
        UsersSecondaryUpdateMatchCloudField = Config.Users.SecondaryUpdateMatchCloudField;

        UserMappings.Clear();
        foreach (var m in Config.Users.FieldMappings)
            UserMappings.Add(new FieldMappingViewModel(m));

        // Schedule
        CoupledSchedule = Config.Schedule.CoupledSchedule;
        PrimaryScheduleType = Config.Schedule.PrimarySchedule.Type;
        PrimaryIntervalHours = Config.Schedule.PrimarySchedule.Interval.TotalHours;
        PrimaryCronExpression = Config.Schedule.PrimarySchedule.CronExpression;
        PrimaryDailyAtTime = Config.Schedule.PrimarySchedule.DailyAtTime;
        UsersScheduleType = Config.Schedule.UsersSchedule.Type;
        UsersIntervalHours = Config.Schedule.UsersSchedule.Interval.TotalHours;
        UsersCronExpression = Config.Schedule.UsersSchedule.CronExpression;
        UsersDailyAtTime = Config.Schedule.UsersSchedule.DailyAtTime;

        // Assets category + location
        AssetsTargetCategoryId = Config.Assets.TargetCategoryId;
        SelectedLocationId = Config.Assets.TargetLocationId;

        // Provider type  must load BEFORE auto-refresh which calls ApplyToConfig
        ProviderType = Config.ProviderType;

        // Asset Panda discovery IDs
        // Asset Panda discovery IDs  seed placeholder items BEFORE setting
        // SelectedValue so WPF doesn't clear the binding when ItemsSource is empty.
        if (!string.IsNullOrEmpty(Config.ApAccountId))
        {
            ApAccounts.Clear();
            ApAccounts.Add(new ApIdNameItem { Id = Config.ApAccountId, Name = Config.ApAccountId });
        }
        if (!string.IsNullOrEmpty(Config.ApModuleId))
        {
            ApModules.Clear();
            ApModules.Add(new ApIdNameItem { Id = Config.ApModuleId, Name = Config.ApModuleId });
        }
        if (!string.IsNullOrEmpty(Config.ApAssetsCollectionId))
        {
            ApAssetsCollections.Clear();
            ApAssetsCollections.Add(new ApIdNameItem { Id = Config.ApAssetsCollectionId, Name = Config.ApAssetsCollectionId });
        }
        if (!string.IsNullOrEmpty(Config.ApUsersCollectionId))
        {
            ApUsersCollections.Clear();
            ApUsersCollections.Add(new ApIdNameItem { Id = Config.ApUsersCollectionId, Name = Config.ApUsersCollectionId });
        }

        ApAccountId = Config.ApAccountId;
        ApModuleId = Config.ApModuleId;
        ApAssetsCollectionId = Config.ApAssetsCollectionId;
        ApUsersCollectionId = Config.ApUsersCollectionId;

        // Source selection (must happen before background auto-load calls that use BuildTargetConfig/ApplyToConfig)
        SourceId = Config.SourceId;
        _selectedSourceDisplay = _sourcesVm.GetDisplayOptionForSourceId(Config.SourceId, _sourceFileService);
        OnPropertyChanged(nameof(SelectedSourceDisplay));

        // File-source success actions (linked-file sources only)
        FileSourceRenameSuffix = string.IsNullOrWhiteSpace(Config.FileSourceRenameSuffix)
            ? "-processed"
            : Config.FileSourceRenameSuffix;
        FileSourceSuccessAction = IsLinkedFileSourceSelected
            ? Config.FileSourceSuccessAction
            : FileSourceSuccessAction.None;

        // Auto-load Reftab-specific data
        if (ShowCategorySelector)
        {
             _ = LoadReftabDataAsync();
        }

        // Auto-resolve AP friendly names in background
        if (ShowApSettings && !string.IsNullOrEmpty(Config.ApAccountId))
        {
            _ = LoadApDiscoveryDataAsync();
        }
    }

    /// <summary>
    /// Loads Reftab categories and locations sequentially to avoid
    /// concurrent BuildTargetConfig/ApplyToConfig race conditions.
    /// </summary>
    private async Task LoadReftabDataAsync()
    {
        await RefreshCategoriesAsync();
        await RefreshLocationsAsync();
    }

    /// <summary>
    /// Refreshes AP dropdowns sequentially so saved IDs resolve to friendly names.
    /// Uses Config directly instead of BuildTargetConfig to avoid ApplyToConfig race conditions.
    /// </summary>
    private async Task LoadApDiscoveryDataAsync()
    {
        try
        {
            using var client = CloudClientFactory.CreateClient(Config);
            if (client is not AssetPandaClient apClient) return;

            var accounts = await apClient.GetAccountsAsync();
            if (accounts.Count > 0)
            {
                ApAccounts.Clear();
                foreach (var (id, name) in accounts)
                    ApAccounts.Add(new ApIdNameItem { Id = id, Name = name });
            }

            if (!string.IsNullOrEmpty(ApModuleId))
            {
                var modules = await apClient.GetModulesAsync(ApAccountId);
                if (modules.Count > 0)
                {
                    ApModules.Clear();
                    foreach (var (id, name) in modules)
                        ApModules.Add(new ApIdNameItem { Id = id, Name = name });
                }
            }

            if (!string.IsNullOrEmpty(ApAssetsCollectionId))
            {
                var collections = await apClient.GetCollectionsAsync(ApAccountId, ApModuleId);
                if (collections.Count > 0)
                {
                    ApAssetsCollections.Clear();
                    foreach (var (id, name) in collections)
                        ApAssetsCollections.Add(new ApIdNameItem { Id = id, Name = name });
                }
            }

            if (!string.IsNullOrEmpty(ApUsersCollectionId))
            {
                var collections = await apClient.GetCollectionsAsync(ApAccountId, ApModuleId);
                if (collections.Count > 0)
                {
                    ApUsersCollections.Clear();
                    foreach (var (id, name) in collections)
                        ApUsersCollections.Add(new ApIdNameItem { Id = id, Name = name });
                }
            }

            CloudConnectionStatus = "AP discovery data loaded.";
        }
        catch
        {
            // Silent  placeholders with IDs remain if API is unreachable
        }
    }

    public void ApplyToConfig()
    {
        Config.Name = Name;
        Config.Enabled = Enabled;
        Config.PresetOrigin = PresetOrigin;

        Config.Connection.BaseUrl = BaseUrl;
        Config.Connection.AuthType = AuthType;
        Config.Connection.ApiKey = ApiKey;
        Config.Connection.ApiSecret = ApiSecret;
        Config.Connection.ApiKeyHeader = ApiKeyHeader;
        Config.Connection.ApiKeyFormat = ApiKeyFormat;
        Config.Connection.BasicUsername = BasicUsername;
        Config.Connection.BasicPassword = BasicPassword;
        Config.Connection.HmacAlgorithm = HmacAlgorithm;
        Config.Connection.ContentType = ContentType;

        Config.Assets.Enabled = AssetsEnabled;
        Config.Assets.GetEndpoint = AssetsGetEndpoint;
        Config.Assets.PostEndpoint = AssetsPostEndpoint;
        Config.Assets.PutEndpoint = AssetsPutEndpoint;
        Config.Assets.ResponseItemsPath = AssetsResponsePath;
        Config.Assets.CloudIdField = AssetsCloudIdField;
        Config.Assets.AdMatchField = AssetsAdMatchField;
        Config.Assets.CloudMatchField = AssetsCloudMatchField;
        Config.Assets.FieldMappings = AssetMappings.Select(m => m.ToModel()).ToList();
        Config.Assets.ReftabStatusSourceField = SelectedSourceStatusField;
        Config.Assets.ReftabStatusTargetField = SelectedTargetStatusField;
        Config.Assets.ReftabStatusMappings = AssetStatusMappings.Select(m => m.ToModel()).ToList();
        Config.Assets.TargetCategoryId = AssetsTargetCategoryId;
        Config.Assets.TargetLocationId = SelectedLocationId;
        Config.Assets.AdSearchBaseOverrides = GetSelectedAssetsOUs();
        Config.Assets.AdFilterOverride = AssetsAdFilterOverride;
        Config.Assets.UpdateMatchAdField = AssetsUpdateMatchAdField;
        Config.Assets.UpdateMatchCloudField = AssetsUpdateMatchCloudField;
        Config.Assets.SecondaryUpdateMatchSourceField = AssetsSecondaryUpdateMatchAdField;
        Config.Assets.SecondaryUpdateMatchCloudField = AssetsSecondaryUpdateMatchCloudField;

        Config.Users.Enabled = UsersEnabled;
        Config.Users.GetEndpoint = UsersGetEndpoint;
        Config.Users.PostEndpoint = UsersPostEndpoint;
        Config.Users.PutEndpoint = UsersPutEndpoint;
        Config.Users.ResponseItemsPath = UsersResponsePath;
        Config.Users.CloudIdField = UsersCloudIdField;
        Config.Users.AdMatchField = UsersAdMatchField;
        Config.Users.CloudMatchField = UsersCloudMatchField;
        Config.Users.FieldMappings = UserMappings.Select(m => m.ToModel()).ToList();
        Config.Users.AdSearchBaseOverrides = GetSelectedUsersOUs();
        Config.Users.AdFilterOverride = UsersAdFilterOverride;
        Config.Users.AdSourceIsGroup = UsersAdSourceIsGroup;
        Config.Users.UpdateMatchAdField = UsersUpdateMatchAdField;
        Config.Users.UpdateMatchCloudField = UsersUpdateMatchCloudField;
        Config.Users.SecondaryUpdateMatchSourceField = UsersSecondaryUpdateMatchAdField;
        Config.Users.SecondaryUpdateMatchCloudField = UsersSecondaryUpdateMatchCloudField;

        Config.Schedule.CoupledSchedule = CoupledSchedule;
        Config.Schedule.PrimarySchedule.Type = PrimaryScheduleType;
        Config.Schedule.PrimarySchedule.Interval = TimeSpan.FromHours(PrimaryIntervalHours);
        Config.Schedule.PrimarySchedule.CronExpression = PrimaryCronExpression;
        Config.Schedule.PrimarySchedule.DailyAtTime = PrimaryDailyAtTime;
        Config.Schedule.UsersSchedule.Type = UsersScheduleType;
        Config.Schedule.UsersSchedule.Interval = TimeSpan.FromHours(UsersIntervalHours);
        Config.Schedule.UsersSchedule.CronExpression = UsersCronExpression;
        Config.Schedule.UsersSchedule.DailyAtTime = UsersDailyAtTime;

        // Save provider type
        Config.ProviderType = ProviderType;

        // Asset Panda discovery IDs
        Config.ApAccountId = ApAccountId;
        Config.ApModuleId = ApModuleId;
        Config.ApAssetsCollectionId = ApAssetsCollectionId;
        Config.ApUsersCollectionId = ApUsersCollectionId;

        // Source ID
        Config.SourceId = SourceId;

        // File-source success actions (persist only for linked file sources)
        Config.FileSourceSuccessAction = IsLinkedFileSourceSelected
            ? FileSourceSuccessAction
            : FileSourceSuccessAction.None;
        Config.FileSourceRenameSuffix = string.IsNullOrWhiteSpace(FileSourceRenameSuffix)
            ? "-processed"
            : FileSourceRenameSuffix.Trim();
    }

    private CloudTargetConfig BuildTargetConfig()
    {
        var config = new CloudTargetConfig();
        // Temporarily apply current VM state so we can use it for test calls
        Config.Name = Name;
        ApplyToConfig();
        return Config;
    }

    private static bool IsLegacyReftabStatusFieldMapping(FieldMappingViewModel mapping)
    {
        var source = mapping.SourceField?.Trim() ?? string.Empty;
        var cloud = mapping.CloudField?.Trim() ?? string.Empty;

        return (string.Equals(source, "status", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "status.name", StringComparison.OrdinalIgnoreCase))
            && string.Equals(cloud, "statid", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSourceStatusFields(IEnumerable<string> fields)
    {
        var saved = SelectedSourceStatusField;

        SourceStatusFields.Clear();
        foreach (var f in fields.Where(v => !string.IsNullOrWhiteSpace(v))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
            SourceStatusFields.Add(f);

        if (!string.IsNullOrWhiteSpace(saved) && !SourceStatusFields.Contains(saved))
            SourceStatusFields.Add(saved);

        if (!string.IsNullOrWhiteSpace(saved))
            SelectedSourceStatusField = saved;
        else if (SourceStatusFields.Count > 0)
            SelectedSourceStatusField = SourceStatusFields[0];
    }

    private void UpdateTargetStatusFields(IEnumerable<string> fields)
    {
        var saved = SelectedTargetStatusField;

        TargetStatusFields.Clear();
        foreach (var f in fields.Where(v => !string.IsNullOrWhiteSpace(v))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
            TargetStatusFields.Add(f);

        if (!string.IsNullOrWhiteSpace(saved) && !TargetStatusFields.Contains(saved))
            TargetStatusFields.Add(saved);

        if (!string.IsNullOrWhiteSpace(saved))
            SelectedTargetStatusField = saved;
        else if (TargetStatusFields.Count > 0)
            SelectedTargetStatusField = TargetStatusFields[0];
    }

    #endregion

    #region Effective Filters & OU Selection

    public string AssetsEffectiveFilter
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AssetsAdFilterOverride))
                return $"Filter: {AssetsAdFilterOverride}";

            var selected = GetSelectedAssetsOUs();

            // For non-AD sources, show source name instead of AD filter
            var cloudSource = ResolveCloudSource();
            if (cloudSource is not null)
                return selected.Count == 0
                    ? $"Source: {cloudSource.Name}"
                    : $"Source: {cloudSource.Name}  |  OUs: {selected.Count} selected";

            var adConfig = ResolveAdConfig();
            var globalFilter = adConfig?.ComputerFilter
                ?? _configService.Current.Sources.FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad.ComputerFilter
                ?? "(objectClass=computer)";

            return selected.Count == 0
                ? $"Filter (global): {globalFilter}"
                : $"Filter (global): {globalFilter}  |  OUs: {selected.Count} selected";
        }
    }

    public string UsersEffectiveFilter
    {
        get
        {
            var selected = GetSelectedUsersOUs();
            if (UsersAdSourceIsGroup && selected.Count > 0)
            {
                var groups = string.Join(", ", selected.Select(s =>
                {
                    var cnEnd = s.IndexOf(',');
                    return cnEnd > 0 ? s[..cnEnd] : s;
                }));
                return $"memberOf: {groups}";
            }

            if (!string.IsNullOrWhiteSpace(UsersAdFilterOverride))
                return $"Filter: {UsersAdFilterOverride}";

            var cloudSource = ResolveCloudSource();
            if (cloudSource is not null) return $"Source: {cloudSource.Name}";

            var adConfig = ResolveAdConfig();
            var globalFilter = adConfig?.UserFilter
                ?? _configService.Current.Sources.FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad.UserFilter
                ?? "(&(objectClass=user)(objectCategory=person))";

            return selected.Count == 0
                ? $"Filter (global): {globalFilter}"
                : $"Filter (global): {globalFilter}  |  OUs: {selected.Count} selected";
        }
    }

    public ObservableCollection<SelectableItemViewModel> AssetsOUSelections { get; } = [];

    public List<string> GetSelectedAssetsOUs()
        => AssetsOUSelections.Where(x => x.IsSelected).Select(x => x.Value).ToList();

    private void OnAssetsSelectionChanged()
    {
        OnPropertyChanged(nameof(AssetsEffectiveFilter));
    }

    public ObservableCollection<SelectableItemViewModel> UsersOUSelections { get; } = [];

    public List<string> GetSelectedUsersOUs()
        => UsersOUSelections.Where(x => x.IsSelected).Select(x => x.Value).ToList();

    private void OnUsersSelectionChanged()
    {
        OnPropertyChanged(nameof(UsersEffectiveFilter));
    }

    private void SelectAllAssetsOUs(bool select)
    {
        foreach (var item in AssetsOUSelections)
            item.IsSelected = select;
    }

    private void SelectAllUsersOUs(bool select)
    {
        foreach (var item in UsersOUSelections)
            item.IsSelected = select;
    }

    public void RebuildOUSelections(
        IEnumerable<string> computerOUs,
        IEnumerable<string> userOUs)
    {
        var previousAssets = AssetsOUSelections.Count > 0
            ? GetSelectedAssetsOUs().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _savedAssetsOUs.ToHashSet(StringComparer.OrdinalIgnoreCase);

        AssetsOUSelections.Clear();
        foreach (var ou in computerOUs)
        {
            AssetsOUSelections.Add(new SelectableItemViewModel(
                ou, previousAssets.Contains(ou), OnAssetsSelectionChanged));
        }

        var previousUsers = UsersOUSelections.Count > 0
            ? GetSelectedUsersOUs().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _savedUsersOUs.ToHashSet(StringComparer.OrdinalIgnoreCase);

        UsersOUSelections.Clear();
        foreach (var ou in userOUs)
        {
            UsersOUSelections.Add(new SelectableItemViewModel(
                ou, previousUsers.Contains(ou), OnUsersSelectionChanged));
        }

        OnPropertyChanged(nameof(AssetsEffectiveFilter));
        OnPropertyChanged(nameof(UsersEffectiveFilter));
    }

    #endregion

    #region Sample Records & Preview

    private Dictionary<string, string>? _sampleComputerRecord;
    private Dictionary<string, string>? _sampleUserRecord;

    public void SetAssetSampleRecord(Dictionary<string, string> sample)
    {
        _sampleComputerRecord = sample;
        foreach (var mapping in AssetMappings)
            mapping.SampleRecord = sample;

        foreach (var statusMapping in AssetStatusMappings)
        {
            statusMapping.SetSampleRecord(sample);
            statusMapping.SetSourceStatusOptions(DiscoveredSourceStatusOptions);
            statusMapping.SetTargetStatusOptions(TargetStatusOptions);
        }
    }

    public void SetUserSampleRecord(Dictionary<string, string> sample)
    {
        _sampleUserRecord = sample;
        foreach (var mapping in UserMappings)
            mapping.SampleRecord = sample;
    }

    private string _testSyncReport = string.Empty;
    public string TestSyncReport
    {
        get => _testSyncReport;
        set => SetProperty(ref _testSyncReport, value);
    }

    private async Task PreviewAssetMappingsAsync()
    {
        CloudConnectionStatus = "Fetching source record for preview...";
        try
        {
            ApplyToConfig();
            var categoryConfig = Config.Assets;

            var records = await ResolveSourcePreviewAsync("assets", categoryConfig, DirectoryObjectType.Computer);
            if (records.Count == 0) { CloudConnectionStatus = "No records found. Check source configuration."; return; }

            SetAssetSampleRecord(records[0]);
            CloudConnectionStatus = $"Preview loaded from: {records[0].GetValueOrDefault("cn", records[0].Keys.FirstOrDefault() ?? "(unknown)")}";

        }
        catch (Exception ex) { CloudConnectionStatus = $"Preview failed: {ex.Message}"; }
    }

    private async Task PreviewUserMappingsAsync()
    {
        CloudConnectionStatus = "Fetching source record for preview...";
        try
        {
            ApplyToConfig();
            var categoryConfig = Config.Users;

            var records = await ResolveSourcePreviewAsync("users", categoryConfig, DirectoryObjectType.User);
            if (records.Count == 0) { CloudConnectionStatus = "No records found. Check source configuration."; return; }

            SetUserSampleRecord(records[0]);
            CloudConnectionStatus = $"Preview loaded from: {records[0].GetValueOrDefault("sAMAccountName", records[0].Keys.FirstOrDefault() ?? "(unknown)")}";
        }
        catch (Exception ex) { CloudConnectionStatus = $"Preview failed: {ex.Message}"; }
    }

    /// <summary>
    /// Resolves a single preview record from whichever source this target is configured to use.
    /// </summary>
    private async Task<IReadOnlyList<Dictionary<string, string>>> ResolveSourcePreviewAsync(
        string category,
        SyncCategoryConfig categoryConfig,
        DirectoryObjectType objectType)
    {
        // File source
        if (SourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal))
        {
            var fileName = SourceId[SourceFileService.FileSourcePrefix.Length..];
            var all = await _sourceFileService.ReadRecordsAsync(fileName, category);
            return all.Take(1).ToList();
        }

        // Cloud source
        var cloudSource = ResolveCloudSource();
        if (cloudSource is not null)
        {
            cloudSource.ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(cloudSource.Config);
            var filter = category == "assets" ? cloudSource.AssetsFilter : cloudSource.UsersFilter;
            var (records, _) = await client.GetRecordsAsync(
                category, string.IsNullOrWhiteSpace(filter) ? null : filter, maxRecords: 1);
            return records;
        }

        // AD source (explicit or fallback)
        var adConfig = ResolveAdConfig() ?? _configService.Current.Sources
            .FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad;

        if (adConfig is null) return [];

        var requiredAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(categoryConfig.SourceMatchField)) requiredAttrs.Add(categoryConfig.SourceMatchField);
        foreach (var mapping in categoryConfig.FieldMappings)
        {
            foreach (var f in mapping.SourceFields) requiredAttrs.Add(f);
            if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
                foreach (var f in TransformEngine.ExtractAttributeNames(mapping.TransformExpression))
                    requiredAttrs.Add(f);
        }

        var effectiveAd = SyncOrchestrator.BuildEffectiveAdConfigPublic(adConfig, categoryConfig, objectType);
        using var provider = new ActiveDirectoryProvider(effectiveAd);
        return await provider.QueryAsync(objectType, requiredAttrs.ToList(), maxResults: 1);
    }

    #endregion

    #region Test Sync

    private async Task RunLocalTestSyncAsync()
    {
        try
        {
            CloudConnectionStatus = "Building test sync preview...";
            ApplyToConfig();
            var target = Config;

            var captures   = new List<DryRunCapture>();
            var categories = new List<string>();

            if (target.Assets.Enabled && target.Assets.FieldMappings.Count > 0) categories.Add("assets");
            if (target.Users.Enabled  && target.Users.FieldMappings.Count  > 0) categories.Add("users");

            if (categories.Count == 0)
            {
                CloudConnectionStatus = "No categories enabled or no mappings configured.";
                return;
            }

            foreach (var category in categories)
            {
                var categoryConfig = category == "assets" ? target.Assets : target.Users;
                var objectType     = category == "assets"
                    ? DirectoryObjectType.Computer
                    : DirectoryObjectType.User;

                var sourceRecords = await ResolveSourcePreviewAsync(category, categoryConfig, objectType);

                if (sourceRecords.Count == 0)
                {
                    captures.Add(new DryRunCapture
                    {
                        Method   = "INFO",
                        Endpoint = $"/{category}",
                        JsonBody = $"\"No source records found for {category}. Check source configuration.\""
                    });
                    continue;
                }

                var engine       = new TransformEngine();
                var cloudRecords = engine.TransformBatch(sourceRecords, categoryConfig.FieldMappings);

                using var client = CloudClientFactory.CreateClient(target);
                if (client is BaseCloudClient baseClient)
                {
                    baseClient.DryRunMode = true;
                    await client.PushRecordsAsync(category, cloudRecords);
                    captures.AddRange(baseClient.DryRunCaptures);
                }

                if (objectType == DirectoryObjectType.Computer)
                    SetAssetSampleRecord(sourceRecords[0]);
                else
                    SetUserSampleRecord(sourceRecords[0]);
            }

            var previewVm = new TestSyncPreviewViewModel
            {
                Summary = $"Target: {target.Name}  |  {captures.Count} request(s) captured  |  {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };
            foreach (var c in captures) previewVm.Captures.Add(c);

            var dialog = new Views.TestSyncPreviewWindow
            {
                DataContext = previewVm,
                Owner       = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();

            if (dialog.Confirmed)
            {
                CloudConnectionStatus = "Proceeding with sync...";
                _ = ExecuteConfirmedSyncAsync(categories);
            }
            else
            {
                CloudConnectionStatus = "Test sync cancelled.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Test sync failed: {ex.Message}";
            System.Windows.MessageBox.Show(
                $"Test sync failed:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Test Sync Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task ExecuteConfirmedSyncAsync(List<string> categories)
    {
        try
        {
            var target = Config;

            foreach (var category in categories)
            {
                var categoryConfig = category == "assets" ? target.Assets : target.Users;
                var objectType     = category == "assets"
                    ? DirectoryObjectType.Computer
                    : DirectoryObjectType.User;

                var sourceRecords = await ResolveSourcePreviewAsync(category, categoryConfig, objectType);
                if (sourceRecords.Count == 0) continue;

                var engine       = new TransformEngine();
                var cloudRecords = engine.TransformBatch(sourceRecords, categoryConfig.FieldMappings);

                using var client = CloudClientFactory.CreateClient(target);
                var result       = await client.PushRecordsAsync(category, cloudRecords);

                CloudConnectionStatus = $"{category}: Created {result.Created}, Updated {result.Updated}, Failed {result.Failed}";
                if (result.Errors.Count > 0)
                    CloudConnectionStatus += $" | Errors: {string.Join("; ", result.Errors)}";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Sync failed: {ex.Message}";
        }
    }

    #endregion


}

/// <summary>
/// Typed RelayCommand for commands that need a parameter.
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
}