using System.Collections.ObjectModel;
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
        AddAssetMappingCommand = new RelayCommand(() => AssetMappings.Add(new FieldMappingViewModel()));
        AddUserMappingCommand = new RelayCommand(() => UserMappings.Add(new FieldMappingViewModel()));
        RemoveAssetMappingCommand = new RelayCommand<FieldMappingViewModel>(m => { if (m is not null) AssetMappings.Remove(m); });
        RemoveUserMappingCommand = new RelayCommand<FieldMappingViewModel>(m => { if (m is not null) UserMappings.Remove(m); });
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
        set
        {
            if (SetProperty(ref _presetOrigin, value))
                ApplyPreset(value);
        }
    }

    #endregion

    #region Connection

    private string _baseUrl = string.Empty;
    public string BaseUrl
    {
        get => _baseUrl;
        set { if (SetProperty(ref _baseUrl, value)) CheckPresetDrift(); }
    }

    private AuthType _authType = AuthType.ApiKey;
    public AuthType AuthType
    {
        get => _authType;
        set { if (SetProperty(ref _authType, value)) CheckPresetDrift(); }
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
        set { if (SetProperty(ref _hmacAlgorithm, value)) CheckPresetDrift(); }
    }

    private string _contentType = "application/json";
    public string ContentType
    {
        get => _contentType;
        set { if (SetProperty(ref _contentType, value)) CheckPresetDrift(); }
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
        set => SetProperty(ref _primaryDailyAtTime, value);
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
        set => SetProperty(ref _usersDailyAtTime, value);
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

    #endregion

    #region Preset Logic

    private void ApplyPreset(string? presetName)
    {
        if (string.IsNullOrEmpty(presetName) || presetName == "Generic")
        {
            PresetOrigin = "Generic";
            return;
        }

        var preset = PresetRegistry.GetByName(presetName);
        if (preset is null) return;

        var config = PresetRegistry.CreateFromPreset(preset);

        // Apply preset values to VM properties
        BaseUrl = config.Connection.BaseUrl;
        AuthType = config.Connection.AuthType;
        ApiKeyHeader = config.Connection.ApiKeyHeader;
        ApiKeyFormat = config.Connection.ApiKeyFormat;
        HmacAlgorithm = config.Connection.HmacAlgorithm;
        ContentType = config.Connection.ContentType;

        AssetsGetEndpoint = config.Assets.GetEndpoint;
        AssetsPostEndpoint = config.Assets.PostEndpoint;
        AssetsPutEndpoint = config.Assets.PutEndpoint;
        AssetsResponsePath = config.Assets.ResponseItemsPath;
        AssetsCloudIdField = config.Assets.CloudIdField;

        UsersGetEndpoint = config.Users.GetEndpoint;
        UsersPostEndpoint = config.Users.PostEndpoint;
        UsersPutEndpoint = config.Users.PutEndpoint;
        UsersResponsePath = config.Users.ResponseItemsPath;
        UsersCloudIdField = config.Users.CloudIdField;

        PresetOrigin = preset.Name;
        Name = preset.Name;
    }

    /// <summary>
    /// Checks if core structural fields have drifted from the preset template.
    /// If they have, switches label to "Custom". Credential fields don't trigger this.
    /// </summary>
    private void CheckPresetDrift()
    {
        if (PresetOrigin is "Generic" or "Custom")
            return;

        var preset = PresetRegistry.GetByName(PresetOrigin);
        if (preset is null) return;

        bool drifted = BaseUrl != preset.BaseUrl
            || AuthType != preset.AuthType
            || HmacAlgorithm != preset.HmacAlgorithm
            || ContentType != preset.ContentType;

        if (drifted)
            PresetOrigin = "Custom";
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
        CloudConnectionStatus = $"Discovered {attrs.Count} AD user attributes.";
    }

    private async Task<IReadOnlyList<string>> DiscoverCloudFieldsAsync(string category)
    {
        try
        {
            var targetConfig = BuildTargetConfig();
            using var client = new GenericCloudClient(targetConfig);
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
            using var client = new GenericCloudClient(targetConfig);
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

        Config.Users.Enabled = UsersEnabled;
        Config.Users.GetEndpoint = UsersGetEndpoint;
        Config.Users.PostEndpoint = UsersPostEndpoint;
        Config.Users.PutEndpoint = UsersPutEndpoint;
        Config.Users.ResponseItemsPath = UsersResponsePath;
        Config.Users.CloudIdField = UsersCloudIdField;
        Config.Users.AdMatchField = UsersAdMatchField;
        Config.Users.CloudMatchField = UsersCloudMatchField;
        Config.Users.FieldMappings = UserMappings.Select(m => m.ToModel()).ToList();

        Config.Schedule.CoupledSchedule = CoupledSchedule;
        Config.Schedule.PrimarySchedule.Type = PrimaryScheduleType;
        Config.Schedule.PrimarySchedule.Interval = TimeSpan.FromHours(PrimaryIntervalHours);
        Config.Schedule.PrimarySchedule.CronExpression = PrimaryCronExpression;
        Config.Schedule.PrimarySchedule.DailyAtTime = PrimaryDailyAtTime;
        Config.Schedule.UsersSchedule.Type = UsersScheduleType;
        Config.Schedule.UsersSchedule.Interval = TimeSpan.FromHours(UsersIntervalHours);
        Config.Schedule.UsersSchedule.CronExpression = UsersCronExpression;
        Config.Schedule.UsersSchedule.DailyAtTime = UsersDailyAtTime;
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