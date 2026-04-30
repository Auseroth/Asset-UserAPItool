namespace LdapCloudSync.Core.Models;

/// <summary>
/// Available cloud provider types for XAML binding.
/// </summary>
public static class ProviderTypeValues
{
    public static string[] All { get; } =
    [
        "Reftab",
        "AssetPanda",
        "SolarWinds",
        "SnipeIT",
        "Generic (Standard REST)"
    ];
}