namespace LdapCloudSync.Core.Models;

/// <summary>
/// Identifies the type of a configured source.
/// File sources are not stored here  they are discovered from the filesystem.
/// </summary>
public enum SourceType
{
    AD,
    Cloud
}

/// <summary>
/// Configuration for a single data source (AD domain or cloud API).
/// Used by cloud targets via SourceId to determine where to pull records from.
/// </summary>
public sealed class CloudSourceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Source";
    public bool Enabled { get; set; } = true;
    public SourceType SourceType { get; set; } = SourceType.Cloud;

    //  Cloud source fields 
    // Used when SourceType == Cloud. Reuses the same connection + provider
    // infrastructure as targets, but only GET endpoints are exercised.

    /// <summary>
    /// The provider type determines which client implementation is used.
    /// </summary>
    public string ProviderType { get; set; } = "Generic (Standard REST)";

    public CloudConnectionConfig Connection { get; set; } = new();
    public SourceReadConfig Assets { get; set; } = new();
    public SourceReadConfig Users { get; set; } = new();

    // Asset Panda discovery IDs (cloud source only)
    public string ApAccountId { get; set; } = string.Empty;
    public string ApModuleId { get; set; } = string.Empty;
    public string ApAssetsCollectionId { get; set; } = string.Empty;
    public string ApUsersCollectionId { get; set; } = string.Empty;

    // AD source fields 
    // Used when SourceType == AD. Each AD source is fully independent
    // (separate domain, credentials, and search configuration).

    public AdConnectionConfig Ad { get; set; } = new();
}

/// <summary>
/// Read-oriented category configuration for a cloud source.
/// Mirrors the GET-relevant subset of SyncCategoryConfig.
/// </summary>
public sealed class SourceReadConfig
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// GET endpoint for fetching records (e.g., "/assets", "/api/v1/users").
    /// </summary>
    public string GetEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// JSONPath expression to the array of items in the GET response.
    /// e.g., "$" for root array, "$.data" for nested.
    /// </summary>
    public string ResponseItemsPath { get; set; } = "$";

    /// <summary>
    /// Optional filter string. Appended to the GET request as a query parameter.
    /// Backend implementation varies by provider. UI placeholder for now.
    /// Example: "status=active&amp;type=laptop"
    /// </summary>
    public string Filter { get; set; } = string.Empty;

    /// <summary>
    /// The field in the source response that acts as the unique record identifier.
    /// </summary>
    public string SourceIdField { get; set; } = "id";

    /// <summary>
    /// For Reftab: the category ID to filter source records by.
    /// Leave 0 to fetch all categories.
    /// </summary>
    public int SourceCategoryId { get; set; } = 0;
}