namespace LdapCloudSync.Core.Interfaces;

/// <summary>
/// Abstracts cloud REST API operations. Implemented generically --
/// driven entirely by CloudTargetConfig, not provider-specific code.
/// </summary>
public interface ICloudClient
{
    /// <summary>
    /// Tests connectivity to the configured cloud endpoint.
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync();

    /// <summary>
    /// Performs a GET request and extracts field names from the response.
    /// Used for field discovery in the mapping UI.
    /// </summary>
    /// <param name="category">"assets" or "users".</param>
    Task<(IReadOnlyList<string> Fields, string RawResponse)> DiscoverFieldsAsync(string category);

    /// <summary>
    /// Pushes a batch of mapped records to the cloud target.
    /// </summary>
    /// <param name="category">"assets" or "users".</param>
    /// <param name="records">List of field->value dictionaries ready to POST/PUT.</param>
    Task<SyncResult> PushRecordsAsync(string category, IReadOnlyList<Dictionary<string, object>> records);
}

public sealed class SyncResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = [];
}