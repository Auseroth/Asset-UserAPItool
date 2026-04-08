namespace LdapCloudSync.Core.Interfaces;

/// <summary>
/// Abstracts directory (LDAP/AD) operations for future extensibility.
/// </summary>
public interface IDirectoryProvider
{
    /// <summary>
    /// Tests connectivity with the configured credentials.
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync();

    /// <summary>
    /// Retrieves all available attribute names for the given object type.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableAttributesAsync(DirectoryObjectType objectType);

    /// <summary>
    /// Queries directory objects with the specified attributes.
    /// </summary>
    /// <param name="objectType">Computers or Users.</param>
    /// <param name="attributes">Which attributes to fetch.</param>
    /// <param name="maxResults">Limit results (0 = no limit). Used for test mode.</param>
    Task<IReadOnlyList<Dictionary<string, string>>> QueryAsync(
        DirectoryObjectType objectType,
        IEnumerable<string> attributes,
        int maxResults = 0);
}

public enum DirectoryObjectType
{
    Computer,
    User
}