namespace LdapCloudSync.Core.Presets;

using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Presets;
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
        Register(AssetPanda);
        Register(SolarWinds);
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
            CloudIdField = "aid",
            AdMatchField = "cn",
            CloudMatchField = "title",
            UpdateMatchAdField = string.Empty,
            UpdateMatchCloudField = string.Empty,
            SecondaryUpdateMatchAdField = string.Empty,
            SecondaryUpdateMatchCloudField = string.Empty,
            DefaultMappings =
            [
                // Top-level Reftab asset fields
                new FieldMapping
                {
                    CloudField = "title",
                    SourceFields = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "aid",
                    SourceFields = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "notes",
                    SourceFields = ["description"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                // Custom category fields (nested into "details" at push time)
                new FieldMapping
                {
                    CloudField = "details.Serial Number",
                    SourceFields = ["serialNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "details.Operating System",
                    SourceFields = ["operatingSystem"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "details.OS Version",
                    SourceFields = ["operatingSystemVersion"],
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
            UpdateMatchAdField = "mail",
            UpdateMatchCloudField = "email",
            DefaultMappings =
            [
                new FieldMapping
                {
                    CloudField = "name",
                    SourceFields = ["displayName"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "email",
                    SourceFields = ["mail"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "title",
                    SourceFields = ["title"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "employeeId",
                    SourceFields = ["employeeID"],
                    TransformExpression = null,
                    DefaultValue = null
                }
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
            UpdateMatchAdField = "cn",
            UpdateMatchCloudField = "name",
            DefaultMappings =
            [
                new FieldMapping
                {
                    CloudField = "name",
                    SourceFields = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "asset_tag",
                    SourceFields = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "serial",
                    SourceFields = ["serialNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "notes",
                    SourceFields = ["description"],
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
            UpdateMatchAdField = "sAMAccountName",
            UpdateMatchCloudField = "username",
            DefaultMappings =
            [
                new FieldMapping
                {
                    CloudField = "first_name",
                    SourceFields = ["givenName"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "last_name",
                    SourceFields = ["sn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "username",
                    SourceFields = ["sAMAccountName"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "email",
                    SourceFields = ["mail"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "department",
                    SourceFields = ["department"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "jobtitle",
                    SourceFields = ["title"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "phone",
                    SourceFields = ["telephoneNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                }
            ]
        }
    };

    public static CloudPreset AssetPanda { get; } = new()
    {
        Name = "Asset Panda",
        ProviderType = "AssetPanda",
        BaseUrl = "https://api.assetpanda.app",
        AuthType = AuthType.ApiKey,
        ContentType = "application/json",
        Assets = new PresetEndpoints
        {
            // Endpoints are dynamic - built from Account + Module + Collection IDs at runtime
            GetEndpoint = "",
            PostEndpoint = "",
            PutEndpoint = "",
            ResponseItemsPath = "$.data",
            CloudIdField = "id",
            AdMatchField = "cn",
            CloudMatchField = "",
            UpdateMatchAdField = "cn",
            UpdateMatchCloudField = "",
            DefaultMappings = []
        },
        Users = new PresetEndpoints
        {
            GetEndpoint = "",
            PostEndpoint = "",
            PutEndpoint = "",
            ResponseItemsPath = "$.data",
            CloudIdField = "id",
            AdMatchField = "displayName",
            CloudMatchField = "",
            UpdateMatchAdField = "displayName",
            UpdateMatchCloudField = "",
            DefaultMappings = []
        }
    };

    public static CloudPreset SolarWinds { get; } = new()
    {
        Name = "SolarWinds",
        ProviderType = "SolarWinds",
        BaseUrl = "https://api.samanage.com",
        AuthType = AuthType.BearerToken,
        ApiKeyHeader = "Authorization",
        ApiKeyFormat = "Bearer {key}",
        ContentType = "application/json",
        Assets = new PresetEndpoints
        {
            GetEndpoint = "/hardwares.json",
            PostEndpoint = "/hardwares.json",
            PutEndpoint = "/hardwares/{id}.json",
            ResponseItemsPath = "$",
            CloudIdField = "id",
            AdMatchField = "cn",
            CloudMatchField = "name",
            UpdateMatchAdField = "cn",
            UpdateMatchCloudField = "name",
            DefaultMappings =
            [
                new FieldMapping
                {
                    CloudField = "name",
                    SourceFields = ["cn"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "bio.ssn",
                    SourceFields = ["serialNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "description",
                    SourceFields = ["description"],
                    TransformExpression = null,
                    DefaultValue = null
                }
            ]
        },
        Users = new PresetEndpoints
        {
            GetEndpoint = "/users.json",
            PostEndpoint = "/users.json",
            PutEndpoint = "/users/{id}.json",
            ResponseItemsPath = "$",
            CloudIdField = "id",
            AdMatchField = "mail",
            CloudMatchField = "email",
            UpdateMatchAdField = "mail",
            UpdateMatchCloudField = "email",
            DefaultMappings =
            [
                new FieldMapping
                {
                    CloudField = "name",
                    SourceFields = ["displayName"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "email",
                    SourceFields = ["mail"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "title",
                    SourceFields = ["title"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "phone",
                    SourceFields = ["telephoneNumber"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "mobile_phone",
                    SourceFields = ["mobile"],
                    TransformExpression = null,
                    DefaultValue = null
                },
                new FieldMapping
                {
                    CloudField = "department",
                    SourceFields = ["department"],
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
                UpdateMatchAdField = preset.Assets.UpdateMatchAdField,
                UpdateMatchCloudField = preset.Assets.UpdateMatchCloudField,
                SecondaryUpdateMatchSourceField = preset.Assets.SecondaryUpdateMatchAdField,
                SecondaryUpdateMatchCloudField = preset.Assets.SecondaryUpdateMatchCloudField,
                FieldMappings = preset.Assets.DefaultMappings
                    .Select(m => new FieldMapping
                    {
                        CloudField = m.CloudField,
                        SourceFields = [.. m.SourceFields],
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
                UpdateMatchAdField = preset.Users.UpdateMatchAdField,
                UpdateMatchCloudField = preset.Users.UpdateMatchCloudField,
                SecondaryUpdateMatchSourceField = preset.Users.SecondaryUpdateMatchAdField,
                SecondaryUpdateMatchCloudField = preset.Users.SecondaryUpdateMatchCloudField,
                FieldMappings = preset.Users.DefaultMappings
                    .Select(m => new FieldMapping
                    {
                        CloudField = m.CloudField,
                        SourceFields = [.. m.SourceFields],
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