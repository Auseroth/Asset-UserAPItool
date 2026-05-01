using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.Windows.Input;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Providers;
using LdapCloudSync.Core.Services;
using LdapCloudSync.Core.Interfaces;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// ViewModel for a single configured data source — either an AD domain or a cloud API.
/// Mirrors the structure of CloudTargetViewModel but is read-oriented.
/// The SourceType property drives which detail panel is shown in the view.
/// </summary>
public sealed class CloudSourceViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly SourceFileService _sourceFileService;
    internal CloudSourceConfig Config { get; }

    public CloudSourceViewModel(
        CloudSourceConfig config,
        ConfigService configService,
        SourceFileService? sourceFileService = null)
    {
        Config = config;
        _configService = configService;
        _sourceFileService = sourceFileService ?? new SourceFileService();
        LoadFromConfig();

        // Shared
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);

        // AD-specific
        PreviewComputersCommand = new AsyncRelayCommand(PreviewComputersAsync);
        PreviewUsersCommand = new AsyncRelayCommand(PreviewUsersAsync);
        RefreshOUsCommand = new AsyncRelayCommand(RefreshOUsAsync);
        ConvertCanonicalCommand = new RelayCommand(ConvertCanonical);
        UseAsComputerSearchBaseCommand = new RelayCommand(ApplyAsComputerSearchBase);
        UseAsUserSearchBaseCommand = new RelayCommand(ApplyAsUserSearchBase);
        UseAsUserFilterGroupCommand = new RelayCommand(ApplyConvertedAsGroupFilter);

        // Cloud-specific
        ApplyPresetCommand = new RelayCommand<string>(ApplyPreset);
        DiscoverAssetFieldsCommand = new AsyncRelayCommand(DiscoverAssetFieldsAsync);
        DiscoverUserFieldsCommand = new AsyncRelayCommand(DiscoverUserFieldsAsync);
        TestRetrieveAssetsCommand = new AsyncRelayCommand(() => TestRetrieveAsync("assets"));
        TestRetrieveUsersCommand = new AsyncRelayCommand(() => TestRetrieveAsync("users"));
        RefreshApAccountsCommand = new AsyncRelayCommand(RefreshApAccountsAsync);
        RefreshApModulesCommand = new AsyncRelayCommand(RefreshApModulesAsync);
        RefreshApAssetsCollectionsCommand = new AsyncRelayCommand(() => RefreshApCollectionsAsync("assets"));
        RefreshApUsersCollectionsCommand = new AsyncRelayCommand(() => RefreshApCollectionsAsync("users"));

        SaveAssetsAsFileCommand = new AsyncRelayCommand(SaveAssetsAsFileAsync);
        SaveUsersAsFileCommand = new AsyncRelayCommand(SaveUsersAsFileAsync);


        RetrieveAllAndSaveAssetsCommand = new AsyncRelayCommand(RetrieveAllAndSaveAssetsAsync);
        RetrieveAllAndSaveUsersCommand = new AsyncRelayCommand(RetrieveAllAndSaveUsersAsync);
    }


    #region Identity

    private string _name = "New Source";
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

    private SourceType _sourceType = SourceType.Cloud;
    public SourceType SourceType
    {
        get => _sourceType;
        set
        {
            if (SetProperty(ref _sourceType, value))
            {
                OnPropertyChanged(nameof(IsAdSource));
                OnPropertyChanged(nameof(IsCloudSource));
            }
        }
    }

    public bool IsAdSource => SourceType == SourceType.AD;
    public bool IsCloudSource => SourceType == SourceType.Cloud;

    #endregion

    #region Shared Status

    private string _connectionStatus = string.Empty;
    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    private bool _isConnectionSuccess;
    public bool IsConnectionSuccess
    {
        get => _isConnectionSuccess;
        set => SetProperty(ref _isConnectionSuccess, value);
    }

    #endregion

    #region AD Source Properties

    private string _adDomain = string.Empty;
    public string AdDomain
    {
        get => _adDomain;
        set => SetProperty(ref _adDomain, value);
    }

    private string _adServer = string.Empty;
    public string AdServer
    {
        get => _adServer;
        set => SetProperty(ref _adServer, value);
    }

    private int _adPort = 389;
    public int AdPort
    {
        get => _adPort;
        set => SetProperty(ref _adPort, value);
    }

    private bool _adUseSsl;
    public bool AdUseSsl
    {
        get => _adUseSsl;
        set
        {
            if (SetProperty(ref _adUseSsl, value))
            {
                if (value && AdPort == 389) AdPort = 636;
                else if (!value && AdPort == 636) AdPort = 389;
            }
        }
    }

    private string _adUsername = string.Empty;
    public string AdUsername
    {
        get => _adUsername;
        set => SetProperty(ref _adUsername, value);
    }

    private string _adPassword = string.Empty;
    public string AdPassword
    {
        get => _adPassword;
        set => SetProperty(ref _adPassword, value);
    }

    private string _computerSearchBase = string.Empty;
    public string ComputerSearchBase
    {
        get => _computerSearchBase;
        set => SetProperty(ref _computerSearchBase, value);
    }

    private string _userSearchBase = string.Empty;
    public string UserSearchBase
    {
        get => _userSearchBase;
        set => SetProperty(ref _userSearchBase, value);
    }

    private string _additionalUserSearchBases = string.Empty;
    public string AdditionalUserSearchBases
    {
        get => _additionalUserSearchBases;
        set => SetProperty(ref _additionalUserSearchBases, value);
    }

    private string _computerFilter = "(objectClass=computer)";
    public string ComputerFilter
    {
        get => _computerFilter;
        set => SetProperty(ref _computerFilter, value);
    }

    private string _userFilter = "(&(objectClass=user)(objectCategory=person))";
    public string UserFilter
    {
        get => _userFilter;
        set => SetProperty(ref _userFilter, value);
    }

    // Canonical converter (AD panel)
    private string _canonicalInput = string.Empty;
    public string CanonicalInput
    {
        get => _canonicalInput;
        set { if (SetProperty(ref _canonicalInput, value)) ConvertCanonical(); }
    }

    private string _convertedDn = string.Empty;
    public string ConvertedDn
    {
        get => _convertedDn;
        set => SetProperty(ref _convertedDn, value);
    }

    private string _computerPreviewStatus = string.Empty;
    public string ComputerPreviewStatus
    {
        get => _computerPreviewStatus;
        set => SetProperty(ref _computerPreviewStatus, value);
    }

    private string _userPreviewStatus = string.Empty;
    public string UserPreviewStatus
    {
        get => _userPreviewStatus;
        set => SetProperty(ref _userPreviewStatus, value);
    }

    public ObservableCollection<string> ComputerPreviewItems { get; } = [];
    public ObservableCollection<string> UserPreviewItems { get; } = [];

    /// <summary>Discovered OUs — shared with CloudTargetsViewModel for per-target OU selection.</summary>
    public ObservableCollection<string> DiscoveredComputerOUs { get; } = [];
    public ObservableCollection<string> DiscoveredUserOUs { get; } = [];

    /// <summary>Raised when OUs are discovered so targets can rebuild their OU selectors.</summary>
    public event Action? OUsDiscovered;

    #endregion

    #region Cloud Source Properties

    private string _providerType = "Generic (Standard REST)";
    public string ProviderType
    {
        get => _providerType;
        set
        {
            if (SetProperty(ref _providerType, value))
            {
                OnPropertyChanged(nameof(ShowCategorySelector));
                OnPropertyChanged(nameof(ShowApSettings));
                OnPropertyChanged(nameof(HideGenericEndpoints));
            }
        }
    }

    public bool ShowCategorySelector => ProviderType == "Reftab";
    public bool ShowApSettings => ProviderType == "AssetPanda";
    public bool HideGenericEndpoints => ProviderType == "AssetPanda";

    // Connection
    private string _baseUrl = string.Empty;
    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); }

    private AuthType _authType = AuthType.ApiKey;
    public AuthType AuthType { get => _authType; set => SetProperty(ref _authType, value); }

    private string _apiKey = string.Empty;
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }

    private string _apiSecret = string.Empty;
    public string ApiSecret { get => _apiSecret; set => SetProperty(ref _apiSecret, value); }

    private string _apiKeyHeader = "Authorization";
    public string ApiKeyHeader { get => _apiKeyHeader; set => SetProperty(ref _apiKeyHeader, value); }

    private string _apiKeyFormat = "Bearer {key}";
    public string ApiKeyFormat { get => _apiKeyFormat; set => SetProperty(ref _apiKeyFormat, value); }

    private string _basicUsername = string.Empty;
    public string BasicUsername { get => _basicUsername; set => SetProperty(ref _basicUsername, value); }

    private string _basicPassword = string.Empty;
    public string BasicPassword { get => _basicPassword; set => SetProperty(ref _basicPassword, value); }

    private string _hmacAlgorithm = "HMACSHA256";
    public string HmacAlgorithm { get => _hmacAlgorithm; set => SetProperty(ref _hmacAlgorithm, value); }

    private string _contentType = "application/json";
    public string ContentType { get => _contentType; set => SetProperty(ref _contentType, value); }

    // Assets read config
    private bool _assetsEnabled;
    public bool AssetsEnabled { get => _assetsEnabled; set => SetProperty(ref _assetsEnabled, value); }

    private string _assetsGetEndpoint = string.Empty;
    public string AssetsGetEndpoint { get => _assetsGetEndpoint; set => SetProperty(ref _assetsGetEndpoint, value); }

    private string _assetsResponsePath = "$";
    public string AssetsResponsePath { get => _assetsResponsePath; set => SetProperty(ref _assetsResponsePath, value); }

    private string _assetsFilter = string.Empty;
    public string AssetsFilter { get => _assetsFilter; set => SetProperty(ref _assetsFilter, value); }

    private string _assetsSourceIdField = "id";
    public string AssetsSourceIdField { get => _assetsSourceIdField; set => SetProperty(ref _assetsSourceIdField, value); }

    private int _assetsSourceCategoryId;
    public int AssetsSourceCategoryId { get => _assetsSourceCategoryId; set => SetProperty(ref _assetsSourceCategoryId, value); }

    // Users read config
    private bool _usersEnabled;
    public bool UsersEnabled { get => _usersEnabled; set => SetProperty(ref _usersEnabled, value); }

    private string _usersGetEndpoint = string.Empty;
    public string UsersGetEndpoint { get => _usersGetEndpoint; set => SetProperty(ref _usersGetEndpoint, value); }

    private string _usersResponsePath = "$";
    public string UsersResponsePath { get => _usersResponsePath; set => SetProperty(ref _usersResponsePath, value); }

    private string _usersFilter = string.Empty;
    public string UsersFilter { get => _usersFilter; set => SetProperty(ref _usersFilter, value); }

    private string _usersSourceIdField = "id";
    public string UsersSourceIdField { get => _usersSourceIdField; set => SetProperty(ref _usersSourceIdField, value); }

    // Discovered source field names (for the target field mapping dropdowns)
    public ObservableCollection<string> DiscoveredAssetSourceFields { get; } = [];
    public ObservableCollection<string> DiscoveredUserSourceFields { get; } = [];

    // Last retrieved raw JSON — used by the JSON editor window
    private string _lastRetrievedAssetsJson = string.Empty;
    public string LastRetrievedAssetsJson
    {
        get => _lastRetrievedAssetsJson;
        set => SetProperty(ref _lastRetrievedAssetsJson, value);
    }

    private string _lastRetrievedUsersJson = string.Empty;
    public string LastRetrievedUsersJson
    {
        get => _lastRetrievedUsersJson;
        set => SetProperty(ref _lastRetrievedUsersJson, value);
    }

    // Asset Panda
    public class ApIdNameItem { public string Id { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; }

    public ObservableCollection<ApIdNameItem> ApAccounts { get; } = [];
    public ObservableCollection<ApIdNameItem> ApModules { get; } = [];
    public ObservableCollection<ApIdNameItem> ApAssetsCollections { get; } = [];
    public ObservableCollection<ApIdNameItem> ApUsersCollections { get; } = [];

    private string _apAccountId = string.Empty;
    public string ApAccountId { get => _apAccountId; set => SetProperty(ref _apAccountId, value); }

    private string _apModuleId = string.Empty;
    public string ApModuleId { get => _apModuleId; set => SetProperty(ref _apModuleId, value); }

    private string _apAssetsCollectionId = string.Empty;
    public string ApAssetsCollectionId { get => _apAssetsCollectionId; set => SetProperty(ref _apAssetsCollectionId, value); }

    private string _apUsersCollectionId = string.Empty;
    public string ApUsersCollectionId { get => _apUsersCollectionId; set => SetProperty(ref _apUsersCollectionId, value); }

    #endregion

    #region Commands

    // Shared
    public ICommand TestConnectionCommand { get; }

    // AD-specific
    public ICommand PreviewComputersCommand { get; }
    public ICommand PreviewUsersCommand { get; }
    public ICommand RefreshOUsCommand { get; }
    public ICommand ConvertCanonicalCommand { get; }
    public ICommand UseAsComputerSearchBaseCommand { get; }
    public ICommand UseAsUserSearchBaseCommand { get; }
    public ICommand UseAsUserFilterGroupCommand { get; }

    // Cloud-specific
    public ICommand ApplyPresetCommand { get; }
    public ICommand DiscoverAssetFieldsCommand { get; }
    public ICommand DiscoverUserFieldsCommand { get; }
    public ICommand TestRetrieveAssetsCommand { get; }
    public ICommand TestRetrieveUsersCommand { get; }
    public ICommand RefreshApAccountsCommand { get; }
    public ICommand RefreshApModulesCommand { get; }
    public ICommand RefreshApAssetsCollectionsCommand { get; }
    public ICommand RefreshApUsersCollectionsCommand { get; }

    public ICommand SaveAssetsAsFileCommand { get; }
    public ICommand SaveUsersAsFileCommand { get; }

    public ICommand RetrieveAllAndSaveAssetsCommand { get; }
    public ICommand RetrieveAllAndSaveUsersCommand  { get; }

    #endregion

    #region Shared Commands

    private async Task TestConnectionAsync()
    {
        ConnectionStatus = "Testing connection...";
        IsConnectionSuccess = false;

        try
        {
            if (IsAdSource)
            {
                ApplyToConfig();
                using var provider = new ActiveDirectoryProvider(Config.Ad);
                var (success, message) = await provider.TestConnectionAsync();
                ConnectionStatus = message;
                IsConnectionSuccess = success;

                if (success)
                {
                    await DiscoverOUsAsync();
                    OUsDiscovered?.Invoke();
                    ConnectionStatus = $"{message} | {DiscoveredComputerOUs.Count} computer OUs, {DiscoveredUserOUs.Count} user OUs discovered.";
                }
            }
            else
            {
                ApplyToConfig();
                using var client = CloudSourceFactory.CreateClient(Config);
                var (success, message) = await client.TestConnectionAsync();
                ConnectionStatus = message;
                IsConnectionSuccess = success;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    #endregion

    #region AD Commands

    private async Task RefreshOUsAsync()
    {
        ApplyToConfig();
        await DiscoverOUsAsync();
        OUsDiscovered?.Invoke();
    }

    private async Task PreviewComputersAsync()
    {
        ComputerPreviewItems.Clear();
        ComputerPreviewStatus = "Querying...";
        try
        {
            ApplyToConfig();
            using var provider = new ActiveDirectoryProvider(Config.Ad);
            var records = await provider.QueryAsync(
                DirectoryObjectType.Computer,
                ["cn", "operatingSystem", "description"],
                maxResults: 25);

            foreach (var r in records)
            {
                var cn = r.GetValueOrDefault("cn", "???");
                var os = r.GetValueOrDefault("operatingSystem", "");
                var desc = r.GetValueOrDefault("description", "");
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(os)) parts.Add(os);
                if (!string.IsNullOrEmpty(desc)) parts.Add($"Desc: {desc}");
                ComputerPreviewItems.Add(parts.Count > 0 ? $"{cn}  ({string.Join(" | ", parts)})" : cn);
            }

            ComputerPreviewStatus = $"Showing {records.Count} (max 25 preview)";
        }
        catch (Exception ex) { ComputerPreviewStatus = $"Error: {ex.Message}"; }
    }

    private async Task PreviewUsersAsync()
    {
        UserPreviewItems.Clear();
        UserPreviewStatus = "Querying...";
        try
        {
            ApplyToConfig();
            using var provider = new ActiveDirectoryProvider(Config.Ad);
            var records = await provider.QueryAsync(
                DirectoryObjectType.User,
                ["displayName", "sAMAccountName", "mail"],
                maxResults: 25);

            foreach (var r in records)
            {
                var name = r.GetValueOrDefault("displayName", "");
                var sam = r.GetValueOrDefault("sAMAccountName", "???");
                var mail = r.GetValueOrDefault("mail", "");
                var label = !string.IsNullOrEmpty(name) ? $"{name} ({sam})" : sam;
                UserPreviewItems.Add(!string.IsNullOrEmpty(mail) ? $"{label}  —  {mail}" : label);
            }

            UserPreviewStatus = $"Showing {records.Count} (max 25 preview)";
        }
        catch (Exception ex) { UserPreviewStatus = $"Error: {ex.Message}"; }
    }

    public async Task DiscoverOUsAsync()
    {
        var adConfig = Config.Ad;
        DiscoveredComputerOUs.Clear();
        DiscoveredUserOUs.Clear();

        if (!string.IsNullOrWhiteSpace(adConfig.ComputerSearchBase))
        {
            DiscoveredComputerOUs.Add(adConfig.ComputerSearchBase);
            var childOUs = await DiscoverChildOUsAsync(adConfig, adConfig.ComputerSearchBase);
            foreach (var ou in childOUs) DiscoveredComputerOUs.Add(ou);
        }

        var groupDns = ExtractMemberOfDns(adConfig.UserFilter);
        if (groupDns.Count > 0)
        {
            foreach (var dn in groupDns) DiscoveredUserOUs.Add(dn);
        }
        else if (!string.IsNullOrWhiteSpace(adConfig.UserSearchBase))
        {
            DiscoveredUserOUs.Add(adConfig.UserSearchBase);
            var childOUs = await DiscoverChildOUsAsync(adConfig, adConfig.UserSearchBase);
            foreach (var ou in childOUs) DiscoveredUserOUs.Add(ou);
        }

        foreach (var additional in adConfig.AdditionalUserSearchBases)
        {
            if (!string.IsNullOrWhiteSpace(additional) &&
                !DiscoveredUserOUs.Contains(additional, StringComparer.OrdinalIgnoreCase))
                DiscoveredUserOUs.Add(additional);
        }
    }

    private static Task<List<string>> DiscoverChildOUsAsync(AdConnectionConfig config, string searchBase)
    {
        return Task.Run(() =>
        {
            var results = new List<string>();
            try
            {
                var protocol = config.UseSsl ? "LDAPS" : "LDAP";
                var host = !string.IsNullOrEmpty(config.Server) ? config.Server : config.Domain;
                var path = $"{protocol}://{host}:{config.Port}/{searchBase}";
                var password = ConfigService.DecryptPassword(config.EncryptedPassword);

                using var entry = new DirectoryEntry(path, config.Username, password,
                    config.UseSsl ? AuthenticationTypes.SecureSocketsLayer : AuthenticationTypes.Secure);
                using var searcher = new DirectorySearcher(entry)
                {
                    Filter = "(objectClass=organizationalUnit)",
                    SearchScope = SearchScope.Subtree,
                    PageSize = 1000
                };
                searcher.PropertiesToLoad.Add("distinguishedName");

                using var sr = searcher.FindAll();
                foreach (SearchResult r in sr)
                {
                    var dn = r.Properties["distinguishedName"][0]?.ToString();
                    if (!string.IsNullOrEmpty(dn) &&
                        !string.Equals(dn, searchBase, StringComparison.OrdinalIgnoreCase))
                        results.Add(dn);
                }
                results.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { /* silently ignore — OUs just won't appear */ }
            return results;
        });
    }

    private static List<string> ExtractMemberOfDns(string filter)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(filter)) return results;
        const string marker = "memberOf=";
        var index = 0;
        while (index < filter.Length)
        {
            var start = filter.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            start += marker.Length;
            var end = filter.IndexOf(')', start);
            if (end < 0) break;
            var dn = filter[start..end].Trim();
            if (!string.IsNullOrEmpty(dn)) results.Add(dn);
            index = end + 1;
        }
        return results;
    }

    // Canonical converter helpers
    private string[] GetCanonicalLines() =>
        string.IsNullOrWhiteSpace(CanonicalInput) ? [] :
        CanonicalInput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

    private static string ConvertSingleCanonicalToDn(string canonical, bool lastPartIsCn)
    {
        var parts = canonical.Trim().TrimEnd('/').Split('/');
        if (parts.Length < 1) return string.Empty;
        var dcComponents = parts[0].Split('.').Select(d => $"DC={d}");
        var pathParts = parts.Skip(1).ToArray();
        if (pathParts.Length == 0) return string.Join(",", dcComponents);
        var dnParts = new List<string>();
        for (int i = pathParts.Length - 1; i >= 0; i--)
            dnParts.Add($"{(i == pathParts.Length - 1 && lastPartIsCn ? "CN" : "OU")}={pathParts[i]}");
        dnParts.AddRange(dcComponents);
        return string.Join(",", dnParts);
    }

    private void ConvertCanonical()
    {
        var lines = GetCanonicalLines();
        if (lines.Length == 0) { ConvertedDn = string.Empty; return; }
        try { ConvertedDn = string.Join(Environment.NewLine, lines.Select(l => ConvertSingleCanonicalToDn(l, true))); }
        catch { ConvertedDn = "Invalid format. Expected: domain.com/OU1/OU2/Name"; }
    }

    private void ApplyAsComputerSearchBase()
    {
        var lines = GetCanonicalLines();
        if (lines.Length > 0) ComputerSearchBase = ConvertSingleCanonicalToDn(lines[0], false);
    }

    private void ApplyAsUserSearchBase()
    {
        var ous = GetCanonicalLines()
            .Select(l => ConvertSingleCanonicalToDn(l, false))
            .Where(dn => !string.IsNullOrEmpty(dn)).ToList();
        if (ous.Count == 0) return;
        UserSearchBase = ous[0];
        AdditionalUserSearchBases = ous.Count > 1 ? string.Join(Environment.NewLine, ous.Skip(1)) : string.Empty;
        UserFilter = "(&(objectClass=user)(objectCategory=person))";
    }

    private void ApplyConvertedAsGroupFilter()
    {
        var groups = GetCanonicalLines()
            .Select(l => ConvertSingleCanonicalToDn(l, true))
            .Where(dn => !string.IsNullOrEmpty(dn)).ToList();
        if (groups.Count == 0) return;
        var dcIndex = groups[0].IndexOf("DC=", StringComparison.OrdinalIgnoreCase);
        UserSearchBase = dcIndex >= 0 ? groups[0][dcIndex..] : groups[0];
        AdditionalUserSearchBases = string.Empty;
        UserFilter = groups.Count == 1
            ? $"(&(objectClass=user)(objectCategory=person)(memberOf={groups[0]}))"
            : $"(&(objectClass=user)(objectCategory=person)(|{string.Join("", groups.Select(g => $"(memberOf={g})"))}))";
    }

    #endregion

    #region Cloud Commands

    private async Task DiscoverAssetFieldsAsync()
    {
        ConnectionStatus = "Discovering asset fields...";
        try
        {
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);
            var (fields, _) = await client.DiscoverFieldsAsync("assets");
            DiscoveredAssetSourceFields.Clear();
            foreach (var f in fields) DiscoveredAssetSourceFields.Add(f);
            ConnectionStatus = $"Discovered {fields.Count} asset fields.";
        }
        catch (Exception ex) { ConnectionStatus = $"Discovery failed: {ex.Message}"; }
    }

    private async Task DiscoverUserFieldsAsync()
    {
        ConnectionStatus = "Discovering user fields...";
        try
        {
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);
            var (fields, _) = await client.DiscoverFieldsAsync("users");
            DiscoveredUserSourceFields.Clear();
            foreach (var f in fields) DiscoveredUserSourceFields.Add(f);
            ConnectionStatus = $"Discovered {fields.Count} user fields.";
        }
        catch (Exception ex) { ConnectionStatus = $"Discovery failed: {ex.Message}"; }
    }

    /// <summary>
    /// Fetches a small sample (10 records) from the source and stores the raw JSON.
    /// The JSON editor window (Phase 5) opens from this result.
    /// </summary>
    private async Task TestRetrieveAsync(string category)
    {
        ConnectionStatus = $"Retrieving {category} sample (10 records)...";
        try
        {
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);

            var filter = category == "assets" ? AssetsFilter : UsersFilter;
            var (records, rawJson) = await client.GetRecordsAsync(
                category,
                string.IsNullOrWhiteSpace(filter) ? null : filter,
                maxRecords: 10);

            if (category == "assets") LastRetrievedAssetsJson = rawJson;
            else LastRetrievedUsersJson = rawJson;

            ConnectionStatus = $"Retrieved {records.Count} {category} record(s). Use 'Retrieve & Edit JSON' to open the editor.";
        }
        catch (Exception ex) { ConnectionStatus = $"Retrieve failed: {ex.Message}"; }
    }

    private async Task RefreshApAccountsAsync()
    {
        ConnectionStatus = "Fetching Asset Panda accounts...";
        try
        {
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);
            if (client is AssetPandaClient apClient)
            {
                var accounts = await apClient.GetAccountsAsync();
                ApAccounts.Clear();
                foreach (var (id, name) in accounts) ApAccounts.Add(new ApIdNameItem { Id = id, Name = name });
                ConnectionStatus = $"Loaded {accounts.Count} accounts.";
            }
        }
        catch (Exception ex) { ConnectionStatus = $"Account fetch failed: {ex.Message}"; }
    }

    private async Task RefreshApModulesAsync()
    {
        if (string.IsNullOrEmpty(ApAccountId)) { ConnectionStatus = "Select an Account first."; return; }
        ConnectionStatus = "Fetching Asset Panda modules...";
        try
        {
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);
            if (client is AssetPandaClient apClient)
            {
                var modules = await apClient.GetModulesAsync(ApAccountId);
                ApModules.Clear();
                foreach (var (id, name) in modules) ApModules.Add(new ApIdNameItem { Id = id, Name = name });
                ConnectionStatus = $"Loaded {modules.Count} modules.";
            }
        }
        catch (Exception ex) { ConnectionStatus = $"Module fetch failed: {ex.Message}"; }
    }

    private async Task RefreshApCollectionsAsync(string target)
    {
        if (string.IsNullOrEmpty(ApAccountId) || string.IsNullOrEmpty(ApModuleId))
        {
            ConnectionStatus = "Select Account and Module first."; return;
        }
        ConnectionStatus = "Fetching Asset Panda collections...";
        try
        {
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);
            if (client is AssetPandaClient apClient)
            {
                var cols = await apClient.GetCollectionsAsync(ApAccountId, ApModuleId);
                var list = target == "assets" ? ApAssetsCollections : ApUsersCollections;
                list.Clear();
                foreach (var (id, name) in cols) list.Add(new ApIdNameItem { Id = id, Name = name });
                ConnectionStatus = $"Loaded {cols.Count} collections.";
            }
        }
        catch (Exception ex) { ConnectionStatus = $"Collection fetch failed: {ex.Message}"; }
    }

    private void ApplyPreset(string? presetName)
    {
        if (string.IsNullOrEmpty(presetName) || presetName == "Blank (REST)")
        {
            BaseUrl = string.Empty; ApiKey = string.Empty; ApiSecret = string.Empty;
            ApiKeyHeader = "Authorization"; ApiKeyFormat = "Bearer {key}";
            AssetsGetEndpoint = string.Empty; AssetsResponsePath = "$";
            UsersGetEndpoint = string.Empty; UsersResponsePath = "$";
            ProviderType = "Generic (Standard REST)";
            Name = $"Source {DateTime.Now:HHmmss}";
            return;
        }

        var preset = LdapCloudSync.Core.Presets.PresetRegistry.GetByName(presetName);
        if (preset is null) return;

        var config = LdapCloudSync.Core.Presets.PresetRegistry.CreateFromPreset(preset);
        BaseUrl = config.Connection.BaseUrl;
        AuthType = config.Connection.AuthType;
        ApiKeyHeader = config.Connection.ApiKeyHeader;
        ApiKeyFormat = config.Connection.ApiKeyFormat;
        HmacAlgorithm = config.Connection.HmacAlgorithm;
        ContentType = config.Connection.ContentType;
        AssetsGetEndpoint = config.Assets.GetEndpoint;
        AssetsResponsePath = config.Assets.ResponseItemsPath;
        UsersGetEndpoint = config.Users.GetEndpoint;
        UsersResponsePath = config.Users.ResponseItemsPath;
        ProviderType = preset.ProviderType;
        Name = preset.Name;
        AssetsEnabled = true;
        UsersEnabled = true;
    }

    #endregion

    #region Config Mapping

    private void LoadFromConfig()
    {
        Name = Config.Name;
        Enabled = Config.Enabled;
        SourceType = Config.SourceType;

        if (Config.SourceType == SourceType.AD)
        {
            var ad = Config.Ad;
            AdDomain = ad.Domain;
            AdServer = ad.Server;
            AdPort = ad.Port;
            AdUseSsl = ad.UseSsl;
            AdUsername = ad.Username;
            ComputerSearchBase = ad.ComputerSearchBase;
            UserSearchBase = ad.UserSearchBase;
            AdditionalUserSearchBases = string.Join(Environment.NewLine, ad.AdditionalUserSearchBases);
            ComputerFilter = ad.ComputerFilter;
            UserFilter = ad.UserFilter;

            if (!string.IsNullOrEmpty(ad.EncryptedPassword))
            {
                try { AdPassword = ConfigService.DecryptPassword(ad.EncryptedPassword); }
                catch { AdPassword = string.Empty; }
            }
        }
        else
        {
            ProviderType = Config.ProviderType;
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

            AssetsEnabled = Config.Assets.Enabled;
            AssetsGetEndpoint = Config.Assets.GetEndpoint;
            AssetsResponsePath = Config.Assets.ResponseItemsPath;
            AssetsFilter = Config.Assets.Filter;
            AssetsSourceIdField = Config.Assets.SourceIdField;
            AssetsSourceCategoryId = Config.Assets.SourceCategoryId;

            UsersEnabled = Config.Users.Enabled;
            UsersGetEndpoint = Config.Users.GetEndpoint;
            UsersResponsePath = Config.Users.ResponseItemsPath;
            UsersFilter = Config.Users.Filter;
            UsersSourceIdField = Config.Users.SourceIdField;

            ApAccountId = Config.ApAccountId;
            ApModuleId = Config.ApModuleId;
            ApAssetsCollectionId = Config.ApAssetsCollectionId;
            ApUsersCollectionId = Config.ApUsersCollectionId;
        }
    }

    public void ApplyToConfig()
    {
        Config.Name = Name;
        Config.Enabled = Enabled;
        Config.SourceType = SourceType;

        if (SourceType == SourceType.AD)
        {
            Config.Ad.Domain = AdDomain;
            Config.Ad.Server = AdServer;
            Config.Ad.Port = AdPort;
            Config.Ad.UseSsl = AdUseSsl;
            Config.Ad.Username = AdUsername;
            Config.Ad.EncryptedPassword = ConfigService.EncryptPassword(AdPassword);
            Config.Ad.ComputerSearchBase = ComputerSearchBase;
            Config.Ad.UserSearchBase = UserSearchBase;
            Config.Ad.AdditionalUserSearchBases = AdditionalUserSearchBases
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            Config.Ad.ComputerFilter = ComputerFilter;
            Config.Ad.UserFilter = UserFilter;
        }
        else
        {
            Config.ProviderType = ProviderType;
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
            Config.Assets.ResponseItemsPath = AssetsResponsePath;
            Config.Assets.Filter = AssetsFilter;
            Config.Assets.SourceIdField = AssetsSourceIdField;
            Config.Assets.SourceCategoryId = AssetsSourceCategoryId;

            Config.Users.Enabled = UsersEnabled;
            Config.Users.GetEndpoint = UsersGetEndpoint;
            Config.Users.ResponseItemsPath = UsersResponsePath;
            Config.Users.Filter = UsersFilter;
            Config.Users.SourceIdField = UsersSourceIdField;

            Config.ApAccountId = ApAccountId;
            Config.ApModuleId = ApModuleId;
            Config.ApAssetsCollectionId = ApAssetsCollectionId;
            Config.ApUsersCollectionId = ApUsersCollectionId;
        }
    }


    #endregion

    #region Save file commands

    private string _saveFileName = string.Empty;
    public string SaveFileName
    {
        get => _saveFileName;
        set => SetProperty(ref _saveFileName, value);
    }

    private async Task SaveAssetsAsFileAsync()
    {
        if (string.IsNullOrWhiteSpace(LastRetrievedAssetsJson))
        {
            ConnectionStatus = "No asset data retrieved yet. Click 'Test Retrieve' first.";
            return;
        }
        var name = string.IsNullOrWhiteSpace(SaveFileName) ? $"{Name}-assets" : SaveFileName.Trim();
        await _sourceFileService.SaveRawJsonAsync(name, LastRetrievedAssetsJson);
        ConnectionStatus = $"Saved as file source: {name}";
    }

    private async Task SaveUsersAsFileAsync()
    {
        if (string.IsNullOrWhiteSpace(LastRetrievedUsersJson))
        {
            ConnectionStatus = "No user data retrieved yet. Click 'Test Retrieve' first.";
            return;
        }
        var name = string.IsNullOrWhiteSpace(SaveFileName) ? $"{Name}-users" : SaveFileName.Trim();
        await _sourceFileService.SaveRawJsonAsync(name, LastRetrievedUsersJson);
        ConnectionStatus = $"Saved as file source: {name}";
    }

    private async Task RetrieveAllAndSaveAssetsAsync()
    {
        try
        {
            ConnectionStatus = "Retrieving ALL asset records (no limit)...";
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);
            var (records, rawJson) = await client.GetRecordsAsync(
                "assets",
                string.IsNullOrWhiteSpace(AssetsFilter) ? null : AssetsFilter,
                maxRecords: 0);    // 0 = no limit

            if (records.Count == 0)
            {
                ConnectionStatus = "No asset records returned.";
                return;
            }

            var name = string.IsNullOrWhiteSpace(SaveFileName)
                ? $"{Name}-assets"
                : SaveFileName.Trim();

            await _sourceFileService.SaveRawJsonAsync(name, rawJson);
            ConnectionStatus = $"Saved {records.Count} asset records as file source: {name}";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Retrieve All failed: {ex.Message}";
        }
    }

    private async Task RetrieveAllAndSaveUsersAsync()
    {
        try
        {
            ConnectionStatus = "Retrieving ALL user records (no limit)...";
            ApplyToConfig();
            using var client = CloudSourceFactory.CreateClient(Config);
            var (records, rawJson) = await client.GetRecordsAsync(
                "users",
                string.IsNullOrWhiteSpace(UsersFilter) ? null : UsersFilter,
                maxRecords: 0);

            if (records.Count == 0)
            {
                ConnectionStatus = "No user records returned.";
                return;
            }

            var name = string.IsNullOrWhiteSpace(SaveFileName)
                ? $"{Name}-users"
                : SaveFileName.Trim();

            await _sourceFileService.SaveRawJsonAsync(name, rawJson);
            ConnectionStatus = $"Saved {records.Count} user records as file source: {name}";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Retrieve All failed: {ex.Message}";
        }
    }
    #endregion
}