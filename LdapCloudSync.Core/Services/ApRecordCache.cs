using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace LdapCloudSync.Core.Services;

/// <summary>
/// Local JSON cache of Asset Panda collection records.
/// Stored in ProgramData alongside config so the service can diff locally
/// instead of hitting the search API on every sync.
/// 
/// File per collection: ap-cache-{collectionId}.json
/// Contains the full record set from the last search, keyed by record ID.
/// </summary>
public sealed class ApRecordCache
{
    private static readonly string CacheDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Connexus", "ap-cache");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILogger _log;

    public ApRecordCache(ILogger? logger = null)
    {
        _log = logger ?? Log.Logger;
    }

    /// <summary>
    /// Saves the full set of records fetched from a collection search.
    /// Each record is stored with its "id" as the key for fast lookup.
    /// </summary>
    public void SaveRecords(string collectionId, List<JsonObject> records)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var filePath = GetCacheFilePath(collectionId);

            var cache = new JsonObject
            {
                ["collectionId"] = collectionId,
                ["lastUpdated"] = DateTime.UtcNow.ToString("O"),
                ["recordCount"] = records.Count,
                ["records"] = new JsonObject()
            };

            var recordsObj = cache["records"]!.AsObject();
            foreach (var record in records)
            {
                var id = record["id"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(id))
                {
                    // Deep clone the record to avoid parent issues
                    var clone = JsonNode.Parse(record.ToJsonString())!.AsObject();
                    recordsObj[id] = clone;
                }
            }

            var json = cache.ToJsonString(JsonOptions);
            File.WriteAllText(filePath, json);

            _log.Information("AP Cache: Saved {Count} records for collection {CollectionId}",
                records.Count, collectionId);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "AP Cache: Failed to save records for collection {CollectionId}", collectionId);
        }
    }

    /// <summary>
    /// Loads the cached records for a collection.
    /// Returns an empty list if no cache exists or it's corrupt.
    /// </summary>
    public List<JsonObject> LoadRecords(string collectionId)
    {
        try
        {
            var filePath = GetCacheFilePath(collectionId);
            if (!File.Exists(filePath))
            {
                _log.Information("AP Cache: No cache file for collection {CollectionId}", collectionId);
                return [];
            }

            var json = File.ReadAllText(filePath);
            var cache = JsonNode.Parse(json)?.AsObject();
            var recordsObj = cache?["records"]?.AsObject();

            if (recordsObj is null)
                return [];

            var records = recordsObj
                .Select(kvp => kvp.Value?.AsObject())
                .Where(r => r is not null)
                .Cast<JsonObject>()
                .ToList();

            var lastUpdated = cache?["lastUpdated"]?.GetValue<string>() ?? "unknown";
            _log.Information("AP Cache: Loaded {Count} records for collection {CollectionId} (cached at {LastUpdated})",
                records.Count, collectionId, lastUpdated);

            return records;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "AP Cache: Failed to load records for collection {CollectionId}", collectionId);
            return [];
        }
    }

    /// <summary>
    /// Compares a push record against the cached version to determine if an update is needed.
    /// Returns true if any mapped field value has changed.
    /// </summary>
    public bool HasRecordChanged(JsonObject cachedRecord, Dictionary<string, object> pushRecord)
    {
        foreach (var kvp in pushRecord)
        {
            // Skip system/meta fields
            if (kvp.Key is "id" or "recordId")
                continue;

            var newValue = kvp.Value?.ToString() ?? string.Empty;

            if (cachedRecord.TryGetPropertyValue(kvp.Key, out var cachedVal))
            {
                var oldValue = cachedVal?.ToString() ?? string.Empty;
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    return true;
            }
            else
            {
                // Field doesn't exist in cache — it's new/changed
                if (!string.IsNullOrEmpty(newValue))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Looks up a cached record by matching a column value.
    /// Returns the record ID if found, null otherwise.
    /// </summary>
    public string? FindRecordIdByField(string collectionId, string columnId, string matchValue)
    {
        var records = LoadRecords(collectionId);

        foreach (var record in records)
        {
            if (record.TryGetPropertyValue(columnId, out var val)
                && string.Equals(val?.ToString(), matchValue, StringComparison.OrdinalIgnoreCase))
            {
                return record["id"]?.GetValue<string>();
            }
        }

        return null;
    }

    private static string GetCacheFilePath(string collectionId)
        => Path.Combine(CacheDirectory, $"ap-cache-{collectionId}.json");
}