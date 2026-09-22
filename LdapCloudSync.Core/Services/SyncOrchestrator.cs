using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Providers;
using Serilog;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Coordinates a full sync cycle: resolve source -> query -> transform -> push to cloud target.
/// Supports AD sources, cloud API sources, and file sources (saved JSON).
/// The source for each target is determined by CloudTargetConfig.SourceId.
/// </summary>
public sealed class SyncOrchestrator : ISyncOrchestrator
{
    private readonly ConfigService _configService;
    private readonly Func<AdConnectionConfig, IDirectoryProvider> _directoryFactory;
    private readonly Func<CloudTargetConfig, ICloudClient> _cloudFactory;
    private readonly Func<CloudSourceConfig, ICloudSourceClient> _sourceFactory;
    private readonly SourceFileService _sourceFileService;
    private readonly ILogger _log;

    public SyncOrchestrator(
        ConfigService configService,
        Func<AdConnectionConfig, IDirectoryProvider> directoryFactory,
        Func<CloudTargetConfig, ICloudClient> cloudFactory,
        Func<CloudSourceConfig, ICloudSourceClient> sourceFactory,
        SourceFileService sourceFileService,
        ILogger? logger = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _directoryFactory = directoryFactory ?? throw new ArgumentNullException(nameof(directoryFactory));
        _cloudFactory = cloudFactory ?? throw new ArgumentNullException(nameof(cloudFactory));
        _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        _sourceFileService = sourceFileService ?? throw new ArgumentNullException(nameof(sourceFileService));
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
            return new SyncResult { Errors = [$"Cloud target '{targetId}' not found in configuration."] };

        if (!target.Enabled)
            return new SyncResult { Errors = [$"Cloud target '{target.Name}' is disabled."] };

        var categoryConfig = GetCategoryConfig(target, category);

        if (!categoryConfig.Enabled)
            return new SyncResult { Errors = [$"{category} sync is disabled for target '{target.Name}'."] };

        if (categoryConfig.FieldMappings.Count == 0)
            return new SyncResult { Errors = [$"No field mappings configured for {category} on target '{target.Name}'."] };

        var syncLabel = maxRecords > 0 ? $"TEST({maxRecords})" : "FULL";
        _log.Information("=== {SyncLabel} SYNC START: {Target} / {Category} ===",
            syncLabel, target.Name, category);

        try
        {
            // -- Step 1: Resolve source -------------------------------------
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<Dictionary<string, string>> sourceRecords;

            var sourceId = target.SourceId;
            var isFileSource = sourceId.StartsWith(SourceFileService.FileSourcePrefix, StringComparison.Ordinal);
            string? fileSourceName = null;

            if (isFileSource)
            {
                // File source: read from saved JSON in ProgramData/ConNexus/sourceFiles/
                fileSourceName = sourceId[SourceFileService.FileSourcePrefix.Length..];
                _log.Information("Source: file '{FileName}' for {Target}/{Category}", fileSourceName, target.Name, category);

                var fileRecords = await _sourceFileService.ReadRecordsAsync(fileSourceName, category);
                sourceRecords = maxRecords > 0 ? fileRecords.Take(maxRecords).ToList() : fileRecords;
            }
            else
            {
                // Resolve CloudSourceConfig (null sourceId falls back to first AD source)
                var source = string.IsNullOrEmpty(sourceId)
                    ? null
                    : config.Sources.Find(s => s.Id == sourceId);

                if (!string.IsNullOrEmpty(sourceId) && source is null)
                {
                    return new SyncResult
                    {
                        Errors = [$"Source '{sourceId}' not found. Check Sources tab configuration."]
                    };
                }

                // Determine effective source type
                var sourceType = source?.SourceType
                    ?? (config.Sources.Any(s => s.SourceType == SourceType.AD) ? SourceType.AD : SourceType.Cloud);

                if (sourceType == SourceType.Cloud && source is not null)
                {
                    // -- Cloud API source -----------------------------------
                    _log.Information("Source: cloud '{SourceName}' for {Target}/{Category}",
                        source.Name, target.Name, category);

                    var readConfig = GetSourceReadConfig(source, category);
                    var filter = string.IsNullOrWhiteSpace(readConfig.Filter) ? null : readConfig.Filter;

                    using var sourceClient = _sourceFactory(source);
                    var (records, _) = await sourceClient.GetRecordsAsync(category, filter, maxRecords);
                    sourceRecords = records;

                    _log.Information("Retrieved {Count} records from cloud source '{SourceName}'",
                        records.Count, source.Name);
                }
                else
                {
                    // -- AD source ------------------------------------------
                    var adConfig = source?.Ad
                        ?? config.Sources.FirstOrDefault(s => s.SourceType == SourceType.AD)?.Ad;

                    if (adConfig is null)
                    {
                        return new SyncResult
                        {
                            Errors = ["No AD source configured. Add an AD source on the Sources tab."]
                        };
                    }

                    _log.Information("Source: AD '{SourceName}' for {Target}/{Category}",
                        source?.Name ?? "(default)", target.Name, category);

                    var objectType = category.ToLowerInvariant() == "assets"
                        ? DirectoryObjectType.Computer
                        : DirectoryObjectType.User;

                    var requiredAttributes = GetRequiredSourceFields(categoryConfig);
                    var effectiveAdConfig = BuildEffectiveAdConfig(adConfig, categoryConfig, objectType);

                    var adProvider = _directoryFactory(effectiveAdConfig);
                    sourceRecords = await adProvider.QueryAsync(objectType, requiredAttributes, maxRecords);

                    if (adProvider is IDisposable disposable)
                        disposable.Dispose();

                    _log.Information("Retrieved {Count} records from AD source", sourceRecords.Count);
                }
            }

            // -- Step 2: Guard empty 
            if (sourceRecords.Count == 0)
            {
                _log.Warning("No source records found for {Category} on {Target}. Check source config.",
                    category, target.Name);
                return new SyncResult { Skipped = 0 };
            }

            //  Step 3: Transform 
            cancellationToken.ThrowIfCancellationRequested();
            var transformEngine = new TransformEngine(_log);
            var cloudRecords = transformEngine.TransformBatch(sourceRecords, categoryConfig.FieldMappings);
            _log.Information("Transformed {Count} records ready for push", cloudRecords.Count);

            //  Step 4: Push to target 
            cancellationToken.ThrowIfCancellationRequested();
            var client = _cloudFactory(target);
            var result = await client.PushRecordsAsync(category, cloudRecords);

            _log.Information(
                "=== {SyncLabel} SYNC COMPLETE: {Target} / {Category} — Created: {Created}, Updated: {Updated}, Failed: {Failed} ===",
                syncLabel, target.Name, category, result.Created, result.Updated, result.Failed);

            if (isFileSource
                && maxRecords == 0
                && fileSourceName is not null
                && result.Failed == 0
                && result.Errors.Count == 0)
            {
                try
                {
                    var (applied, message) = await _sourceFileService.ApplyLinkedFileSuccessActionAsync(
                        fileSourceName,
                        target.FileSourceSuccessAction,
                        target.FileSourceRenameSuffix);

                    if (applied)
                        _log.Information("Post-success file action applied for source '{SourceName}': {Message}", fileSourceName, message);
                    else if (target.FileSourceSuccessAction != FileSourceSuccessAction.None)
                        _log.Warning("Post-success file action skipped for source '{SourceName}': {Message}", fileSourceName, message);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Post-success file action failed for source '{SourceName}'", fileSourceName);
                }
            }

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

    //  Helpers 

    /// <summary>
    /// Collects all unique source field names needed by the field mappings,
    /// including the source match field and update match field.
    /// Works for both AD attribute names and cloud API field names.
    /// </summary>
    private static List<string> GetRequiredSourceFields(SyncCategoryConfig categoryConfig)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(categoryConfig.SourceMatchField))
            fields.Add(categoryConfig.SourceMatchField);

        if (!string.IsNullOrEmpty(categoryConfig.UpdateMatchSourceField))
            fields.Add(categoryConfig.UpdateMatchSourceField);

        foreach (var mapping in categoryConfig.FieldMappings)
        {
            foreach (var field in mapping.SourceFields)
                fields.Add(field);

            if (!string.IsNullOrWhiteSpace(mapping.TransformExpression))
            {
                foreach (var field in TransformEngine.ExtractAttributeNames(mapping.TransformExpression))
                    fields.Add(field);
            }
        }

        return fields.ToList();
    }

    private static SyncCategoryConfig GetCategoryConfig(CloudTargetConfig target, string category)
    {
        return category.ToLowerInvariant() switch
        {
            "assets"             => target.Assets,
            "users" or "loanees" => target.Users,
            _ => throw new ArgumentException($"Unknown sync category: {category}", nameof(category))
        };
    }

    private static SourceReadConfig GetSourceReadConfig(CloudSourceConfig source, string category)
    {
        return category.ToLowerInvariant() switch
        {
            "assets"             => source.Assets,
            "users" or "loanees" => source.Users,
            _ => throw new ArgumentException($"Unknown sync category: {category}", nameof(category))
        };
    }

    /// <summary>
    /// Builds an effective AD config by applying per-target overrides on top of the source AD config.
    /// Supports multiple selected OUs or group-based memberOf filters.
    /// </summary>
    private static AdConnectionConfig BuildEffectiveAdConfig(
        AdConnectionConfig sourceAdConfig,
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
            return sourceAdConfig;

        if (isGroupMode)
        {
            var dcIndex = overrides[0].IndexOf("DC=", StringComparison.OrdinalIgnoreCase);
            var domainDn = dcIndex >= 0 ? overrides[0][dcIndex..] : sourceAdConfig.UserSearchBase;

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
                Domain = sourceAdConfig.Domain,
                Server = sourceAdConfig.Server,
                Port = sourceAdConfig.Port,
                UseSsl = sourceAdConfig.UseSsl,
                Username = sourceAdConfig.Username,
                EncryptedPassword = sourceAdConfig.EncryptedPassword,
                ComputerSearchBase = sourceAdConfig.ComputerSearchBase,
                UserSearchBase = domainDn,
                AdditionalUserSearchBases = [],
                ComputerFilter = sourceAdConfig.ComputerFilter,
                UserFilter = memberOfFilter
            };
        }

        var primary = overrides[0];
        var additional = overrides.Skip(1).ToList();

        if (objectType == DirectoryObjectType.Computer)
        {
            return new AdConnectionConfig
            {
                Domain = sourceAdConfig.Domain,
                Server = sourceAdConfig.Server,
                Port = sourceAdConfig.Port,
                UseSsl = sourceAdConfig.UseSsl,
                Username = sourceAdConfig.Username,
                EncryptedPassword = sourceAdConfig.EncryptedPassword,
                ComputerSearchBase = primary,
                UserSearchBase = sourceAdConfig.UserSearchBase,
                AdditionalUserSearchBases = sourceAdConfig.AdditionalUserSearchBases,
                ComputerFilter = hasFilterOverride
                    ? categoryConfig.AdFilterOverride.Trim()
                    : sourceAdConfig.ComputerFilter,
                UserFilter = sourceAdConfig.UserFilter
            };
        }

        return new AdConnectionConfig
        {
            Domain = sourceAdConfig.Domain,
            Server = sourceAdConfig.Server,
            Port = sourceAdConfig.Port,
            UseSsl = sourceAdConfig.UseSsl,
            Username = sourceAdConfig.Username,
            EncryptedPassword = sourceAdConfig.EncryptedPassword,
            ComputerSearchBase = sourceAdConfig.ComputerSearchBase,
            UserSearchBase = primary,
            AdditionalUserSearchBases = additional,
            ComputerFilter = sourceAdConfig.ComputerFilter,
            UserFilter = hasFilterOverride
                ? categoryConfig.AdFilterOverride.Trim()
                : sourceAdConfig.UserFilter
        };
    }

    //  Public static accessors (used by app for test sync preview) 

    /// <summary>
    /// Public accessor for GetRequiredSourceFields, used by the app for test sync preview.
    /// </summary>
    public static List<string> GetRequiredAdAttributesPublic(SyncCategoryConfig categoryConfig)
        => GetRequiredSourceFields(categoryConfig);

    /// <summary>
    /// Public accessor for BuildEffectiveAdConfig, used by the app for test sync preview.
    /// </summary>
    public static AdConnectionConfig BuildEffectiveAdConfigPublic(
        AdConnectionConfig adConfig,
        SyncCategoryConfig categoryConfig,
        DirectoryObjectType objectType)
        => BuildEffectiveAdConfig(adConfig, categoryConfig, objectType);
}