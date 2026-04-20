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

            // Step 2: Query Active Directory (with per-target overrides)
            cancellationToken.ThrowIfCancellationRequested();
            var objectType = category.ToLowerInvariant() == "assets"
                ? DirectoryObjectType.Computer
                : DirectoryObjectType.User;

            var effectiveAdConfig = BuildEffectiveAdConfig(config.ActiveDirectory, categoryConfig, objectType);

            _log.Information("AD source for {Target}/{Category}: SearchBase={SearchBase}, Filter={Filter}",
                target.Name, category,
                objectType == DirectoryObjectType.Computer
                    ? effectiveAdConfig.ComputerSearchBase
                    : effectiveAdConfig.UserSearchBase,
                objectType == DirectoryObjectType.Computer
                    ? effectiveAdConfig.ComputerFilter
                    : effectiveAdConfig.UserFilter);

            var adProvider = _directoryFactory(effectiveAdConfig);
            var adRecords = await adProvider.QueryAsync(objectType, requiredAttributes, maxRecords);

            if (adProvider is IDisposable disposable)
                disposable.Dispose();

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
    /// including the AD match field and the update match field.
    /// </summary>
    private static List<string> GetRequiredAdAttributes(SyncCategoryConfig categoryConfig)
    {
        var attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always need the match field
        if (!string.IsNullOrEmpty(categoryConfig.AdMatchField))
            attributes.Add(categoryConfig.AdMatchField);

        // Also need the update match field for PUT fallback
        if (!string.IsNullOrEmpty(categoryConfig.UpdateMatchAdField))
            attributes.Add(categoryConfig.UpdateMatchAdField);

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

    /// <summary>
    /// Builds an effective AD config by applying per-target overrides on top of the global config.
    /// Supports multiple selected OUs or groups.
    /// </summary>
    private static AdConnectionConfig BuildEffectiveAdConfig(
        AdConnectionConfig globalConfig,
        SyncCategoryConfig categoryConfig,
        DirectoryObjectType objectType)
    {
        var overrides = categoryConfig.AdSearchBaseOverrides
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        var hasSearchOverride = overrides.Count > 0;
        var hasFilterOverride = !string.IsNullOrWhiteSpace(categoryConfig.AdFilterOverride);
        var isGroupMode = categoryConfig.AdSourceIsGroup && hasSearchOverride;

        if (!hasSearchOverride && !hasFilterOverride)
            return globalConfig;

        // Group mode: build a memberOf filter from all selected groups
        if (isGroupMode)
        {
            // Extract domain root from first group DN
            var dcIndex = overrides[0].IndexOf("DC=", StringComparison.OrdinalIgnoreCase);
            var domainDn = dcIndex >= 0 ? overrides[0][dcIndex..] : globalConfig.UserSearchBase;

            string memberOfFilter;
            if (overrides.Count == 1)
            {
                memberOfFilter = $"(&(objectClass=user)(objectCategory=person)(memberOf={overrides[0]}))";
            }
            else
            {
                var clauses = string.Join("", overrides.Select(dn => $"(memberOf={dn})"));
                memberOfFilter = $"(&(objectClass=user)(objectCategory=person)(|{clauses}))";
            }

            return new AdConnectionConfig
            {
                Domain = globalConfig.Domain,
                Server = globalConfig.Server,
                Port = globalConfig.Port,
                UseSsl = globalConfig.UseSsl,
                Username = globalConfig.Username,
                EncryptedPassword = globalConfig.EncryptedPassword,
                ComputerSearchBase = globalConfig.ComputerSearchBase,
                UserSearchBase = domainDn,
                AdditionalUserSearchBases = [],
                ComputerFilter = globalConfig.ComputerFilter,
                UserFilter = memberOfFilter
            };
        }

        // OU mode: first selected is primary, rest are additional
        var primary = overrides[0];
        var additional = overrides.Skip(1).ToList();

        if (objectType == DirectoryObjectType.Computer)
        {
            return new AdConnectionConfig
            {
                Domain = globalConfig.Domain,
                Server = globalConfig.Server,
                Port = globalConfig.Port,
                UseSsl = globalConfig.UseSsl,
                Username = globalConfig.Username,
                EncryptedPassword = globalConfig.EncryptedPassword,
                ComputerSearchBase = primary,
                UserSearchBase = globalConfig.UserSearchBase,
                AdditionalUserSearchBases = globalConfig.AdditionalUserSearchBases,
                ComputerFilter = hasFilterOverride
                    ? categoryConfig.AdFilterOverride.Trim()
                    : globalConfig.ComputerFilter,
                UserFilter = globalConfig.UserFilter
            };
        }

        // User OU mode
        return new AdConnectionConfig
        {
            Domain = globalConfig.Domain,
            Server = globalConfig.Server,
            Port = globalConfig.Port,
            UseSsl = globalConfig.UseSsl,
            Username = globalConfig.Username,
            EncryptedPassword = globalConfig.EncryptedPassword,
            ComputerSearchBase = globalConfig.ComputerSearchBase,
            UserSearchBase = primary,
            AdditionalUserSearchBases = additional,
            ComputerFilter = globalConfig.ComputerFilter,
            UserFilter = hasFilterOverride
                ? categoryConfig.AdFilterOverride.Trim()
                : globalConfig.UserFilter
        };
    }

    /// <summary>
    /// Public accessor for GetRequiredAdAttributes, used by the app for test sync preview.
    /// </summary>
    public static List<string> GetRequiredAdAttributesPublic(SyncCategoryConfig categoryConfig)
    {
        return GetRequiredAdAttributes(categoryConfig);
    }

    /// <summary>
    /// Public accessor for BuildEffectiveAdConfig, used by the app for test sync preview.
    /// </summary>
    public static AdConnectionConfig BuildEffectiveAdConfigPublic(
        AdConnectionConfig globalConfig,
        SyncCategoryConfig categoryConfig,
        DirectoryObjectType objectType)
    {
        return BuildEffectiveAdConfig(globalConfig, categoryConfig, objectType);
    }
}