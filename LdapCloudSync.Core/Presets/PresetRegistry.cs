namespace LdapCloudSync.Core.Presets;

using LdapCloudSync.Core.Models;

/// <summary>
/// Registry of all known cloud provider presets. 
/// Presets are immutable templates -- they are never modified at runtime.
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
        ProviderType = "Reftab",
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
            CloudIdField = "id",
            AdMatchField = "cn",
            CloudMatchField = "title",
            DefaultMappings =
            [
                // Top-level Reftab asset fields
                new FieldMapping
                {
                    CloudField = "title",
                    AdAttributes = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "aid",
                    AdAttributes = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "notes",
                    AdAttributes = ["description"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                // Custom category fields (nested into "details" at push time)
                new FieldMapping
                {
                    CloudField = "details.Serial Number",
                    AdAttributes = ["serialNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "details.Operating System",
                    AdAttributes = ["operatingSystem"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "details.OS Version",
                    AdAttributes = ["operatingSystemVersion"],
                    TransformExpression = null,
                    DefaultValue = null
                }
            ]
        },
        Users = new PresetEndpoints
        {
            GetEndpoint = "/loanees",
            PostEndpoint = "/loanees",
            PutEndpoint = "/loanees/{id}",
            ResponseItemsPath = "$",
            CloudIdField = "lnid",
            AdMatchField = "mail",
            CloudMatchField = "email",
            DefaultMappings =
            [
                // Required Reftab loanee fields in order
                new FieldMapping
                {
                    CloudField = "name",
                    AdAttributes = ["displayName"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "email",
                    AdAttributes = ["mail"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "title",
                    AdAttributes = ["title"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "employeeId",
                    AdAttributes = ["employeeID"],
                    TransformExpression = null,
                    DefaultValue = null
                }
                // "disabled" is auto-injected as false by ReftabClient.PreparePushRecord
                // "details" is auto-injected as {} by ReftabClient.PreparePushRecord
                // Additional custom fields can be mapped as "details.FieldName"
            ]
        }
    };

    public static CloudPreset SnipeIt { get; } = new()
    {
        Name = "Snipe-IT",
        ProviderType = "SnipeIT",
        BaseUrl = "https://yourdomain.snipeitapp.com/api/v1",
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
            CloudIdField = "id",
            AdMatchField = "cn",
            CloudMatchField = "name",
            DefaultMappings =
            [
                new FieldMapping
                {
                    CloudField = "name",
                    AdAttributes = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "asset_tag",
                    AdAttributes = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "serial",
                    AdAttributes = ["serialNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "notes",
                    AdAttributes = ["description"],
                    TransformExpression = null,
                    DefaultValue = null
                }
            ]
        },
        Users = new PresetEndpoints
        {
            GetEndpoint = "/users",
            PostEndpoint = "/users",
            PutEndpoint = "/users/{id}",
            ResponseItemsPath = "$.rows",
            CloudIdField = "id",
            AdMatchField = "sAMAccountName",
            CloudMatchField = "username",
            DefaultMappings =
            [
                new FieldMapping
                {
                    CloudField = "first_name",
                    AdAttributes = ["givenName"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "last_name",
                    AdAttributes = ["sn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "username",
                    AdAttributes = ["sAMAccountName"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "email",
                    AdAttributes = ["mail"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "department",
                    AdAttributes = ["department"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "jobtitle",
                    AdAttributes = ["title"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "phone",
                    AdAttributes = ["telephoneNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                }
            ]
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
                CloudIdField = preset.Assets.CloudIdField,
                AdMatchField = preset.Assets.AdMatchField,
                CloudMatchField = preset.Assets.CloudMatchField,
                FieldMappings = preset.Assets.DefaultMappings
                    .Select(m => new FieldMapping
                    {
                        CloudField = m.CloudField,
                        AdAttributes = [.. m.AdAttributes],
                        TransformExpression = m.TransformExpression,
                        DefaultValue = m.DefaultValue
                    })
                    .ToList()
            },
            Users = new SyncCategoryConfig
            {
                GetEndpoint = preset.Users.GetEndpoint,
                PostEndpoint = preset.Users.PostEndpoint,
                PutEndpoint = preset.Users.PutEndpoint,
                ResponseItemsPath = preset.Users.ResponseItemsPath,
                CloudIdField = preset.Users.CloudIdField,
                AdMatchField = preset.Users.AdMatchField,
                CloudMatchField = preset.Users.CloudMatchField,
                FieldMappings = preset.Users.DefaultMappings
                    .Select(m => new FieldMapping
                    {
                        CloudField = m.CloudField,
                        AdAttributes = [.. m.AdAttributes],
                        TransformExpression = m.TransformExpression,
                        DefaultValue = m.DefaultValue
                    })
                    .ToList()
            }
        };
    }

    /// <summary>
    /// Checks if a config still matches its preset origin on core fields.
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