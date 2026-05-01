using LdapCloudSync.Core.Models;

namespace LdapCloudSync.Core.Interfaces;

/// <summary>
/// Abstraction for reading records from a cloud API source.
/// Implemented by the same provider classes that implement ICloudClient,
/// so adding a new provider automatically supports both source and target roles.
/// </summary>
public interface ICloudSourceClient : IDisposable
{
    /// <summary>
    /// Tests connectivity and authentication to the cloud source.
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync();

    /// <summary>
    /// Discovers available field names from the source API response.
    /// Returns the fields and the raw JSON for the editor window.
    /// </summary>
    Task<(IReadOnlyList<string> Fields, string RawResponse)> DiscoverFieldsAsync(string category);

    /// <summary>
    /// Fetches records from the source API.
    /// </summary>
    /// <param name="category">Category to fetch ("assets" or "users").</param>
    /// <param name="filter">Optional filter string appended to the GET URL as a query parameter.</param>
    /// <param name="maxRecords">Limit result count. 0 = all records.</param>
    /// <returns>
    /// Flat string dictionaries (field -> value) compatible with TransformEngine,
    /// and the raw JSON string for the editor window.
    /// </returns>
    Task<(IReadOnlyList<Dictionary<string, string>> Records, string RawJson)> GetRecordsAsync(
        string category,
        string? filter = null,
        int maxRecords = 0);
}