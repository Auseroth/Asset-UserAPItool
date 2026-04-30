using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// Factory for creating the appropriate cloud client based on provider type.
/// </summary>
public static class CloudClientFactory
{
    /// <summary>
    /// Creates a cloud client instance based on the target configuration.
    /// Routes to provider-specific implementations or falls back to generic client.
    /// </summary>
    public static ICloudClient CreateClient(CloudTargetConfig config, ILogger? logger = null)
    {
        var log = logger ?? Log.Logger;

        log.Information("CloudClientFactory creating client for ProviderType: '{ProviderType}'",
            config.ProviderType ?? "(null)");

        ICloudClient client = config.ProviderType switch
        {
            "Reftab" => new ReftabClient(config, null, logger),
            "AssetPanda" => new AssetPandaClient(config, null, logger),
            "SolarWinds" => new SolarWindsClient(config, null, logger),
            "SnipeIT" => new GenericCloudClient(config, null, logger),
            _ => new GenericCloudClient(config, null, logger)
        };

        log.Information("Created client type: {ClientType}", client.GetType().Name);
        return client;
    }
}