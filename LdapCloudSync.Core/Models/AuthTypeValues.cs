namespace LdapCloudSync.Core.Models;

/// <summary>
/// Static helper to expose all AuthType values for XAML ComboBox binding.
/// </summary>
public static class AuthTypeValues
{
    public static AuthType[] All { get; } = Enum.GetValues<AuthType>();
}