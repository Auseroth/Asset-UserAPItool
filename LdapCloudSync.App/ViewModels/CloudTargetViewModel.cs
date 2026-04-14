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

    public CloudTargetViewModel(CloudTargetConfig config, ConfigService configService)
    {
        Config = config;
        _configService = configService;
        LoadFromConfig();

        ApplyPresetCommand = new RelayCommand<string>(ApplyPreset);
        DiscoverAssetFieldsCommand = new AsyncRelayCommand(DiscoverAssetFieldsAsync);
        DiscoverUserFieldsCommand = new AsyncRelayCommand(DiscoverUserFieldsAsync);
        DiscoverAdComputerFieldsCommand = new AsyncRelayCommand(DiscoverAdComputerFieldsAsync);
        DiscoverAdUserFieldsCommand = new AsyncRelayCommand(DiscoverAdUserFieldsAsync);
        TestCloudConnectionCommand = new AsyncRelayCommand(TestCloudConnectionAsync);
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
        RemoveAssetMappingCommand = new RelayCommand<FieldMappingViewModel>(m => { if (m is not null) AssetMappings.Remove(m); });
        RemoveUserMappingCommand = new RelayCommand<FieldMappingViewModel>(m => { if (m is not null) UserMappings.Remove(m); });
        RefreshCategoriesCommand = new AsyncRelayCommand(RefreshCategoriesAsync);
        RefreshLocationsCommand = new AsyncRelayCommand(RefreshLocationsAsync);
        SelectAllAssetsOUsCommand = new RelayCommand<bool>(SelectAllAssetsOUs);
        SelectAllUsersOUsCommand = new RelayCommand<bool>(SelectAllUsersOUs);
        PreviewAssetMappingsCommand = new AsyncRelayCommand(PreviewAssetMappingsAsync);
        PreviewUserMappingsCommand = new AsyncRelayCommand(PreviewUserMappingsAsync);
        RunLocalTestSyncCommand = new AsyncRelayCommand(RunLocalTestSyncAsync);

        // Asset Panda discovery commands
        RefreshApAccountsCommand = new AsyncRelayCommand(RefreshApAccountsAsync);
        RefreshApModulesCommand = new AsyncRelayCommand(RefreshApModulesAsync);
        RefreshApAssetsCollectionsCommand = new AsyncRelayCommand(() => RefreshApCollectionsAsync("assets"));
        RefreshApUsersCollectionsCommand = new AsyncRelayCommand(() => RefreshApCollectionsAsync("users"));
        RefreshApAssetColumnsCommand = new AsyncRelayCommand(() => RefreshApColumnsAsync("assets"));
        RefreshApUserColumnsCommand = new AsyncRelayCommand(() => RefreshApColumnsAsync("users"));
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
        set => SetProperty(ref _assetsEnabled, value);
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

    /// <summary>Discovered cloud field names from test pull.</summary>
    public ObservableCollection<string> DiscoveredAssetCloudFields { get; } = [];
    public ObservableCollection<string> DiscoveredUserCloudFields { get; } = [];

    /// <summary>Discovered AD attribute names.</summary>
    public ObservableCollection<string> DiscoveredAdComputerAttributes { get; } = [];
    public ObservableCollection<string> DiscoveredAdUserAttributes { get; } = [];

    #endregion

    #region Commands

    public ICommand ApplyPresetCommand { get; }
    public ICommand DiscoverAssetFieldsCommand { get; }
    public ICommand DiscoverUserFieldsCommand { get; }
    public ICommand DiscoverAdComputerFieldsCommand { get; }
    public ICommand DiscoverAdUserFieldsCommand { get; }
    public ICommand TestCloudConnectionCommand { get; }
    public ICommand AddAssetMappingCommand { get; }
    public ICommand AddUserMappingCommand { get; }
    public ICommand RemoveAssetMappingCommand { get; }
    public ICommand RemoveUserMappingCommand { get; }
    public ICommand RefreshCategoriesCommand { get; }
    public ICommand RefreshLocationsCommand { get; }
    public ICommand SelectAllAssetsOUsCommand { get; }
    public ICommand SelectAllUsersOUsCommand { get; }
    public ICommand PreviewAssetMappingsCommand { get; }
    public ICommand PreviewUserMappingsCommand { get; }
    public ICommand RunLocalTestSyncCommand { get; }

    // Asset Panda commands
    public ICommand RefreshApAccountsCommand { get; }
    public ICommand RefreshApModulesCommand { get; }
    public ICommand RefreshApAssetsCollectionsCommand { get; }
    public ICommand RefreshApUsersCollectionsCommand { get; }
    public ICommand RefreshApAssetColumnsCommand { get; }
    public ICommand RefreshApUserColumnsCommand { get; }

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
            }
        }
    }

    public bool ShowCategorySelector => ProviderType == "Reftab";
    public bool ShowLocationSelector => ProviderType == "Reftab";
    public bool ShowApSettings => ProviderType == "AssetPanda";
    public bool ShowApAssetSettings => ProviderType == "AssetPanda";
    public bool ShowApUserSettings => ProviderType == "AssetPanda";
    public bool HideGenericEndpoints => ProviderType == "AssetPanda";

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
                ApAccounts.Clear();
                foreach (var (id, name) in accounts)
                    ApAccounts.Add(new ApIdNameItem { Id = id, Name = name });

                CloudConnectionStatus = $"Loaded {accounts.Count} accounts.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Account fetch failed: {ex.Message}";
        }
    }

    private async Task RefreshApModulesAsync()
    {
        try
        {
            CloudConnectionStatus = "Fetching Asset Panda modules...";
            var targetConfig = BuildTargetConfig();
            using var client = CloudClientFactory.CreateClient(targetConfig);

            if (client is AssetPandaClient apClient)
            {
                var modules = await apClient.GetModulesAsync();
                ApModules.Clear();
                foreach (var (id, name) in modules)
                    ApModules.Add(new ApIdNameItem { Id = id, Name = name });

                CloudConnectionStatus = $"Loaded {modules.Count} modules.";
            }
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Module fetch failed: {ex.Message}";
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
                AdAttribute = mapping.AdAttributes.FirstOrDefault() ?? string.Empty,
                CloudField = mapping.CloudField,
                TransformExpression = mapping.TransformExpression ?? string.Empty,
                DefaultValue = mapping.DefaultValue ?? string.Empty
            });
        }

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
                AdAttribute = mapping.AdAttributes.FirstOrDefault() ?? string.Empty,
                CloudField = mapping.CloudField,
                TransformExpression = mapping.TransformExpression ?? string.Empty,
                DefaultValue = mapping.DefaultValue ?? string.Empty
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
        CloudConnectionStatus = "Discovering AD computer attributes...";
        var adConfig = _configService.Current.ActiveDirectory;
        using var provider = new ActiveDirectoryProvider(adConfig);
        var attrs = await provider.GetAvailableAttributesAsync(DirectoryObjectType.Computer);
        DiscoveredAdComputerAttributes.Clear();
        foreach (var a in attrs) DiscoveredAdComputerAttributes.Add(a);

        // Fetch one sample record for live mapping preview
        try
        {
            var sampleRecords = await provider.QueryAsync(DirectoryObjectType.Computer, attrs.ToList(), maxResults: 1);
            if (sampleRecords.Count > 0)
                SetAssetSampleRecord(sampleRecords[0]);
        }
        catch { /* Preview just won't have live data */ }

        CloudConnectionStatus = $"Discovered {attrs.Count} AD computer attributes.";
    }

    private async Task DiscoverAdUserFieldsAsync()
    {
        CloudConnectionStatus = "Discovering AD user attributes...";
        var adConfig = _configService.Current.ActiveDirectory;
        using var provider = new ActiveDirectoryProvider(adConfig);
        var attrs = await provider.GetAvailableAttributesAsync(DirectoryObjectType.User);
        DiscoveredAdUserAttributes.Clear();
        foreach (var a in attrs) DiscoveredAdUserAttributes.Add(a);

        // Fetch one sample record for live mapping preview
        try
        {
            var sampleRecords = await provider.QueryAsync(DirectoryObjectType.User, attrs.ToList(), maxResults: 1);
            if (sampleRecords.Count > 0)
                SetUserSampleRecord(sampleRecords[0]);
        }
        catch { /* Preview just won't have live data */ }

        CloudConnectionStatus = $"Discovered {attrs.Count} AD user attributes.";
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

        AssetMappings.Clear();
        foreach (var m in Config.Assets.FieldMappings)
            AssetMappings.Add(new FieldMappingViewModel(m));

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

        // Provider type — must load BEFORE auto-refresh which calls ApplyToConfig
        ProviderType = Config.ProviderType;

        // Asset Panda discovery IDs
        ApAccountId = Config.ApAccountId;
        ApModuleId = Config.ApModuleId;
        ApAssetsCollectionId = Config.ApAssetsCollectionId;
        ApUsersCollectionId = Config.ApUsersCollectionId;

        // Auto-load Reftab-specific data
        if (ShowCategorySelector)
        {
             _ = LoadReftabDataAsync();
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
        Config.Assets.TargetCategoryId = AssetsTargetCategoryId;
        Config.Assets.TargetLocationId = SelectedLocationId;
        Config.Assets.AdSearchBaseOverrides = GetSelectedAssetsOUs();
        Config.Assets.AdFilterOverride = AssetsAdFilterOverride;

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
    }

    private CloudTargetConfig BuildTargetConfig()
    {
        var config = new CloudTargetConfig();
        // Temporarily apply current VM state so we can use it for test calls
        Config.Name = Name;
        ApplyToConfig();
        return Config;
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
            var globalFilter = _configService.Current.ActiveDirectory.ComputerFilter;
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
            var globalFilter = _configService.Current.ActiveDirectory.UserFilter;
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
        CloudConnectionStatus = "Fetching first AD computer record for preview...";
        try
        {
            ApplyToConfig();
            var globalAd = _configService.Current.ActiveDirectory;
            var categoryConfig = Config.Assets;

            var requiredAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(categoryConfig.AdMatchField))
                requiredAttrs.Add(categoryConfig.AdMatchField);
            foreach (var mapping in categoryConfig.FieldMappings)
            {
                foreach (var attr in mapping.AdAttributes)
                    requiredAttrs.Add(attr);
                if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
                    foreach (var attr in TransformEngine.ExtractAttributeNames(mapping.TransformExpression))
                        requiredAttrs.Add(attr);
            }

            var effectiveAd = SyncOrchestrator.BuildEffectiveAdConfigPublic(
                globalAd, categoryConfig, DirectoryObjectType.Computer);

            using var provider = new ActiveDirectoryProvider(effectiveAd);
            var records = await provider.QueryAsync(
                DirectoryObjectType.Computer, requiredAttrs.ToList(), maxResults: 1);

            if (records.Count == 0)
            {
                CloudConnectionStatus = "No computer records found. Check AD source and filters.";
                return;
            }

            SetAssetSampleRecord(records[0]);
            CloudConnectionStatus = $"Preview loaded from: {records[0].GetValueOrDefault("cn", "(unknown)")}";
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Preview failed: {ex.Message}";
        }
    }

    private async Task PreviewUserMappingsAsync()
    {
        CloudConnectionStatus = "Fetching first AD user record for preview...";
        try
        {
            ApplyToConfig();
            var globalAd = _configService.Current.ActiveDirectory;
            var categoryConfig = Config.Users;

            var requiredAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(categoryConfig.AdMatchField))
                requiredAttrs.Add(categoryConfig.AdMatchField);
            foreach (var mapping in categoryConfig.FieldMappings)
            {
                foreach (var attr in mapping.AdAttributes)
                    requiredAttrs.Add(attr);
                if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
                    foreach (var attr in TransformEngine.ExtractAttributeNames(mapping.TransformExpression))
                        requiredAttrs.Add(attr);
            }

            var effectiveAd = SyncOrchestrator.BuildEffectiveAdConfigPublic(
                globalAd, categoryConfig, DirectoryObjectType.User);

            using var provider = new ActiveDirectoryProvider(effectiveAd);
            var records = await provider.QueryAsync(
                DirectoryObjectType.User, requiredAttrs.ToList(), maxResults: 1);

            if (records.Count == 0)
            {
                CloudConnectionStatus = "No user records found. Check AD source and filters.";
                return;
            }

            SetUserSampleRecord(records[0]);
            CloudConnectionStatus = $"Preview loaded from: {records[0].GetValueOrDefault("sAMAccountName", "(unknown)")}";
        }
        catch (Exception ex)
        {
            CloudConnectionStatus = $"Preview failed: {ex.Message}";
        }
    }

    #endregion

    #region Test Sync

    private async Task RunLocalTestSyncAsync()
    {
        try
        {
            System.Windows.MessageBox.Show("Test Sync button clicked!", "Debug");
            CloudConnectionStatus = "Building test sync preview...";

            ApplyToConfig();
            var config = _configService.Current;
            var target = Config;

            var captures = new List<DryRunCapture>();
            var categories = new List<string>();

            if (target.Assets.Enabled && target.Assets.FieldMappings.Count > 0)
                categories.Add("assets");
            if (target.Users.Enabled && target.Users.FieldMappings.Count > 0)
                categories.Add("users");

            if (categories.Count == 0)
            {
                System.Windows.MessageBox.Show("No categories enabled or no mappings configured.", "Debug");
                CloudConnectionStatus = "No categories enabled or no mappings configured.";
                return;
            }

            System.Windows.MessageBox.Show($"Processing {categories.Count} category(ies): {string.Join(", ", categories)}", "Debug");

            foreach (var category in categories)
            {
                var categoryConfig = category == "assets" ? target.Assets : target.Users;
                var objectType = category == "assets"
                    ? DirectoryObjectType.Computer
                    : DirectoryObjectType.User;

                var requiredAttributes = SyncOrchestrator.GetRequiredAdAttributesPublic(categoryConfig);
                var effectiveAd = SyncOrchestrator.BuildEffectiveAdConfigPublic(
                    config.ActiveDirectory, categoryConfig, objectType);

                using var provider = new ActiveDirectoryProvider(effectiveAd);
                var adRecords = await provider.QueryAsync(objectType, requiredAttributes, maxResults: 1);

                System.Windows.MessageBox.Show($"AD returned {adRecords.Count} record(s) for {category}", "Debug");

                if (adRecords.Count == 0)
                {
                    captures.Add(new DryRunCapture
                    {
                        Method = "INFO",
                        Endpoint = $"/{category}",
                        JsonBody = $"\"No AD records found for {category}. Check search base and filters.\""
                    });
                    continue;
                }

                var engine = new TransformEngine();
                var cloudRecords = engine.TransformBatch(adRecords, categoryConfig.FieldMappings);

                using var client = CloudClientFactory.CreateClient(target);
                if (client is BaseCloudClient baseClient)
                {
                    baseClient.DryRunMode = true;
                    await client.PushRecordsAsync(category, cloudRecords);
                    captures.AddRange(baseClient.DryRunCaptures);
                }

                if (objectType == DirectoryObjectType.Computer)
                    SetAssetSampleRecord(adRecords[0]);
                else
                    SetUserSampleRecord(adRecords[0]);
            }

            System.Windows.MessageBox.Show($"Captured {captures.Count} request(s). Opening preview window...", "Debug");

            var previewVm = new TestSyncPreviewViewModel
            {
                Summary = $"Target: {target.Name}  |  {captures.Count} request(s) captured  |  {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };
            foreach (var c in captures)
                previewVm.Captures.Add(c);

            var dialog = new Views.TestSyncPreviewWindow
            {
                DataContext = previewVm,
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();

            if (dialog.Confirmed)
            {
                CloudConnectionStatus = "Proceeding with sync...";
                _ = ExecuteConfirmedSyncAsync(config, target, categories);
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

    private async Task ExecuteConfirmedSyncAsync(
        SyncConfig config,
        CloudTargetConfig target,
        List<string> categories)
    {
        try
        {
            foreach (var category in categories)
            {
                var categoryConfig = category == "assets" ? target.Assets : target.Users;
                var objectType = category == "assets"
                    ? DirectoryObjectType.Computer
                    : DirectoryObjectType.User;

                var requiredAttributes = SyncOrchestrator.GetRequiredAdAttributesPublic(categoryConfig);
                var effectiveAd = SyncOrchestrator.BuildEffectiveAdConfigPublic(
                    config.ActiveDirectory, categoryConfig, objectType);

                using var provider = new ActiveDirectoryProvider(effectiveAd);
                var adRecords = await provider.QueryAsync(objectType, requiredAttributes, maxResults: 1);

                if (adRecords.Count == 0) continue;

                var engine = new TransformEngine();
                var cloudRecords = engine.TransformBatch(adRecords, categoryConfig.FieldMappings);

                using var client = CloudClientFactory.CreateClient(target);
                var result = await client.PushRecordsAsync(category, cloudRecords);

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