using LdapCloudSync.Core.Models;

namespace LdapCloudSync.Core.Interfaces;

/// <summary>
/// Abstraction for cloud service API clients.
/// Implementations handle authentication, field discovery, and record synchronization.
/// </summary>
public interface ICloudClient : IDisposable
{
    /// <summary>
    /// Tests connectivity and authentication to the cloud service.
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync();

    /// <summary>
    /// Discovers available fields/properties from the cloud API response.
    /// Useful for dynamic field mapping configuration.
    /// </summary>
    Task<(IReadOnlyList<string> Fields, string RawResponse)> DiscoverFieldsAsync(string category);

    /// <summary>
    /// Pushes a batch of records to the cloud service.
    /// Handles create vs. update logic internally.
    /// </summary>
    Task<SyncResult> PushRecordsAsync(string category, IReadOnlyList<Dictionary<string, object>> records);
}

public sealed class SyncResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Last successful HTTP response body returned by the target API for this sync call.
    /// </summary>
    public string LastSuccessResponseBody { get; set; } = string.Empty;
}
