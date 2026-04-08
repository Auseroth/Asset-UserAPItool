namespace LdapCloudSync.Core.Models;

/// <summary>
/// Configuration for a single cloud sync target.
/// </summary>
public sealed class CloudTargetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Target";
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The original preset this config was created from. 
    /// "Reftab", "SnipeIt", or "Generic".
    /// If the user modifies core structural fields of a preset, this switches to "Custom".
    /// </summary>
    public string PresetOrigin { get; set; } = "Generic";

    public CloudConnectionConfig Connection { get; set; } = new();
    public SyncCategoryConfig Assets { get; set; } = new();
    public SyncCategoryConfig Users { get; set; } = new();
    public ScheduleConfig Schedule { get; set; } = new();
}

/// <summary>
/// REST API connection details for a cloud target.
/// </summary>
public sealed class CloudConnectionConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public AuthType AuthType { get; set; } = AuthType.ApiKey;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Header name for API key auth (e.g., "Authorization", "x-api-key").
    /// </summary>
    public string ApiKeyHeader { get; set; } = "Authorization";

    /// <summary>
    /// Format string for the header value. Use {key} and {secret} placeholders.
    /// e.g., "Bearer {key}" or just "{key}".
    /// </summary>
    public string ApiKeyFormat { get; set; } = "Bearer {key}";

    /// <summary>
    /// For Basic Auth: username.
    /// </summary>
    public string BasicUsername { get; set; } = string.Empty;

    /// <summary>
    /// For Basic Auth: password.
    /// </summary>
    public string BasicPassword { get; set; } = string.Empty;

    /// <summary>
    /// For HMAC: the hashing algorithm (e.g., "HMACSHA256").
    /// </summary>
    public string HmacAlgorithm { get; set; } = "HMACSHA256";

    /// <summary>
    /// Content type for POST/PUT requests. Defaults to JSON.
    /// </summary>
    public string ContentType { get; set; } = "application/json";
}

public enum AuthType
{
    ApiKey,
    BearerToken,
    BasicAuth,
    Hmac
}

/// <summary>
/// Configuration for one sync category (Assets or Users).
/// </summary>
public sealed class SyncCategoryConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// GET endpoint for pulling existing records / discovering fields.
    /// e.g., "/assets" or "/loanees".
    /// </summary>
    public string GetEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// POST endpoint for creating new records.
    /// </summary>
    public string PostEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// PUT endpoint for updating existing records. Use {id} placeholder.
    /// e.g., "/assets/{id}".
    /// </summary>
    public string PutEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// JSONPath expression to the array of items in the GET response.
    /// e.g., "$" for root array, "$.data" for nested, "$.rows" etc.
    /// </summary>
    public string ResponseItemsPath { get; set; } = "$";

    /// <summary>
    /// The field name in the cloud response that represents the unique ID.
    /// </summary>
    public string CloudIdField { get; set; } = "id";

    /// <summary>
    /// The AD attribute used as the unique key to match AD records to cloud records.
    /// e.g., "cn" for computers, "sAMAccountName" for users.
    /// </summary>
    public string AdMatchField { get; set; } = string.Empty;

    /// <summary>
    /// The cloud field to match against the AD match field.
    /// </summary>
    public string CloudMatchField { get; set; } = string.Empty;

    /// <summary>
    /// Field mappings: AD attribute -> Cloud field, with optional transform.
    /// </summary>
    public List<FieldMapping> FieldMappings { get; set; } = [];
}