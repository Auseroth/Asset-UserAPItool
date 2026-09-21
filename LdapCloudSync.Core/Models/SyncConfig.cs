namespace LdapCloudSync.Core.Models;

/// <summary>
/// Root configuration object. Serialized to/from JSON in ProgramData.
/// </summary>
public sealed class SyncConfig
{
    /// <summary>
    /// All configured data sources (AD domains and cloud APIs).
    /// Targets reference a source by its Id via CloudTargetConfig.SourceId.
    /// </summary>
    public List<CloudSourceConfig> Sources { get; set; } = [];

    public List<CloudTargetConfig> CloudTargets { get; set; } = [];
    public LoggingConfig Logging { get; set; } = new();
    public KioskConfig Kiosk { get; set; } = new();
}

/// <summary>
/// Active Directory connection and search configuration.
/// Used inside CloudSourceConfig.Ad for AD-type sources.
/// </summary>
public sealed class AdConnectionConfig
{
    public string Domain { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; } = false;
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-encrypted password (base64 string). Never stored in plaintext.
    /// </summary>
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>
    /// Base OU for computer searches (e.g., "OU=Computers,DC=contoso,DC=com").
    /// </summary>
    public string ComputerSearchBase { get; set; } = string.Empty;

    /// <summary>
    /// Primary user search base. For a single OU search.
    /// </summary>
    public string UserSearchBase { get; set; } = string.Empty;

    /// <summary>
    /// Additional user search bases. Each is queried separately and results are
    /// merged with the primary UserSearchBase, deduplicated by sAMAccountName.
    /// One per line or semicolon-separated in the UI.
    /// </summary>
    public List<string> AdditionalUserSearchBases { get; set; } = [];

    /// <summary>
    /// Optional LDAP filter override for computers. Defaults to (objectClass=computer).
    /// </summary>
    public string ComputerFilter { get; set; } = "(objectClass=computer)";

    /// <summary>
    /// Optional LDAP filter override for users.
    /// </summary>
    public string UserFilter { get; set; } = "(&(objectClass=user)(objectCategory=person))";
}