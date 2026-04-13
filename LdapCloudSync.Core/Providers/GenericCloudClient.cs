using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// Generic cloud client for standard REST APIs.
/// Supports Bearer, Basic, ApiKey, and standard HMAC authentication.
/// No provider-specific customizations — pure base implementation.
/// Use this for any API that follows standard REST patterns.
/// </summary>
public sealed class GenericCloudClient : BaseCloudClient
{
    public GenericCloudClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null)
        : base(target, httpClient, logger)
    {
    }

    // All functionality inherited from BaseCloudClient.
    // Override methods here only if you need to customize behavior for a specific "generic" use case.
}