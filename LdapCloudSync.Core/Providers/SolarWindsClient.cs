using System.Text.Json;
using System.Text.Json.Nodes;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// SolarWinds Service Desk (formerly Samanage) cloud client.
/// Uses Bearer token auth with X-Samanage-Authorization header.
/// Always fetches existing records first to determine POST vs PUT (SolarWinds allows duplicate POSTs).
/// </summary>
public sealed class SolarWindsClient : BaseCloudClient
{
    public SolarWindsClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null)
        : base(target, httpClient, logger)
    {
    }

    /// <summary>
    /// SolarWinds requires:
    /// - "X-Samanage-Authorization: Bearer {token}" (not standard Authorization header)
    /// - "Accept: application/vnd.samanage.v2.1+json" (versioned content negotiation)
    /// </summary>
    protected override void ApplyAuthentication(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;

        request.Headers.TryAddWithoutValidation("X-Samanage-Authorization", $"Bearer {conn.ApiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.samanage.v2.1+json");
    }

    /// <summary>
    /// SolarWinds nesting rules:
    /// - bio.* fields -> {"bio": {...}}
    /// - Simple string values for status/category/owner -> wrapped in objects with "name" or "email" keys
    /// </summary>
    protected override void PreparePushRecord(SyncCategoryConfig categoryConfig, Dictionary<string, object> record)
    {
        // Nest any "bio.*" fields into a "bio" sub-object
        var bioKeys = record.Keys.Where(k => k.StartsWith("bio.", StringComparison.OrdinalIgnoreCase)).ToList();
        if (bioKeys.Count > 0)
        {
            var bio = new Dictionary<string, object>();
            foreach (var key in bioKeys)
            {
                var childKey = key[(key.IndexOf('.') + 1)..];
                bio[childKey] = record[key];
                record.Remove(key);
            }
            record["bio"] = bio;
        }

        // Wrap status string into {"name": "...}
        if (record.TryGetValue("status", out var statusVal) && statusVal is string statusName && !string.IsNullOrEmpty(statusName))
        {
            record["status"] = new Dictionary<string, object> { ["name"] = statusName };
        }

        // Wrap category string into {"name": "...}
        if (record.TryGetValue("category", out var catVal) && catVal is string catName && !string.IsNullOrEmpty(catName))
        {
            record["category"] = new Dictionary<string, object> { ["name"] = catName };
        }

        // Wrap owner string (email) into {"email": "...}
        if (record.TryGetValue("owner", out var ownerVal) && ownerVal is string ownerEmail && !string.IsNullOrEmpty(ownerEmail))
        {
            record["owner"] = new Dictionary<string, object> { ["email"] = ownerEmail };
        }
    }

    /// <summary>
    /// SolarWinds allows duplicate POSTs, so we MUST fetch existing records first
    /// and determine POST vs PUT by matching on the configured match field.
    /// </summary>
    public override async Task<SyncResult> PushRecordsAsync(
        string category,
        IReadOnlyList<Dictionary<string, object>> records)
    {
        var categoryConfig = GetCategoryConfig(category);
        var result = new SyncResult();
        var updateMatchCloudKey = ResolveUpdateMatchCloudKey(categoryConfig);

        if (string.IsNullOrEmpty(updateMatchCloudKey) || string.IsNullOrEmpty(categoryConfig.UpdateMatchCloudField))
        {
            _log.Warning("SolarWinds requires UpdateMatchAdField and UpdateMatchCloudField to prevent duplicates. Skipping sync.");
            result.Failed = records.Count;
            result.Errors.Add("Match fields not configured — cannot determine POST vs PUT.");
            return result;
        }

        // Fetch ALL existing records up front
        _log.Information("Fetching existing {Category} records from SolarWinds to prevent duplicates...", category);
        var existingRecords = await FetchAllPagesAsync(categoryConfig);
        _log.Information("Fetched {Count} existing {Category} records for matching", existingRecords.Count, category);

        foreach (var record in records)
        {
            try
            {
                PreparePushRecord(categoryConfig, record);

                var matchValue = ResolveRecordValue(record, updateMatchCloudKey);
                if (string.IsNullOrEmpty(matchValue))
                {
                    result.Failed++;
                    result.Errors.Add($"Match field '{updateMatchCloudKey}' is empty — cannot determine if record exists.");
                    continue;
                }

                var existingId = FindExistingRecordByUpdateMatch(existingRecords, categoryConfig, matchValue);
                var payload = WrapPayload(category, record);

                if (existingId is not null)
                {
                    // Record exists -> PUT (update)
                    var endpoint = categoryConfig.PutEndpoint.Replace("{id}", existingId);
                    await SendJsonAsync(HttpMethod.Put, endpoint, payload);
                    result.Updated++;
                    _log.Debug("Updated {Category} record: {MatchField}='{MatchValue}' (id={Id})",
                        category, categoryConfig.UpdateMatchCloudField, matchValue, existingId);
                }
                else
                {
                    // Record doesn't exist -> POST (create)
                    await SendJsonAsync(HttpMethod.Post, categoryConfig.PostEndpoint, payload);
                    result.Created++;
                    _log.Debug("Created {Category} record: {MatchField}='{MatchValue}'",
                        category, categoryConfig.UpdateMatchCloudField, matchValue);
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add(ex.Message);
                _log.Warning(ex, "Failed to sync {Category} record", category);
            }
        }

        _log.Information(
            "Push complete for {Category} on {Target}: {Created} created, {Updated} updated, {Failed} failed",
            category, _target.Name, result.Created, result.Updated, result.Failed);

        return result;
    }

    /// <summary>
    /// SolarWinds wrapping rules (confirmed via working PowerShell test):
    /// - Hardware: wrapped in {"hardware": {...}}
    /// - Users: wrapped in {"user": {...}}
    /// </summary>
    private static object WrapPayload(string category, Dictionary<string, object> record)
    {
        return category.ToLowerInvariant() switch
        {
            "assets" => new Dictionary<string, object> { ["hardware"] = record },
            "users" => new Dictionary<string, object> { ["user"] = record },
            _ => record
        };
    }

    /// <summary>
    /// SolarWinds paginates with ?page=N (25 items per page by default).
    /// Fetches all pages to build the full existing record set.
    /// </summary>
    private async Task<List<JsonObject>> FetchAllPagesAsync(SyncCategoryConfig categoryConfig)
    {
        var allRecords = new List<JsonObject>();

        if (string.IsNullOrEmpty(categoryConfig.GetEndpoint))
            return allRecords;

        var page = 1;
        var hasMore = true;

        while (hasMore)
        {
            try
            {
                var separator = categoryConfig.GetEndpoint.Contains('?') ? "&" : "?";
                var pagedEndpoint = $"{categoryConfig.GetEndpoint}{separator}page={page}&per_page=100";

                var request = BuildRequest(HttpMethod.Get, pagedEndpoint);
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _log.Warning("FetchAllPages failed on page {Page}: {StatusCode}",
                        page, (int)response.StatusCode);
                    break;
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonNode.Parse(json);
                if (doc is null) break;

                var items = ResolveItemsPath(doc, categoryConfig.ResponseItemsPath);
                if (items is null || items.Count == 0)
                {
                    hasMore = false;
                    break;
                }

                var pageRecords = items.OfType<JsonObject>().ToList();
                allRecords.AddRange(pageRecords);

                _log.Debug("FetchAllPages page {Page}: {Count} records (total: {Total})",
                    page, pageRecords.Count, allRecords.Count);

                hasMore = pageRecords.Count >= 100;
                page++;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "FetchAllPages error on page {Page}", page);
                break;
            }
        }

        _log.Information("FetchAllPages complete: {Count} total records from {Endpoint}",
            allRecords.Count, categoryConfig.GetEndpoint);

        return allRecords;
    }

    protected override Task<List<JsonObject>> FetchExistingRecordsAsync(SyncCategoryConfig categoryConfig)
        => FetchAllPagesAsync(categoryConfig);
}