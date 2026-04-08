using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Coordinates a full sync cycle: query AD -> transform -> push to cloud.
/// This is the core engine used by both the service (scheduled) and the app (on-demand).
/// </summary>
public sealed class SyncOrchestrator : ISyncOrchestrator
{
    private readonly ConfigService _configService;
    private readonly Func<AdConnectionConfig, IDirectoryProvider> _directoryFactory;
    private readonly Func<CloudTargetConfig, ICloudClient> _cloudFactory;
    private readonly ILogger _log;

    public SyncOrchestrator(
        ConfigService configService,
        Func<AdConnectionConfig, IDirectoryProvider> directoryFactory,
        Func<CloudTargetConfig, ICloudClient> cloudFactory,
        ILogger? logger = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _directoryFactory = directoryFactory ?? throw new ArgumentNullException(nameof(directoryFactory));
        _cloudFactory = cloudFactory ?? throw new ArgumentNullException(nameof(cloudFactory));
        _log = logger ?? Log.Logger;
    }

    public async Task<SyncResult> RunSyncAsync(
        string targetId,
        string category,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSyncAsync(targetId, category, maxRecords: 0, cancellationToken);
    }

    public async Task<SyncResult> RunTestSyncAsync(
        string targetId,
        string category,
        int maxRecords = 10,
        CancellationToken cancellationToken = default)
    {
        _log.Information("Starting TEST sync for target {TargetId}, category {Category}, max {Max} records",
            targetId, category, maxRecords);
        return await ExecuteSyncAsync(targetId, category, maxRecords, cancellationToken);
    }

    private async Task<SyncResult> ExecuteSyncAsync(
        string targetId,
        string category,
        int maxRecords,
        CancellationToken cancellationToken)
    {
        var config = _configService.Current;
        var target = config.CloudTargets.Find(t => t.Id == targetId);

        if (target is null)
        {
            return new SyncResult
            {
                Errors = [$"Cloud target '{targetId}' not found in configuration."]
            };
        }

        if (!target.Enabled)
        {
            return new SyncResult
            {
                Errors = [$"Cloud target '{target.Name}' is disabled."]
            };
        }

        var categoryConfig = GetCategoryConfig(target, category);
        if (!categoryConfig.Enabled)
        {
            return new SyncResult
            {
                Errors = [$"{category} sync is disabled for target '{target.Name}'."]
            };
        }

        if (categoryConfig.FieldMappings.Count == 0)
        {
            return new SyncResult
            {
                Errors = [$"No field mappings configured for {category} on target '{target.Name}'."]
            };
        }

        var syncLabel = maxRecords > 0 ? $"TEST({maxRecords})" : "FULL";
        _log.Information("=== {SyncLabel} SYNC START: {Target} / {Category} ===",
            syncLabel, target.Name, category);

        try
        {
            // Step 1: Determine which AD attributes we need
            var requiredAttributes = GetRequiredAdAttributes(categoryConfig);
            _log.Information("Requesting {Count} AD attributes: {Attributes}",
                requiredAttributes.Count, string.Join(", ", requiredAttributes));

            // Step 2: Query Active Directory
            cancellationToken.ThrowIfCancellationRequested();
            var objectType = category.ToLowerInvariant() == "assets"
                ? DirectoryObjectType.Computer
                : DirectoryObjectType.User;

            using var directoryProvider = (IDisposable?)_directoryFactory(config.ActiveDirectory) as IDisposable;
            var adProvider = _directoryFactory(config.ActiveDirectory);
            var adRecords = await adProvider.QueryAsync(objectType, requiredAttributes, maxRecords);

            _log.Information("Retrieved {Count} records from AD", adRecords.Count);

            if (adRecords.Count == 0)
            {
                _log.Warning("No AD records found for {Category}. Check your search base and filters.", category);
                return new SyncResult { Skipped = 0 };
            }

            // Step 3: Transform AD records to cloud-ready records
            cancellationToken.ThrowIfCancellationRequested();
            var transformEngine = new TransformEngine(_log);
            var cloudRecords = transformEngine.TransformBatch(adRecords, categoryConfig.FieldMappings);

            _log.Information("Transformed {Count} records ready for push", cloudRecords.Count);

            // Step 4: Push to cloud
            cancellationToken.ThrowIfCancellationRequested();
            using var cloudClient = _cloudFactory(target) as IDisposable;
            var client = _cloudFactory(target);
            var result = await client.PushRecordsAsync(category, cloudRecords);

            _log.Information(
                "=== {SyncLabel} SYNC COMPLETE: {Target} / {Category} -- Created: {Created}, Updated: {Updated}, Failed: {Failed} ===",
                syncLabel, target.Name, category, result.Created, result.Updated, result.Failed);

            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Sync cancelled for {Target} / {Category}", target.Name, category);
            return new SyncResult { Errors = ["Sync was cancelled."] };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Sync failed for {Target} / {Category}", target.Name, category);
            return new SyncResult { Errors = [ex.Message] };
        }
    }

    /// <summary>
    /// Collects all unique AD attribute names needed by the field mappings,
    /// including the AD match field.
    /// </summary>
    private static List<string> GetRequiredAdAttributes(SyncCategoryConfig categoryConfig)
    {
        var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always need the match field
        if (!string.IsNullOrEmpty(categoryConfig.AdMatchField))
            attributes.Add(categoryConfig.AdMatchField);

        foreach (var mapping in categoryConfig.FieldMappings)
        {
            // Add explicitly listed AD attributes
            foreach (var attr in mapping.AdAttributes)
            {
                attributes.Add(attr);
            }

            // Also extract any attributes referenced in the transform expression
            if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
            {
                foreach (var attr in TransformEngine.ExtractAttributeNames(mapping.TransformExpression))
                {
                    attributes.Add(attr);
                }
            }
        }

        return attributes.ToList();
    }

    private static SyncCategoryConfig GetCategoryConfig(CloudTargetConfig target, string category)
    {
        return category.ToLowerInvariant() switch
        {
            "assets" => target.Assets,
            "users" or "loanees" => target.Users,
            _ => throw new ArgumentException($"Unknown sync category: {category}", nameof(category))
        };
    }
}