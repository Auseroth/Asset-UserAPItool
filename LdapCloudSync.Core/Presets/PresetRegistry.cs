namespace LdapCloudSync.Core.Presets;

using LdapCloudSync.Core.Models;

/// <summary>
/// Registry of all known cloud provider presets. 
/// Presets are immutable templates — they are never modified at runtime.
/// </summary>
public static class PresetRegistry
{
    private static readonly Dictionary<string, CloudPreset> _presets = new(StringComparer.OrdinalIgnoreCase);

    static PresetRegistry()
    {
        Register(Reftab);
        Register(SnipeIt);
    }

    public static CloudPreset Reftab { get; } = new()
    {
        Name = "Reftab",
        BaseUrl = "https://www.reftab.com/api",
        AuthType = AuthType.Hmac,
        HmacAlgorithm = "HMACSHA256",
        ContentType = "application/json",
        Assets = new PresetEndpoints
        {
            GetEndpoint = "/assets",
            PostEndpoint = "/assets",
            PutEndpoint = "/assets/{id}",
            ResponseItemsPath = "$",
            CloudIdField = "id"
        },
        Users = new PresetEndpoints
        {
            GetEndpoint = "/loanees",
            PostEndpoint = "/loanees",
            PutEndpoint = "/loanees/{id}",
            ResponseItemsPath = "$",
            CloudIdField = "id"
        }
    };

    public static CloudPreset SnipeIt { get; } = new()
    {
        Name = "Snipe-IT",
        BaseUrl = "https://your-instance.snipeitapp.com/api/v1",
        AuthType = AuthType.BearerToken,
        ApiKeyHeader = "Authorization",
        ApiKeyFormat = "Bearer {key}",
        ContentType = "application/json",
        Assets = new PresetEndpoints
        {
            GetEndpoint = "/hardware",
            PostEndpoint = "/hardware",
            PutEndpoint = "/hardware/{id}",
            ResponseItemsPath = "$.rows",
            CloudIdField = "id"
        },
        Users = new PresetEndpoints
        {
            GetEndpoint = "/users",
            PostEndpoint = "/users",
            PutEndpoint = "/users/{id}",
            ResponseItemsPath = "$.rows",
            CloudIdField = "id"
        }
    };

    public static void Register(CloudPreset preset) => _presets[preset.Name] = preset;

    public static CloudPreset? GetByName(string name) =>
        _presets.TryGetValue(name, out var preset) ? preset : null;

    public static IReadOnlyCollection<CloudPreset> GetAll() => _presets.Values;

    public static IReadOnlyCollection<string> GetAllNames() => _presets.Keys;

    /// <summary>
    /// Creates a new CloudTargetConfig pre-populated from a preset template.
    /// </summary>
    public static CloudTargetConfig CreateFromPreset(CloudPreset preset)
    {
        return new CloudTargetConfig
        {
            Name = preset.Name,
            PresetOrigin = preset.Name,
            Connection = new CloudConnectionConfig
            {
                BaseUrl = preset.BaseUrl,
                AuthType = preset.AuthType,
                ApiKeyHeader = preset.ApiKeyHeader,
                ApiKeyFormat = preset.ApiKeyFormat,
                HmacAlgorithm = preset.HmacAlgorithm,
                ContentType = preset.ContentType
            },
            Assets = new SyncCategoryConfig
            {
                GetEndpoint = preset.Assets.GetEndpoint,
                PostEndpoint = preset.Assets.PostEndpoint,
                PutEndpoint = preset.Assets.PutEndpoint,
                ResponseItemsPath = preset.Assets.ResponseItemsPath,
                CloudIdField = preset.Assets.CloudIdField
            },
            Users = new SyncCategoryConfig
            {
                GetEndpoint = preset.Users.GetEndpoint,
                PostEndpoint = preset.Users.PostEndpoint,
                PutEndpoint = preset.Users.PutEndpoint,
                ResponseItemsPath = preset.Users.ResponseItemsPath,
                CloudIdField = preset.Users.CloudIdField
            }
        };
    }

    /// <summary>
    /// Checks if a config still matches its preset origin on core fields.
    /// If not, it should be labeled "Custom".
    /// </summary>
    public static bool MatchesPreset(CloudTargetConfig config)
    {
        var preset = GetByName(config.PresetOrigin);
        if (preset is null || config.PresetOrigin is "Generic" or "Custom")
            return false;

        return config.Connection.BaseUrl == preset.BaseUrl
            && config.Connection.AuthType == preset.AuthType
            && config.Connection.HmacAlgorithm == preset.HmacAlgorithm
            && config.Connection.ContentType == preset.ContentType;
    }
}