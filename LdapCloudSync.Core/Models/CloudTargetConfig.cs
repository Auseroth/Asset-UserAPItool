namespace LdapCloudSync.Core.Models;

/// <summary>
/// Configuration for a single cloud target (e.g., Reftab, Snipe-IT, Asset Panda).
/// </summary>
public sealed class CloudTargetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Target";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The Id of the CloudSourceConfig this target pulls data from.
    /// If empty, the orchestrator falls back to the first available AD source.
    /// Set to the reserved prefix "file:" + filename to use a saved JSON file source.
    /// Example: "file:MyExport" reads from ProgramData/LDAPult/sourceFiles/MyExport.json
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Optional post-success action when this target syncs from a linked file source.
    /// </summary>
    public FileSourceSuccessAction FileSourceSuccessAction { get; set; } = FileSourceSuccessAction.None;

    /// <summary>
    /// Suffix used when FileSourceSuccessAction is RenameFile.
    /// </summary>
    public string FileSourceRenameSuffix { get; set; } = "-processed";

    /// <summary>
    /// The preset template that was used to create this config (for reference only).
    /// </summary>
    public string PresetOrigin { get; set; } = "Blank (REST)";

    /// <summary>
    /// The provider type determines which client implementation to use.
    /// </summary>
    public string ProviderType { get; set; } = "Generic (Standard REST)";

    public CloudConnectionConfig Connection { get; set; } = new();
    public SyncCategoryConfig Assets { get; set; } = new();
    public SyncCategoryConfig Users { get; set; } = new();
    public ScheduleConfig Schedule { get; set; } = new();

    // Asset Panda Discovery IDs 

    /// <summary>Asset Panda account ID. Selected from GET /accounts.</summary>
    public string ApAccountId { get; set; } = string.Empty;

    /// <summary>Asset Panda module ID. Selected from GET /accounts/{accountId}/modules.</summary>
    public string ApModuleId { get; set; } = string.Empty;

    /// <summary>Asset Panda collection ID for the Assets category.</summary>
    public string ApAssetsCollectionId { get; set; } = string.Empty;

    /// <summary>Asset Panda collection ID for the Users/People category.</summary>
    public string ApUsersCollectionId { get; set; } = string.Empty;
}

/// <summary>
/// REST API connection details for a cloud target or cloud source.
/// </summary>
public sealed class CloudConnectionConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public AuthType AuthType { get; set; } = AuthType.ApiKey;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>Header name for API key auth (e.g., "Authorization", "x-api-key").</summary>
    public string ApiKeyHeader { get; set; } = "Authorization";

    /// <summary>
    /// Format string for the header value. Use {key} and {secret} placeholders.
    /// e.g., "Bearer {key}" or just "{key}".
    /// </summary>
    public string ApiKeyFormat { get; set; } = "Bearer {key}";

    /// <summary>For Basic Auth: username.</summary>
    public string BasicUsername { get; set; } = string.Empty;

    /// <summary>For Basic Auth: password.</summary>
    public string BasicPassword { get; set; } = string.Empty;

    /// <summary>For HMAC: the hashing algorithm (e.g., "HMACSHA256").</summary>
    public string HmacAlgorithm { get; set; } = "HMACSHA256";

    /// <summary>Content type for POST/PUT requests. Defaults to JSON.</summary>
    public string ContentType { get; set; } = "application/json";
}

public enum AuthType
{
    ApiKey,
    BearerToken,
    BasicAuth,
    Hmac
}

public enum FileSourceSuccessAction
{
    None,
    DeleteFile,
    RenameFile
}

public static class FileSourceSuccessActionValues
{
    public static FileSourceSuccessAction[] All { get; } =
    [
        FileSourceSuccessAction.None,
        FileSourceSuccessAction.DeleteFile,
        FileSourceSuccessAction.RenameFile
    ];
}

/// <summary>
/// Configuration for one sync category (Assets or Users) on a cloud target.
/// </summary>
public sealed class SyncCategoryConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>GET endpoint for pulling existing records / discovering fields.</summary>
    public string GetEndpoint { get; set; } = string.Empty;

    /// <summary>POST endpoint for creating new records.</summary>
    public string PostEndpoint { get; set; } = string.Empty;

    /// <summary>PUT endpoint for updating existing records. Use {id} placeholder.</summary>
    public string PutEndpoint { get; set; } = string.Empty;

    /// <summary>JSONPath expression to the array of items in the GET response.</summary>
    public string ResponseItemsPath { get; set; } = "$";

    /// <summary>The field name in the cloud response that represents the unique ID.</summary>
    public string CloudIdField { get; set; } = "id";

    /// <summary>
    /// The source field used as the unique key to match source records to cloud records.
    /// For AD sources: an LDAP attribute name (e.g., "cn", "sAMAccountName").
    /// For cloud sources: an API response field name.
    /// </summary>
    public string SourceMatchField { get; set; } = string.Empty;

    /// <summary>The cloud field to match against the source match field.</summary>
    public string CloudMatchField { get; set; } = string.Empty;

    /// <summary>Source field used for PUT fallback matching.</summary>
    public string UpdateMatchSourceField { get; set; } = string.Empty;

    /// <summary>Cloud field used for PUT fallback matching.</summary>
    public string UpdateMatchCloudField { get; set; } = string.Empty;

    /// <summary>Secondary source field used for PUT retry fallback matching.</summary>
    public string SecondaryUpdateMatchSourceField { get; set; } = string.Empty;

    /// <summary>Secondary cloud field used for PUT retry fallback matching.</summary>
    public string SecondaryUpdateMatchCloudField { get; set; } = string.Empty;

    /// <summary>
    /// Field mappings: source field -> cloud field, with optional transform.
    /// SourceFields may be AD attribute names or cloud API field names depending on source type.
    /// </summary>
    public List<FieldMapping> FieldMappings { get; set; } = [];

    /// <summary>For Reftab: the category ID (cid) to assign to new assets.</summary>
    public int TargetCategoryId { get; set; } = 0;

    /// <summary>For Reftab: the location ID (clid) to assign to new assets.</summary>
    public int TargetLocationId { get; set; } = 0;

    /// <summary>
    /// Per-target source search base overrides.
    /// For AD sources: OU distinguished names to restrict the LDAP search.
    /// Leave empty to use the AD source's global search base.
    /// </summary>
    public List<string> AdSearchBaseOverrides { get; set; } = [];

    /// <summary>
    /// Per-target LDAP filter override (AD sources only).
    /// Leave empty to use the AD source's global filter.
    /// </summary>
    public string AdFilterOverride { get; set; } = string.Empty;

    /// <summary>
    /// When true, treat AdSearchBaseOverrides as group DNs and generate
    /// a memberOf filter instead of using them as search bases (AD sources only).
    /// </summary>
    public bool AdSourceIsGroup { get; set; } = false;

    //  Legacy property aliases 
    // These map old property names to the new generic names so that
    // ViewModels referencing the old names still compile during migration.

    /// <summary>Alias for SourceMatchField. Use SourceMatchField in new code.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string AdMatchField
    {
        get => SourceMatchField;
        set => SourceMatchField = value;
    }

    /// <summary>Alias for UpdateMatchSourceField. Use UpdateMatchSourceField in new code.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string UpdateMatchAdField
    {
        get => UpdateMatchSourceField;
        set => UpdateMatchSourceField = value;
    }
}