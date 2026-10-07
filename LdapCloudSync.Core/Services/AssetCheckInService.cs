using System.Text;
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
        bool isTurningInAsset,
        string? firstName,
        string? lastName,
        string? phoneNumber,
        string? assetTag,
        string? serialNumber,
        string? requestDescription,
        CancellationToken cancellationToken = default)
    {
        var config = _configService.Current;

        var cleanedFirstName = firstName?.Trim() ?? string.Empty;
        var cleanedLastName = lastName?.Trim() ?? string.Empty;
        var cleanedPhoneNumber = phoneNumber?.Trim() ?? string.Empty;
        var cleanedAssetTag = assetTag?.Trim() ?? string.Empty;
        var cleanedSerialNumber = serialNumber?.Trim() ?? string.Empty;
        var cleanedDescription = requestDescription?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(cleanedFirstName) || string.IsNullOrWhiteSpace(cleanedLastName))
            return AssetCheckInResult.Failure("First name and last name are required.");

        if (string.IsNullOrWhiteSpace(cleanedPhoneNumber))
            return AssetCheckInResult.Failure("Phone number is required.");

        if (isTurningInAsset && string.IsNullOrWhiteSpace(cleanedAssetTag) && string.IsNullOrWhiteSpace(cleanedSerialNumber))
            return AssetCheckInResult.Failure("Enter an asset tag or SN before submitting a turn-in.");

        if (string.IsNullOrWhiteSpace(cleanedDescription))
            return AssetCheckInResult.Failure("Please enter a request/issue description before submitting.");

        var transformedDescription = BuildDescription(cleanedFirstName, cleanedLastName, cleanedPhoneNumber, cleanedDescription);
        var sourceRecord = BuildSourceRecord(
            isTurningInAsset,
            cleanedFirstName,
            cleanedLastName,
            cleanedPhoneNumber,
            cleanedAssetTag,
            cleanedSerialNumber,
            transformedDescription);

        await SaveKioskSourceRecordAsync(sourceRecord);

        var kioskSourceId = $"{SourceFileService.FileSourcePrefix}{KioskConfig.AssetCheckInSourceFileName}";
        var eligibleTargets = config.CloudTargets
            .Where(t => t.Enabled)
            .Where(t => string.Equals(t.SourceId, kioskSourceId, StringComparison.OrdinalIgnoreCase))
            .Where(t => IsEligibleTarget(t, isTurningInAsset))
            .ToList();

        if (eligibleTargets.Count == 0)
        {
            return isTurningInAsset
                ? AssetCheckInResult.Failure("No enabled API targets are configured for Check-in Kiosk asset turn-ins.")
                : AssetCheckInResult.Failure("No enabled Email targets are configured for Check-in Kiosk request tickets.");
        }

        var aggregate = new SyncResult();
        var targetNames = new List<string>();

        foreach (var target in eligibleTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            targetNames.Add(target.Name);

            try
            {
                var targetResult = await PushToTargetAsync(
                    target,
                    isTurningInAsset,
                    cleanedAssetTag,
                    cleanedDescription,
                    transformedDescription,
                    sourceRecord);

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
            var modeText = isTurningInAsset ? "asset turn-in" : "support ticket";
            var message = $"Submitted {modeText} to {eligibleTargets.Count} target(s): {string.Join(", ", targetNames)}.";
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
            Message = $"Submission failed. {failureText}",
            TargetResponseBody = aggregate.LastSuccessResponseBody,
            SyncResult = aggregate,
            TargetName = string.Join(", ", targetNames)
        };
    }

    private async Task<SyncResult> PushToTargetAsync(
        CloudTargetConfig target,
        bool isTurningInAsset,
        string cleanedAssetTag,
        string requestDescription,
        string transformedDescription,
        Dictionary<string, string> sourceRecord)
    {
        if (!isTurningInAsset)
        {
            var emailRecord = BuildEmailTicketRecord(sourceRecord, requestDescription);
            using var emailClient = CloudClientFactory.CreateClient(target, _log);
            return await emailClient.PushRecordsAsync("tickets", [emailRecord]);
        }

        if (string.Equals(target.ProviderType, "Reftab", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(cleanedAssetTag))
        {
            using var reftabClient = new ReftabClient(target, null, _log);
            return await reftabClient.UpdateAssetMaintenanceByAidAsync(
                cleanedAssetTag,
                transformedDescription,
                maintenanceTriggerValue: "Yes");
        }

        var engine = new TransformEngine(_log);
        var cloudRecord = engine.TransformSingle(sourceRecord, target.Assets.FieldMappings);
        if (cloudRecord.Count == 0)
        {
            return new SyncResult
            {
                Failed = 1,
                Errors = [$"No mapped asset fields were produced from kiosk input."]
            };
        }

        using var client = CloudClientFactory.CreateClient(target, _log);
        return await client.PushRecordsAsync("assets", [CloneCloudRecord(cloudRecord)]);
    }

    private static bool IsEligibleTarget(CloudTargetConfig target, bool isTurningInAsset)
    {
        var isEmailTarget = string.Equals(target.ProviderType, "Email", StringComparison.OrdinalIgnoreCase);
        if (!isTurningInAsset)
            return isEmailTarget;

        if (isEmailTarget)
            return false;

        return target.Assets.Enabled
            && !target.Users.Enabled
            && (string.Equals(target.ProviderType, "Reftab", StringComparison.OrdinalIgnoreCase)
                || target.Assets.FieldMappings.Count > 0);
    }

    private static string BuildDescription(
        string firstName,
        string lastName,
        string phoneNumber,
        string requestDescription)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Requester: {firstName} {lastName}");
        builder.AppendLine($"Phone: {phoneNumber}");
        builder.AppendLine();
        builder.Append(requestDescription);
        return builder.ToString().Trim();
    }

    private static Dictionary<string, string> BuildSourceRecord(
        bool isTurningInAsset,
        string firstName,
        string lastName,
        string phoneNumber,
        string assetTag,
        string serialNumber,
        string description)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["first_name"] = firstName,
            ["last_name"] = lastName,
            ["phone_number"] = phoneNumber,
            ["asset_tag"] = assetTag,
            ["serial_number"] = serialNumber,
            ["check_in_note"] = description,
            ["maintenance_trigger"] = isTurningInAsset ? "Yes" : "No"
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

    private static Dictionary<string, object> BuildEmailTicketRecord(
        Dictionary<string, string> sourceRecord,
        string requestDescription)
    {
        sourceRecord.TryGetValue("first_name", out var firstName);
        sourceRecord.TryGetValue("last_name", out var lastName);
        sourceRecord.TryGetValue("phone_number", out var phoneNumber);

        var requesterName = string.Join(" ", new[] { firstName, lastName }.Where(v => !string.IsNullOrWhiteSpace(v))).Trim();
        var safeRequester = string.IsNullOrWhiteSpace(requesterName) ? "Unknown Requester" : requesterName;

        var body = new StringBuilder();
        body.AppendLine("New kiosk support request");
        body.AppendLine();
        body.AppendLine($"Requester: {safeRequester}");
        body.AppendLine($"Phone: {phoneNumber}");
        body.AppendLine();
        body.AppendLine("Request/Issue Description:");
        body.AppendLine(requestDescription);

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["email_subject"] = $"Kiosk Request - {safeRequester}",
            ["email_body"] = body.ToString().Trim()
        };
    }
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
