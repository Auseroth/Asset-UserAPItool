namespace LdapCloudSync.Core.Models;

/// <summary>
/// Root configuration object. Serialized to/from JSON in ProgramData.
/// </summary>
public sealed class SyncConfig
{
    public AdConnectionConfig ActiveDirectory { get; set; } = new();
    public List<CloudTargetConfig> CloudTargets { get; set; } = [];
    public LoggingConfig Logging { get; set; } = new();
}

/// <summary>
/// Active Directory connection and search configuration.
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
    /// Optional LDAP filter override for users. Defaults to (&amp;(objectClass=user)(objectCategory=person)).
    /// </summary>
    public string UserFilter { get; set; } = "(&(objectClass=user)(objectCategory=person))";
}