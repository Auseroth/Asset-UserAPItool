using System.Collections.ObjectModel;
using System.Windows.Input;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Providers;
using LdapCloudSync.Core.Services;

namespace LdapCloudSync.App.ViewModels;

public sealed class AdSettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;

    public AdSettingsViewModel(ConfigService configService)
    {
        _configService = configService;
        LoadFromConfig();

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        PreviewComputersCommand = new AsyncRelayCommand(PreviewComputersAsync);
        PreviewUsersCommand = new AsyncRelayCommand(PreviewUsersAsync);
        ConvertCanonicalCommand = new RelayCommand(ConvertCanonical);
        UseAsComputerSearchBaseCommand = new RelayCommand(ApplyAsComputerSearchBase);
        UseAsUserSearchBaseCommand = new RelayCommand(ApplyAsUserSearchBase);
        UseAsUserFilterGroupCommand = new RelayCommand(ApplyConvertedAsGroupFilter);
    }

    // Fields
    private string _domain = string.Empty;
    public string Domain
    {
        get => _domain;
        set => SetProperty(ref _domain, value);
    }

    private string _server = string.Empty;
    public string Server
    {
        get => _server;
        set => SetProperty(ref _server, value);
    }

    private int _port = 389;
    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

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
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
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

    // Test connection
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

    // Preview results
    public ObservableCollection<string> ComputerPreviewItems { get; } = [];
    public ObservableCollection<string> UserPreviewItems { get; } = [];

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

    // Canonical name converter
    private string _canonicalInput = string.Empty;
    public string CanonicalInput
    {
        get => _canonicalInput;
        set
        {
            if (SetProperty(ref _canonicalInput, value))
                ConvertCanonical();
        }
    }

    private string _convertedDn = string.Empty;
    public string ConvertedDn
    {
        get => _convertedDn;
        set => SetProperty(ref _convertedDn, value);
    }

    // Commands
    public ICommand TestConnectionCommand { get; }
    public ICommand PreviewComputersCommand { get; }
    public ICommand PreviewUsersCommand { get; }
    public ICommand ConvertCanonicalCommand { get; }
    public ICommand UseAsComputerSearchBaseCommand { get; }
    public ICommand UseAsUserSearchBaseCommand { get; }
    public ICommand UseAsUserFilterGroupCommand { get; }

    private async Task TestConnectionAsync()
    {
        ConnectionStatus = "Testing connection...";
        IsConnectionSuccess = false;

        var testConfig = BuildAdConfig();
        using var provider = new ActiveDirectoryProvider(testConfig);
        var (success, message) = await provider.TestConnectionAsync();

        ConnectionStatus = message;
        IsConnectionSuccess = success;
    }

    private async Task PreviewComputersAsync()
    {
        ComputerPreviewItems.Clear();
        ComputerPreviewStatus = "Querying...";

        try
        {
            var config = BuildAdConfig();
            using var provider = new ActiveDirectoryProvider(config);
            var records = await provider.QueryAsync(
                DirectoryObjectType.Computer,
                ["cn", "distinguishedName", "operatingSystem"],
                maxResults: 25);

            foreach (var record in records)
            {
                var cn = record.GetValueOrDefault("cn", "???");
                var os = record.GetValueOrDefault("operatingSystem", "");
                var display = string.IsNullOrEmpty(os) ? cn : $"{cn} ({os})";
                ComputerPreviewItems.Add(display);
            }

            ComputerPreviewStatus = $"Showing {records.Count} of total (max 25 preview)";
        }
        catch (Exception ex)
        {
            ComputerPreviewStatus = $"Error: {ex.Message}";
        }
    }

    private async Task PreviewUsersAsync()
    {
        UserPreviewItems.Clear();
        UserPreviewStatus = "Querying...";

        try
        {
            var config = BuildAdConfig();
            using var provider = new ActiveDirectoryProvider(config);
            var records = await provider.QueryAsync(
                DirectoryObjectType.User,
                ["displayName", "sAMAccountName", "mail"],
                maxResults: 25);

            foreach (var record in records)
            {
                var name = record.GetValueOrDefault("displayName", "");
                var sam = record.GetValueOrDefault("sAMAccountName", "???");
                var mail = record.GetValueOrDefault("mail", "");
                var display = !string.IsNullOrEmpty(name)
                    ? $"{name} ({sam})"
                    : !string.IsNullOrEmpty(mail)
                        ? $"{sam} - {mail}"
                        : sam;
                UserPreviewItems.Add(display);
            }

            UserPreviewStatus = $"Showing {records.Count} of total (max 25 preview)";
        }
        catch (Exception ex)
        {
            UserPreviewStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Parses the canonical input into individual lines, skipping blanks.
    /// </summary>
    private string[] GetCanonicalLines()
    {
        if (string.IsNullOrWhiteSpace(CanonicalInput))
            return [];

        return CanonicalInput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Converts a single canonical name to DN.
    /// </summary>
    private static string ConvertSingleCanonicalToDn(string canonical, bool lastPartIsCn)
    {
        var input = canonical.Trim().TrimEnd('/');
        var parts = input.Split('/');

        if (parts.Length < 1)
            return string.Empty;

        var domainPart = parts[0];
        var dcComponents = domainPart.Split('.').Select(d => $"DC={d}");
        var pathParts = parts.Skip(1).ToArray();

        if (pathParts.Length == 0)
            return string.Join(",", dcComponents);

        var dnParts = new List<string>();

        for (int i = pathParts.Length - 1; i >= 0; i--)
        {
            var prefix = (i == pathParts.Length - 1 && lastPartIsCn) ? "CN" : "OU";
            dnParts.Add($"{prefix}={pathParts[i]}");
        }

        dnParts.AddRange(dcComponents);
        return string.Join(",", dnParts);
    }

    /// <summary>
    /// Auto-converts on input change. Shows CN= format (group-oriented) in the preview.
    /// Multiple lines show one DN per line.
    /// </summary>
    private void ConvertCanonical()
    {
        var lines = GetCanonicalLines();
        if (lines.Length == 0)
        {
            ConvertedDn = string.Empty;
            return;
        }

        try
        {
            var dns = lines.Select(l => ConvertSingleCanonicalToDn(l, lastPartIsCn: true));
            ConvertedDn = string.Join(Environment.NewLine, dns);
        }
        catch
        {
            ConvertedDn = "Invalid format. Expected: domain.com/OU1/OU2/Name";
        }
    }

    /// <summary>
    /// Extracts only the DC= portion from a DN for use as a broad search base.
    /// </summary>
    private static string GetDomainDn(string dn)
    {
        if (string.IsNullOrEmpty(dn))
            return dn;

        var dcIndex = dn.IndexOf("DC=", StringComparison.OrdinalIgnoreCase);
        return dcIndex >= 0 ? dn[dcIndex..] : dn;
    }

    /// <summary>
    /// Builds an LDAP filter for memberOf across one or more groups.
    /// Single:   (&amp;(objectClass=user)(objectCategory=person)(memberOf=CN=...))
    /// Multiple: (&amp;(objectClass=user)(objectCategory=person)(|(memberOf=CN=...)(memberOf=CN=...)))
    /// </summary>
    private static string BuildMemberOfFilter(IReadOnlyList<string> groupDns)
    {
        if (groupDns.Count == 1)
        {
            return $"(&(objectClass=user)(objectCategory=person)(memberOf={groupDns[0]}))";
        }

        // Multiple groups: OR them together
        var memberOfClauses = string.Join("", groupDns.Select(dn => $"(memberOf={dn})"));
        return $"(&(objectClass=user)(objectCategory=person)(|{memberOfClauses}))";
    }

    private void ApplyAsComputerSearchBase()
    {
        var lines = GetCanonicalLines();
        if (lines.Length == 0) return;

        // Use first line as OU search base
        ComputerSearchBase = ConvertSingleCanonicalToDn(lines[0], lastPartIsCn: false);
        OnPropertyChanged(nameof(ComputerSearchBase));
    }

    private void ApplyAsUserSearchBase()
    {
        var lines = GetCanonicalLines();
        if (lines.Length == 0) return;

        // Convert all lines as OU paths
        var ouDns = lines
            .Select(l => ConvertSingleCanonicalToDn(l, lastPartIsCn: false))
            .Where(dn => !string.IsNullOrEmpty(dn))
            .ToList();

        if (ouDns.Count == 0) return;

        // First one is the primary search base
        UserSearchBase = ouDns[0];

        // Rest go into additional search bases (one per line)
        AdditionalUserSearchBases = ouDns.Count > 1
            ? string.Join(Environment.NewLine, ouDns.Skip(1))
            : string.Empty;

        UserFilter = "(&(objectClass=user)(objectCategory=person))";
        OnPropertyChanged(nameof(UserSearchBase));
        OnPropertyChanged(nameof(AdditionalUserSearchBases));
        OnPropertyChanged(nameof(UserFilter));
    }

    private void ApplyConvertedAsGroupFilter()
    {
        var lines = GetCanonicalLines();
        if (lines.Length == 0) return;

        // Convert all lines as group CNs
        var groupDns = lines
            .Select(l => ConvertSingleCanonicalToDn(l, lastPartIsCn: true))
            .Where(dn => !string.IsNullOrEmpty(dn))
            .ToList();

        if (groupDns.Count == 0) return;

        // Search base = domain root (members could be in any OU)
        UserSearchBase = GetDomainDn(groupDns[0]);
        AdditionalUserSearchBases = string.Empty;
        UserFilter = BuildMemberOfFilter(groupDns);
        OnPropertyChanged(nameof(UserSearchBase));
        OnPropertyChanged(nameof(AdditionalUserSearchBases));
        OnPropertyChanged(nameof(UserFilter));
    }

    public void LoadFromConfig()
    {
        var ad = _configService.Current.ActiveDirectory;
        Domain = ad.Domain;
        Server = ad.Server;
        Port = ad.Port;
        UseSsl = ad.UseSsl;
        Username = ad.Username;
        ComputerSearchBase = ad.ComputerSearchBase;
        UserSearchBase = ad.UserSearchBase;
        AdditionalUserSearchBases = string.Join(Environment.NewLine, ad.AdditionalUserSearchBases);
        ComputerFilter = ad.ComputerFilter;
        UserFilter = ad.UserFilter;

        if (!string.IsNullOrEmpty(ad.EncryptedPassword))
        {
            try { Password = ConfigService.DecryptPassword(ad.EncryptedPassword); }
            catch { Password = string.Empty; }
        }
    }

    public void ApplyToConfig()
    {
        var ad = _configService.Current.ActiveDirectory;
        ad.Domain = Domain;
        ad.Server = Server;
        ad.Port = Port;
        ad.UseSsl = UseSsl;
        ad.Username = Username;
        ad.ComputerSearchBase = ComputerSearchBase;
        ad.UserSearchBase = UserSearchBase;
        ad.AdditionalUserSearchBases = AdditionalUserSearchBases
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        ad.ComputerFilter = ComputerFilter;
        ad.UserFilter = UserFilter;
        ad.EncryptedPassword = ConfigService.EncryptPassword(Password);
    }

    private AdConnectionConfig BuildAdConfig()
    {
        return new AdConnectionConfig
        {
            Domain = Domain,
            Server = Server,
            Port = Port,
            UseSsl = UseSsl,
            Username = Username,
            EncryptedPassword = ConfigService.EncryptPassword(Password),
            ComputerSearchBase = ComputerSearchBase,
            UserSearchBase = UserSearchBase,
            AdditionalUserSearchBases = AdditionalUserSearchBases
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList(),
            ComputerFilter = ComputerFilter,
            UserFilter = UserFilter
        };
    }
}