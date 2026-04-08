using System.DirectoryServices;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Services;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// IDirectoryProvider implementation for local Windows Active Directory using System.DirectoryServices.
/// </summary>
public sealed class ActiveDirectoryProvider : IDirectoryProvider, IDisposable
{
    private readonly AdConnectionConfig _config;
    private readonly ILogger _log;

    public ActiveDirectoryProvider(AdConnectionConfig config, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = logger ?? Log.Logger;
    }

    public Task<(bool Success, string Message)> TestConnectionAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                using var entry = CreateDirectoryEntry(string.Empty);
                _ = entry.NativeObject;
                return (true, $"Successfully connected to {_config.Server ?? _config.Domain}.");
            }
            catch (DirectoryServicesCOMException ex)
            {
                _log.Error(ex, "AD connection test failed");
                return (false, $"Connection failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "AD connection test failed with unexpected error");
                return (false, $"Unexpected error: {ex.Message}");
            }
        });
    }

    public Task<IReadOnlyList<string>> GetAvailableAttributesAsync(DirectoryObjectType objectType)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var searchBase = objectType == DirectoryObjectType.Computer
                ? _config.ComputerSearchBase
                : _config.UserSearchBase;

            var filter = objectType == DirectoryObjectType.Computer
                ? _config.ComputerFilter
                : _config.UserFilter;

            using var entry = CreateDirectoryEntry(searchBase);
            using var searcher = new DirectorySearcher(entry)
            {
                Filter = filter,
                SizeLimit = 1,
                PageSize = 1
            };

            var result = searcher.FindOne();
            if (result is null)
            {
                _log.Warning("No {ObjectType} objects found for attribute discovery", objectType);
                return GetCommonAttributes(objectType);
            }

            var attributes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string attrName in result.Properties.PropertyNames!)
            {
                attributes.Add(attrName);
            }

            _log.Information("Discovered {Count} attributes for {ObjectType}", attributes.Count, objectType);
            return attributes.ToList();
        });
    }

    public Task<IReadOnlyList<Dictionary<string, string>>> QueryAsync(
        DirectoryObjectType objectType,
        IEnumerable<string> attributes,
        int maxResults = 0)
    {
        return Task.Run<IReadOnlyList<Dictionary<string, string>>>(() =>
        {
            var attrList = attributes.ToList();

            // Determine all search bases for this object type
            var searchBases = GetSearchBases(objectType);
            var filter = objectType == DirectoryObjectType.Computer
                ? _config.ComputerFilter
                : _config.UserFilter;

            var allRecords = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var dedupeKey = objectType == DirectoryObjectType.Computer ? "cn" : "sAMAccountName";

            // Ensure we request the dedupe key
            var requestAttrs = attrList.Contains(dedupeKey, StringComparer.OrdinalIgnoreCase)
                ? attrList
                : [.. attrList, dedupeKey];

            foreach (var searchBase in searchBases)
            {
                _log.Information("Querying {ObjectType} from search base: {SearchBase}", objectType, searchBase);

                try
                {
                    var records = QuerySingleBase(searchBase, filter, requestAttrs, maxResults);

                    foreach (var record in records)
                    {
                        var key = record.GetValueOrDefault(dedupeKey, "");
                        if (!string.IsNullOrEmpty(key) && !allRecords.ContainsKey(key))
                        {
                            allRecords[key] = record;
                        }
                    }

                    _log.Information("Retrieved {Count} records from {SearchBase} ({Total} unique total)",
                        records.Count, searchBase, allRecords.Count);

                    // If we have a max and we've hit it, stop querying more bases
                    if (maxResults > 0 && allRecords.Count >= maxResults)
                        break;
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to query search base {SearchBase}, continuing with next", searchBase);
                }
            }

            var result = allRecords.Values.ToList();

            // Trim to maxResults if needed
            if (maxResults > 0 && result.Count > maxResults)
                result = result.Take(maxResults).ToList();

            _log.Information("Queried {Count} unique {ObjectType} records from {BaseCount} search base(s)",
                result.Count, objectType, searchBases.Count);

            return result;
        });
    }

    /// <summary>
    /// Returns all search bases for the given object type.
    /// For users, includes AdditionalUserSearchBases.
    /// </summary>
    private List<string> GetSearchBases(DirectoryObjectType objectType)
    {
        var bases = new List<string>();

        var primary = objectType == DirectoryObjectType.Computer
            ? _config.ComputerSearchBase
            : _config.UserSearchBase;

        if (!string.IsNullOrWhiteSpace(primary))
            bases.Add(primary);

        // Only users support multiple search bases
        if (objectType == DirectoryObjectType.User && _config.AdditionalUserSearchBases.Count > 0)
        {
            foreach (var additional in _config.AdditionalUserSearchBases)
            {
                var trimmed = additional.Trim();
                if (!string.IsNullOrEmpty(trimmed) &&
                    !bases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    bases.Add(trimmed);
                }
            }
        }

        return bases;
    }

    /// <summary>
    /// Queries a single search base and returns raw records.
    /// </summary>
    private List<Dictionary<string, string>> QuerySingleBase(
        string searchBase,
        string filter,
        List<string> attrList,
        int maxResults)
    {
        using var entry = CreateDirectoryEntry(searchBase);
        using var searcher = new DirectorySearcher(entry)
        {
            Filter = filter,
            PageSize = 1000
        };

        if (maxResults > 0)
            searcher.SizeLimit = maxResults;

        foreach (var attr in attrList)
        {
            searcher.PropertiesToLoad.Add(attr);
        }

        var results = new List<Dictionary<string, string>>();

        using var searchResults = searcher.FindAll();
        foreach (SearchResult result in searchResults)
        {
            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var attr in attrList)
            {
                if (result.Properties.Contains(attr) && result.Properties[attr].Count > 0)
                {
                    var value = result.Properties[attr][0];
                    record[attr] = ConvertAdValue(value);
                }
                else
                {
                    record[attr] = string.Empty;
                }
            }

            results.Add(record);

            if (maxResults > 0 && results.Count >= maxResults)
                break;
        }

        return results;
    }

    /// <summary>
    /// Converts an AD property value to a string representation.
    /// Handles byte arrays (GUIDs, SIDs), DateTime, and other types.
    /// </summary>
    private static string ConvertAdValue(object value)
    {
        return value switch
        {
            byte[] bytes when bytes.Length == 16 => new Guid(bytes).ToString(),
            byte[] bytes => Convert.ToBase64String(bytes),
            DateTime dt => dt.ToString("o"),
            long fileTime when fileTime > 0 => TryConvertFileTime(fileTime),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string TryConvertFileTime(long fileTime)
    {
        try
        {
            return DateTime.FromFileTimeUtc(fileTime).ToString("o");
        }
        catch
        {
            return fileTime.ToString();
        }
    }

    /// <summary>
    /// Returns commonly used AD attributes as a fallback when discovery finds no objects.
    /// </summary>
    private static List<string> GetCommonAttributes(DirectoryObjectType objectType)
    {
        var common = new List<string>
        {
            "cn", "distinguishedName", "name", "description",
            "whenCreated", "whenChanged", "objectGUID"
        };

        if (objectType == DirectoryObjectType.Computer)
        {
            common.AddRange([
                "dNSHostName", "operatingSystem", "operatingSystemVersion",
                "operatingSystemServicePack", "lastLogonTimestamp",
                "managedBy", "location", "serialNumber"
            ]);
        }
        else
        {
            common.AddRange([
                "sAMAccountName", "userPrincipalName", "givenName", "sn",
                "displayName", "mail", "telephoneNumber", "department",
                "title", "company", "manager", "streetAddress",
                "l", "st", "postalCode", "co", "memberOf"
            ]);
        }

        return common;
    }

    private DirectoryEntry CreateDirectoryEntry(string searchBase)
    {
        var path = BuildLdapPath(searchBase);
        var password = ConfigService.DecryptPassword(_config.EncryptedPassword);

        _log.Debug("Connecting to LDAP path: {Path} as {Username}", path, _config.Username);

        return new DirectoryEntry(path, _config.Username, password,
            _config.UseSsl ? AuthenticationTypes.SecureSocketsLayer : AuthenticationTypes.Secure);
    }

    private string BuildLdapPath(string searchBase)
    {
        var protocol = _config.UseSsl ? "LDAPS" : "LDAP";
        var host = !string.IsNullOrEmpty(_config.Server) ? _config.Server : _config.Domain;
        var port = _config.Port;

        if (!string.IsNullOrEmpty(searchBase))
            return $"{protocol}://{host}:{port}/{searchBase}";

        return $"{protocol}://{host}:{port}";
    }

    public void Dispose()
    {
        // DirectoryEntry instances are disposed individually via using statements
    }
}