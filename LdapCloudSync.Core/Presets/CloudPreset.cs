namespace LdapCloudSync.Core.Presets;

using LdapCloudSync.Core.Models;

/// <summary>
/// Defines a known cloud provider preset (template).
/// Presets are immutable -- they define defaults that populate a new CloudTargetConfig.
/// </summary>
public sealed class CloudPreset
{
    public required string Name { get; init; }
    public required string BaseUrl { get; init; }
    public required AuthType AuthType { get; init; }
    public string ApiKeyHeader { get; init; } = "Authorization";
    public string ApiKeyFormat { get; init; } = "Bearer {key}";
    public string HmacAlgorithm { get; init; } = "HMACSHA256";
    public string ContentType { get; init; } = "application/json";

    public required PresetEndpoints Assets { get; init; }
    public required PresetEndpoints Users { get; init; }

    /// <summary>
    /// Fields that, if changed by the user, cause the target to switch to "Custom".
    /// These are the "structural" / "core" fields that define the preset identity.
    /// Credential fields (ApiKey, ApiSecret) do NOT trigger the switch.
    /// </summary>
    public IReadOnlySet<string> CoreFields { get; init; } = new HashSet<string>
    {
        nameof(CloudConnectionConfig.BaseUrl),
        nameof(CloudConnectionConfig.AuthType),
        nameof(CloudConnectionConfig.HmacAlgorithm),
        nameof(CloudConnectionConfig.ContentType)
    };
}

public sealed class PresetEndpoints
{
    public string GetEndpoint { get; init; } = string.Empty;
    public string PostEndpoint { get; init; } = string.Empty;
    public string PutEndpoint { get; init; } = string.Empty;
    public string ResponseItemsPath { get; init; } = "$";
    public string CloudIdField { get; init; } = "id";

    /// <summary>
    /// Default AD attribute to match records (e.g., "cn" for computers, "sAMAccountName" for users).
    /// </summary>
    public string AdMatchField { get; init; } = string.Empty;

    /// <summary>
    /// Default cloud field to match against the AD match field.
    /// </summary>
    public string CloudMatchField { get; init; } = string.Empty;

    /// <summary>
    /// Default field mappings pre-populated for this preset.
    /// </summary>
    public List<FieldMapping> DefaultMappings { get; init; } = [];
}