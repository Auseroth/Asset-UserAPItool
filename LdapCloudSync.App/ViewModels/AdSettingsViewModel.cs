using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.Windows.Input;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Providers;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

/// <summary>
/// Legacy AD settings viewmodel — retained for backward compatibility.
/// New code should use CloudSourceViewModel with SourceType.AD instead.
/// LoadFromConfig/ApplyToConfig now operate on the first configured AD source.
/// </summary>
public sealed class AdSettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;

    public AdSettingsViewModel(ConfigService configService)
    {
        _configService = configService;
        LoadFromConfig();

        TestConnectionCommand          = new AsyncRelayCommand(TestConnectionAsync);
        PreviewComputersCommand        = new AsyncRelayCommand(PreviewComputersAsync);
        PreviewUsersCommand            = new AsyncRelayCommand(PreviewUsersAsync);
        ConvertCanonicalCommand        = new RelayCommand(ConvertCanonical);
        UseAsComputerSearchBaseCommand = new RelayCommand(ApplyAsComputerSearchBase);
        UseAsUserSearchBaseCommand     = new RelayCommand(ApplyAsUserSearchBase);
        UseAsUserFilterGroupCommand    = new RelayCommand(ApplyConvertedAsGroupFilter);
        RefreshOUsCommand              = new AsyncRelayCommand(RefreshOUsAsync);
    }

    // -- Helpers to resolve the first AD source config ---------------------

    private AdConnectionConfig GetFirstAdConfig()
        => _configService.Current.Sources
               .FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad
           ?? new AdConnectionConfig();

    private AdConnectionConfig? TryGetFirstAdConfig()
        => _configService.Current.Sources
               .FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad;

    // Fields
    private string _domain = string.Empty;
    public string Domain { get => _domain; set => SetProperty(ref _domain, value); }

    private string _server = string.Empty;
    public string Server { get => _server; set => SetProperty(ref _server, value); }

    private int _port = 389;
    public int Port { get => _port; set => SetProperty(ref _port, value); }

    private bool _useSsl;
    public bool UseSsl
    {
        get => _useSsl;
        set
        {
            if (SetProperty(ref _useSsl, value))
            {
                if (value && Port == 389) Port = 636;
                else if (!value && Port == 636) Port = 389;
            }
        }
    }

    private string _username = string.Empty;
    public string Username { get => _username; set => SetProperty(ref _username, value); }

    private string _password = string.Empty;
    public string Password { get => _password; set => SetProperty(ref _password, value); }

    private string _computerSearchBase = string.Empty;
    public string ComputerSearchBase { get => _computerSearchBase; set => SetProperty(ref _computerSearchBase, value); }

    private string _userSearchBase = string.Empty;
    public string UserSearchBase { get => _userSearchBase; set => SetProperty(ref _userSearchBase, value); }

    private string _additionalUserSearchBases = string.Empty;
    public string AdditionalUserSearchBases { get => _additionalUserSearchBases; set => SetProperty(ref _additionalUserSearchBases, value); }

    private string _computerFilter = "(objectClass=computer)";
    public string ComputerFilter { get => _computerFilter; set => SetProperty(ref _computerFilter, value); }

    private string _userFilter = "(&(objectClass=user)(objectCategory=person))";
    public string UserFilter { get => _userFilter; set => SetProperty(ref _userFilter, value); }

    private string _connectionStatus = string.Empty;
    public string ConnectionStatus { get => _connectionStatus; set => SetProperty(ref _connectionStatus, value); }

    private bool _isConnectionSuccess;
    public bool IsConnectionSuccess { get => _isConnectionSuccess; set => SetProperty(ref _isConnectionSuccess, value); }

    public ObservableCollection<string> ComputerPreviewItems { get; } = [];
    public ObservableCollection<string> UserPreviewItems     { get; } = [];

    private string _computerPreviewStatus = string.Empty;
    public string ComputerPreviewStatus { get => _computerPreviewStatus; set => SetProperty(ref _computerPreviewStatus, value); }

    private string _userPreviewStatus = string.Empty;
    public string UserPreviewStatus { get => _userPreviewStatus; set => SetProperty(ref _userPreviewStatus, value); }

    private string _canonicalInput = string.Empty;
    public string CanonicalInput
    {
        get => _canonicalInput;
        set { if (SetProperty(ref _canonicalInput, value)) ConvertCanonical(); }
    }

    private string _convertedDn = string.Empty;
    public string ConvertedDn { get => _convertedDn; set => SetProperty(ref _convertedDn, value); }

    public ICommand TestConnectionCommand          { get; }
    public ICommand PreviewComputersCommand        { get; }
    public ICommand PreviewUsersCommand            { get; }
    public ICommand ConvertCanonicalCommand        { get; }
    public ICommand UseAsComputerSearchBaseCommand { get; }
    public ICommand UseAsUserSearchBaseCommand     { get; }
    public ICommand UseAsUserFilterGroupCommand    { get; }
    public ICommand RefreshOUsCommand              { get; }

    public ObservableCollection<string> DiscoveredComputerOUs { get; } = [];
    public ObservableCollection<string> DiscoveredUserOUs     { get; } = [];
    public event Action? OUsDiscovered;

    public async Task DiscoverOUsAsync()
    {
        var config = BuildAdConfig();
        DiscoveredComputerOUs.Clear();
        DiscoveredUserOUs.Clear();

        if (!string.IsNullOrWhiteSpace(config.ComputerSearchBase))
        {
            DiscoveredComputerOUs.Add(config.ComputerSearchBase);
            var childOUs = await DiscoverChildOUsAsync(config, config.ComputerSearchBase);
            foreach (var ou in childOUs) DiscoveredComputerOUs.Add(ou);
        }

        var groupDns = ExtractMemberOfDns(config.UserFilter);
        if (groupDns.Count > 0)
        {
            foreach (var dn in groupDns) DiscoveredUserOUs.Add(dn);
        }
        else if (!string.IsNullOrWhiteSpace(config.UserSearchBase))
        {
            DiscoveredUserOUs.Add(config.UserSearchBase);
            var childOUs = await DiscoverChildOUsAsync(config, config.UserSearchBase);
            foreach (var ou in childOUs) DiscoveredUserOUs.Add(ou);
        }

        foreach (var additional in config.AdditionalUserSearchBases)
            if (!string.IsNullOrWhiteSpace(additional) && !DiscoveredUserOUs.Contains(additional, StringComparer.OrdinalIgnoreCase))
                DiscoveredUserOUs.Add(additional);
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

    private static Task<List<string>> DiscoverChildOUsAsync(AdConnectionConfig config, string searchBase)
    {
        return Task.Run(() =>
        {
            var results = new List<string>();
            try
            {
                var protocol = config.UseSsl ? "LDAPS" : "LDAP";
                var host     = !string.IsNullOrEmpty(config.Server) ? config.Server : config.Domain;
                var path     = $"{protocol}://{host}:{config.Port}/{searchBase}";
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
                    if (!string.IsNullOrEmpty(dn) && !string.Equals(dn, searchBase, StringComparison.OrdinalIgnoreCase))
                        results.Add(dn);
                }
                results.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return results;
        });
    }

    private async Task TestConnectionAsync()
    {
        ConnectionStatus = "Testing connection...";
        IsConnectionSuccess = false;
        var testConfig = BuildAdConfig();
        using var provider = new ActiveDirectoryProvider(testConfig);
        var (success, message) = await provider.TestConnectionAsync();
        ConnectionStatus = message;
        IsConnectionSuccess = success;
        if (success) { await DiscoverOUsAsync(); OUsDiscovered?.Invoke(); }
    }

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
            var config = BuildAdConfig();
            using var provider = new ActiveDirectoryProvider(config);
            var records = await provider.QueryAsync(DirectoryObjectType.Computer,
                ["cn", "distinguishedName", "operatingSystem", "description"], maxResults: 25);
            foreach (var r in records)
            {
                var cn = r.GetValueOrDefault("cn", "???");
                var os = r.GetValueOrDefault("operatingSystem", "");
                ComputerPreviewItems.Add(string.IsNullOrEmpty(os) ? cn : $"{cn}  ({os})");
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
            var config = BuildAdConfig();
            using var provider = new ActiveDirectoryProvider(config);
            var records = await provider.QueryAsync(DirectoryObjectType.User,
                ["displayName", "sAMAccountName", "mail"], maxResults: 25);
            foreach (var r in records)
            {
                var name  = r.GetValueOrDefault("displayName", "");
                var sam   = r.GetValueOrDefault("sAMAccountName", "???");
                var mail  = r.GetValueOrDefault("mail", "");
                var label = !string.IsNullOrEmpty(name) ? $"{name} ({sam})" : sam;
                UserPreviewItems.Add(!string.IsNullOrEmpty(mail) ? $"{label}  —  {mail}" : label);
            }
            UserPreviewStatus = $"Showing {records.Count} (max 25 preview)";
        }
        catch (Exception ex) { UserPreviewStatus = $"Error: {ex.Message}"; }
    }

    // All canonical converter methods preserved unchanged from original
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

    // -- Config mapping (updated to use first AD source) -------------------

    public void LoadFromConfig()
    {
        var ad = GetFirstAdConfig();
        Domain   = ad.Domain;
        Server   = ad.Server;
        Port     = ad.Port;
        UseSsl   = ad.UseSsl;
        Username = ad.Username;
        ComputerSearchBase        = ad.ComputerSearchBase;
        UserSearchBase            = ad.UserSearchBase;
        AdditionalUserSearchBases = string.Join(Environment.NewLine, ad.AdditionalUserSearchBases);
        ComputerFilter = ad.ComputerFilter;
        UserFilter     = ad.UserFilter;

        if (!string.IsNullOrEmpty(ad.EncryptedPassword))
        {
            try { Password = ConfigService.DecryptPassword(ad.EncryptedPassword); }
            catch { Password = string.Empty; }
        }
    }

    public void ApplyToConfig()
    {
        var ad = TryGetFirstAdConfig();
        if (ad is null) return; // no AD source configured — nothing to save

        ad.Domain   = Domain;
        ad.Server   = Server;
        ad.Port     = Port;
        ad.UseSsl   = UseSsl;
        ad.Username = Username;
        ad.ComputerSearchBase = ComputerSearchBase;
        ad.UserSearchBase     = UserSearchBase;
        ad.AdditionalUserSearchBases = AdditionalUserSearchBases
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        ad.ComputerFilter    = ComputerFilter;
        ad.UserFilter        = UserFilter;
        ad.EncryptedPassword = ConfigService.EncryptPassword(Password);
    }

    private AdConnectionConfig BuildAdConfig() => new()
    {
        Domain   = Domain,
        Server   = Server,
        Port     = Port,
        UseSsl   = UseSsl,
        Username = Username,
        EncryptedPassword     = ConfigService.EncryptPassword(Password),
        ComputerSearchBase    = ComputerSearchBase,
        UserSearchBase        = UserSearchBase,
        AdditionalUserSearchBases = AdditionalUserSearchBases
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList(),
        ComputerFilter = ComputerFilter,
        UserFilter     = UserFilter
    };
}