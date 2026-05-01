using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// Factory for creating ICloudSourceClient instances from a CloudSourceConfig.
/// Parallel to CloudClientFactory, but for read (source) operations.
/// The same underlying provider classes are used - they implement both
/// ICloudClient and ICloudSourceClient, so adding one provider covers both roles.
/// </summary>
public static class CloudSourceFactory
{
    /// <summary>
    /// Creates a cloud source client for the given source configuration.
    /// The source config is adapted into the CloudTargetConfig shape that
    /// existing provider constructors expect, with only GET-relevant fields populated.
    /// </summary>
    public static ICloudSourceClient CreateClient(CloudSourceConfig config, ILogger? logger = null)
    {
        if (config.SourceType != SourceType.Cloud)
            throw new InvalidOperationException(
                $"CloudSourceFactory only creates clients for Cloud-type sources. " +
                $"Source '{config.Name}' is type {config.SourceType}.");

        var adapted = AdaptToTargetConfig(config);
        var log = logger ?? Log.Logger;

        log.Information("CloudSourceFactory creating client for source '{Name}', ProviderType: '{ProviderType}'",
            config.Name, config.ProviderType);

        ICloudSourceClient client = config.ProviderType switch
        {
            "Reftab"                  => new ReftabClient(adapted, null, logger),
            "AssetPanda"              => new AssetPandaClient(adapted, null, logger),
            "SolarWinds"              => new SolarWindsClient(adapted, null, logger),
            "SnipeIT"                 => new GenericCloudClient(adapted, null, logger),
            _                         => new GenericCloudClient(adapted, null, logger)
        };

        log.Information("Created source client type: {ClientType}", client.GetType().Name);
        return client;
    }

    /// <summary>
    /// Adapts a CloudSourceConfig into a CloudTargetConfig so existing provider
    /// constructors can be reused without modification.
    /// Only connection and GET-relevant fields are populated.
    /// </summary>
    private static CloudTargetConfig AdaptToTargetConfig(CloudSourceConfig source)
    {
        return new CloudTargetConfig
        {
            Id          = source.Id,
            Name        = source.Name,
            ProviderType = source.ProviderType,
            Connection  = source.Connection,

            Assets = new SyncCategoryConfig
            {
                Enabled           = source.Assets.Enabled,
                GetEndpoint       = source.Assets.GetEndpoint,
                ResponseItemsPath = source.Assets.ResponseItemsPath,
                SourceMatchField  = source.Assets.SourceIdField,
                TargetCategoryId  = source.Assets.SourceCategoryId
            },

            Users = new SyncCategoryConfig
            {
                Enabled           = source.Users.Enabled,
                GetEndpoint       = source.Users.GetEndpoint,
                ResponseItemsPath = source.Users.ResponseItemsPath,
                SourceMatchField  = source.Users.SourceIdField
            },

            ApAccountId          = source.ApAccountId,
            ApModuleId           = source.ApModuleId,
            ApAssetsCollectionId = source.ApAssetsCollectionId,
            ApUsersCollectionId  = source.ApUsersCollectionId
        };
    }
}