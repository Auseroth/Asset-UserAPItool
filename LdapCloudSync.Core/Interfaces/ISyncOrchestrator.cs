namespace LdapCloudSync.Core.Interfaces;

/// <summary>
/// Coordinates a full sync cycle: query AD -> transform -> push to cloud.
/// </summary>
public interface ISyncOrchestrator
{
    /// <summary>
    /// Runs a full sync for the specified target and category.
    /// </summary>
    Task<SyncResult> RunSyncAsync(string targetId, string category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a test sync (limited to maxRecords) for validation.
    /// </summary>
    Task<SyncResult> RunTestSyncAsync(string targetId, string category, int maxRecords = 10, CancellationToken cancellationToken = default);
}