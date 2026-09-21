using System.Text.Json;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Providers;
using Serilog;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Builds and submits an on-demand asset check-in payload using the same
/// transform and cloud push pipeline as the regular sync flow.
/// </summary>
public sealed class AssetCheckInService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ConfigService _configService;
    private readonly SourceFileService _sourceFileService;
    private readonly ILogger _log;

    public AssetCheckInService(ConfigService configService, ILogger? logger = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _sourceFileService = new SourceFileService(logger);
        _log = logger ?? Log.Logger;
    }

    public async Task<AssetCheckInResult> CheckInAsync(
        string? assetTag,
        string? serialNumber,
        string? checkInReason,
        CancellationToken cancellationToken = default)
    {
        var config = _configService.Current;

        var cleanedAssetTag = assetTag?.Trim() ?? string.Empty;
        var cleanedSerialNumber = serialNumber?.Trim() ?? string.Empty;
        var cleanedReason = checkInReason?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cleanedAssetTag) && string.IsNullOrWhiteSpace(cleanedSerialNumber))
            return AssetCheckInResult.Failure("Enter an asset tag or serial number before checking in.");

        if (string.IsNullOrWhiteSpace(cleanedReason))
            return AssetCheckInResult.Failure("Please enter a check-in note before submitting.");

        var sourceRecord = BuildSourceRecord(cleanedAssetTag, cleanedSerialNumber, cleanedReason);
        await SaveKioskSourceRecordAsync(sourceRecord);

        var kioskSourceId = $"{SourceFileService.FileSourcePrefix}{KioskConfig.AssetCheckInSourceFileName}";
        var eligibleTargets = config.CloudTargets
            .Where(t => t.Enabled)
            .Where(t => string.Equals(t.SourceId, kioskSourceId, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Assets.Enabled)
            .Where(t => !t.Users.Enabled)
            .Where(t => string.Equals(t.ProviderType, "Reftab", StringComparison.OrdinalIgnoreCase)
                || t.Assets.FieldMappings.Count > 0)
            .ToList();

        if (eligibleTargets.Count == 0)
            return AssetCheckInResult.Failure("No enabled cloud targets are configured to use source 'Check-in Kiosk'.");

        var aggregate = new SyncResult();
        var targetNames = new List<string>();

        foreach (var target in eligibleTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            targetNames.Add(target.Name);

            try
            {
                SyncResult targetResult;

                // Reftab maintenance kickoff mode:
                // resolve the asset by aid, preserve the full asset payload, modify only
                // maintenance fields, then PUT the full payload back.
                if (string.Equals(target.ProviderType, "Reftab", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(cleanedAssetTag))
                {
                    using var reftabClient = new ReftabClient(target, null, _log);
                    targetResult = await reftabClient.UpdateAssetMaintenanceByAidAsync(
                        cleanedAssetTag,
                        cleanedReason,
                        maintenanceTriggerValue: "Yes");
                }
                else
                {
                    var engine = new TransformEngine(_log);
                    var cloudRecord = engine.TransformSingle(sourceRecord, target.Assets.FieldMappings);
                    if (cloudRecord.Count == 0)
                    {
                        aggregate.Failed++;
                        aggregate.Errors.Add($"[{target.Name}] No mapped asset fields were produced from kiosk input.");
                        continue;
                    }

                    using var client = CloudClientFactory.CreateClient(target, _log);
                    targetResult = await client.PushRecordsAsync("assets", [CloneCloudRecord(cloudRecord)]);
                }

                aggregate.Created += targetResult.Created;
                aggregate.Updated += targetResult.Updated;
                aggregate.Skipped += targetResult.Skipped;
                aggregate.Failed += targetResult.Failed;
                aggregate.Errors.AddRange(targetResult.Errors.Select(e => $"[{target.Name}] {e}"));

                if (!string.IsNullOrWhiteSpace(targetResult.LastSuccessResponseBody))
                    aggregate.LastSuccessResponseBody = targetResult.LastSuccessResponseBody;
            }
            catch (Exception ex)
            {
                aggregate.Failed++;
                aggregate.Errors.Add($"[{target.Name}] {ex.Message}");
            }
        }

        var successfulWrites = aggregate.Created + aggregate.Updated;
        if (successfulWrites > 0 && aggregate.Failed == 0)
        {
            var message = $"Check-in complete. Sent to {eligibleTargets.Count} target(s): {string.Join(", ", targetNames)}.";
            return new AssetCheckInResult
            {
                Success = true,
                Message = message,
                TargetResponseBody = aggregate.LastSuccessResponseBody,
                SyncResult = aggregate,
                TargetName = string.Join(", ", targetNames)
            };
        }

        var failureText = aggregate.Errors.Count == 0
            ? "No successful create or update was reported by the target(s)."
            : string.Join(Environment.NewLine, aggregate.Errors.Distinct(StringComparer.OrdinalIgnoreCase));

        return new AssetCheckInResult
        {
            Success = false,
            Message = $"Check-in failed. {failureText}",
            TargetResponseBody = aggregate.LastSuccessResponseBody,
            SyncResult = aggregate,
            TargetName = string.Join(", ", targetNames)
        };
    }

    private static Dictionary<string, string> BuildSourceRecord(
        string assetTag,
        string serialNumber,
        string checkInReason)
    {
        // Keep a stable kiosk payload template so source-field discovery always
        // shows these fields even when a value is blank in the latest check-in.
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["asset_tag"] = assetTag,
            ["serial_number"] = serialNumber,
            ["check_in_note"] = checkInReason,
            ["maintenance_trigger"] = "Yes"
        };
    }

    private async Task SaveKioskSourceRecordAsync(Dictionary<string, string> sourceRecord)
    {
        var envelope = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["assets"] = new[] { sourceRecord },
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O")
        };

        var rawJson = JsonSerializer.Serialize(envelope, JsonOptions);
        await _sourceFileService.SaveRawJsonAsync(KioskConfig.AssetCheckInSourceFileName, rawJson);
    }

    private static Dictionary<string, object> CloneCloudRecord(Dictionary<string, object> cloudRecord)
        => cloudRecord.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    }

    public sealed class AssetCheckInResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TargetResponseBody { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public SyncResult SyncResult { get; set; } = new();

    public static AssetCheckInResult Failure(string message) => new()
    {
        Success = false,
        Message = message
    };
}
