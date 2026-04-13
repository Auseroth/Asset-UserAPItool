namespace LdapCloudSync.Core.Models;

/// <summary>
/// Helper class for binding AuthType enum values in XAML.
/// </summary>
public static class AuthTypeValues
{
    public static AuthType[] All { get; } = 
    [
        AuthType.BearerToken,
        AuthType.ApiKey,
        AuthType.BasicAuth,
        AuthType.Hmac
    ];
}