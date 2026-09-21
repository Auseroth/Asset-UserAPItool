using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// Reftab-specific cloud client.
/// Implements Reftab's custom HMAC authentication scheme and category management.
/// </summary>
public sealed class ReftabClient : BaseCloudClient
{
    public ReftabClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null)
        : base(target, httpClient, logger)
    {
    }

    public override async Task<SyncResult> PushRecordsAsync(
        string category,
        IReadOnlyList<Dictionary<string, object>> records)
    {
        _log.Information("Reftab PushRecordsAsync override engaged for category '{Category}' with {Count} record(s).",
            category, records.Count);

        if (!string.Equals(category, "assets", StringComparison.OrdinalIgnoreCase) || records.Count == 0)
            return await base.PushRecordsAsync(category, records);

        var passthrough = new List<Dictionary<string, object>>();
        var result = new SyncResult();

        foreach (var record in records)
        {
            if (TryGetMaintenanceKickoffPayload(record, out var aid, out var note, out var trigger))
            {
                _log.Information("Reftab maintenance kickoff detected for aid '{Aid}'. Using fetch+PUT update path.", aid);

                var updateResult = await UpdateAssetMaintenanceByAidAsync(
                    aid,
                    note,
                    string.IsNullOrWhiteSpace(trigger) ? "Yes" : trigger);

                MergeSyncResult(result, updateResult);
                continue;
            }

            passthrough.Add(record);
        }

        if (passthrough.Count > 0)
        {
            _log.Information("Reftab maintenance detector did not match {Count} record(s); falling back to base push path.",
                passthrough.Count);

            var baseResult = await base.PushRecordsAsync(category, passthrough);
            MergeSyncResult(result, baseResult);
        }

        return result;
    }

    /// <summary>
    /// Reftab uses a custom HMAC scheme:
    /// - RFC 2822 date format
    /// - String-to-sign: METHOD\n\n\nDATE\nFULL_URL
    /// - Signature: Base64(UTF8(Hex(HMAC-SHA256)))
    /// - Headers: x-rt-date, Authorization: RT {key}:{sig}
    /// </summary>
    protected override void ApplyAuthentication(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;

        if (conn.AuthType == AuthType.Hmac)
        {
            ApplyReftabHmacAuth(request, method, endpoint);
        }
        else
        {
            // Fallback to standard auth for non-HMAC
            base.ApplyAuthentication(request, method, endpoint);
        }
    }

    private void ApplyReftabHmacAuth(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;

        // 1. RFC 2822 date format (same as PowerShell's (Get-Date).ToUniversalTime().ToString("R"))
        var rtDate = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture) + " GMT";

        // 2. Full URL for signing
        var fullUrl = conn.BaseUrl.TrimEnd('/') + endpoint;

        // 3. For POST/PUT, compute Content-MD5 as LOWERCASE HEX (not Base64)
        //    Reftab expects: lowercase hex of MD5 hash
        var contentMd5 = "";
        var contentType = "";

        if (request.Content is not null)
        {
            var bodyBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (bodyBytes.Length > 0)
            {
                // CRITICAL: Reftab requires lowercase hex for MD5, NOT Base64
                var md5Bytes = MD5.HashData(bodyBytes);
                contentMd5 = string.Concat(md5Bytes.Select(b => b.ToString("x2")));
            }
            contentType = "application/json";

            // Ensure Content-Type header is exactly "application/json" (no charset)
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        // 4. String to sign: METHOD\nMD5\nCONTENT_TYPE\nDATE\nURL
        var stringToSign = $"{method.Method}\n{contentMd5}\n{contentType}\n{rtDate}\n{fullUrl}";

        _log.Information("Reftab HMAC Auth: {Method} {Url} | Date: {Date} | String-to-sign: {StringToSign}",
            method.Method, fullUrl, rtDate, stringToSign.Replace("\n", "\\n"));

        // 5. Compute HMAC-SHA256
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(conn.ApiSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));

        // 6. Convert to lowercase hex string
        var hexHash = string.Concat(hashBytes.Select(b => b.ToString("x2")));

        // 7. Base64-encode the hex string (Reftab's unique approach)
        var signature = Convert.ToBase64String(Encoding.UTF8.GetBytes(hexHash));

        // 8. Set Reftab-specific headers (including Content-MD5 for POST/PUT)
        request.Headers.TryAddWithoutValidation("x-rt-date", rtDate);
        request.Headers.TryAddWithoutValidation("Authorization", $"RT {conn.ApiKey}:{signature}");

        if (!string.IsNullOrEmpty(contentMd5))
        {
            request.Headers.TryAddWithoutValidation("Content-MD5", contentMd5);
        }

        _log.Information("Reftab auth complete | MD5: {MD5} | ContentType: {CT} | Signature: {Sig}",
            contentMd5, contentType, signature);
    }

    /// <summary>
    /// Reftab requires:
    /// - Assets: cid, clid injected; details.* and status.* nested into objects
    /// - Loanees: "disabled" hardcoded to false; details always present (empty {} if no custom fields)
    /// - Fields prefixed with "details." or "status." are nested into matching objects
    ///   e.g., "details.Serial Number" becomes { "details": { "Serial Number": "ABC123" } }
    ///         "status.name" becomes { "status": { "name": "Available" } }
    /// </summary>
    protected override void PreparePushRecord(SyncCategoryConfig categoryConfig, Dictionary<string, object> record)
    {
        var isLoanee = categoryConfig.PostEndpoint.Contains("/loanees", StringComparison.OrdinalIgnoreCase);

        // Inject category ID if configured (assets only)
        if (!isLoanee && categoryConfig.TargetCategoryId > 0 && !record.ContainsKey("cid"))
        {
            record["cid"] = categoryConfig.TargetCategoryId;
        }

        // Inject location ID if configured (assets only)
        if (!isLoanee && categoryConfig.TargetLocationId > 0 && !record.ContainsKey("clid"))
        {
            record["clid"] = categoryConfig.TargetLocationId;
        }

        // Loanees: inject "disabled" as false if not already mapped
        if (isLoanee && !record.ContainsKey("disabled"))
        {
            record["disabled"] = false;
        }

        // Reftab assets require a top-level title even on updates.
        // For kiosk maintenance payloads, default title from aid when not mapped.
        if (!isLoanee &&
            (!record.TryGetValue("title", out var titleObj) || string.IsNullOrWhiteSpace(titleObj?.ToString())) &&
            record.TryGetValue("aid", out var aidObj) &&
            !string.IsNullOrWhiteSpace(aidObj?.ToString()))
        {
            record["title"] = aidObj!.ToString()!;
        }

        // Restructure "details.*" keys into a nested details object
        var detailKeys = record.Keys
            .Where(k => k.StartsWith("details.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (detailKeys.Count > 0)
        {
            var details = new Dictionary<string, object>();

            foreach (var key in detailKeys)
            {
                var fieldName = key["details.".Length..];
                details[fieldName] = record[key];
                record.Remove(key);
            }

            // Merge with any existing details object
            if (record.TryGetValue("details", out var existing) && existing is Dictionary<string, object> existingDetails)
            {
                foreach (var kvp in details)
                    existingDetails[kvp.Key] = kvp.Value;
            }
            else
            {
                record["details"] = details;
            }
        }

        // Ensure "details" is always present (Reftab requires it, even as empty {})
        if (!record.ContainsKey("details"))
        {
            record["details"] = new Dictionary<string, object>();
        }

        // Reorder based on record type
        if (isLoanee)
            ReorderLoaneeRecord(record);
        else
            ReorderAssetRecord(record);
    }

    /// <summary>
    /// Reorders asset record keys:
    /// title -> aid -> clid -> cid -> notes -> (other fields) -> details
    /// </summary>
    private static void ReorderAssetRecord(Dictionary<string, object> record)
    {
        string[] leadingKeys = ["title", "aid", "clid", "cid", "notes"];
        ReorderRecordImpl(record, leadingKeys);
    }

    /// <summary>
    /// Reorders loanee record keys:
    /// name -> email -> title -> employeeID -> disabled -> (other fields) -> details
    /// </summary>
    private static void ReorderLoaneeRecord(Dictionary<string, object> record)
    {
        string[] leadingKeys = ["name", "email", "title", "employeeId", "disabled"];
        ReorderRecordImpl(record, leadingKeys);
    }

    /// <summary>
    /// Reorders record keys: leading keys first, then remaining fields, details last.
    /// </summary>
    private static void ReorderRecordImpl(Dictionary<string, object> record, string[] leadingKeys)
    {
        var ordered = new List<KeyValuePair<string, object>>();

        // Priority keys first, in order
        foreach (var key in leadingKeys)
        {
            if (record.TryGetValue(key, out var val))
                ordered.Add(new(key, val));
        }

        // Everything else except details
        foreach (var kvp in record)
        {
            if (!leadingKeys.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase)
                && !kvp.Key.Equals("details", StringComparison.OrdinalIgnoreCase))
            {
                ordered.Add(kvp);
            }
        }

        // Details last
        if (record.TryGetValue("details", out var details))
            ordered.Add(new("details", details));

        record.Clear();
        foreach (var kvp in ordered)
            record[kvp.Key] = kvp.Value;
    }

    private static bool TryGetMaintenanceKickoffPayload(
        Dictionary<string, object> record,
        out string aid,
        out string maintenanceNote,
        out string maintenanceTrigger)
    {
        aid = string.Empty;
        maintenanceNote = string.Empty;
        maintenanceTrigger = string.Empty;

        if (!record.TryGetValue("aid", out var aidValue))
            return false;

        aid = aidValue?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(aid))
            return false;

        if (record.TryGetValue("details", out var detailsObj))
        {
            if (detailsObj is Dictionary<string, object> details)
            {
                maintenanceNote = details.TryGetValue("Maintenance Check-in Notes", out var note)
                    ? note?.ToString()?.Trim() ?? string.Empty
                    : string.Empty;

                maintenanceTrigger = details.TryGetValue("Maintenance trigger", out var trigger)
                    ? trigger?.ToString()?.Trim() ?? string.Empty
                    : string.Empty;

                return true;
            }

            if (detailsObj is JsonObject jsonDetails)
            {
                maintenanceNote = jsonDetails["Maintenance Check-in Notes"]?.ToString()?.Trim() ?? string.Empty;
                maintenanceTrigger = jsonDetails["Maintenance trigger"]?.ToString()?.Trim() ?? string.Empty;
                return true;
            }
        }

        // Support flat keys in case details nesting has not yet occurred.
        if (record.TryGetValue("details.Maintenance Check-in Notes", out var flatNote))
            maintenanceNote = flatNote?.ToString()?.Trim() ?? string.Empty;

        if (record.TryGetValue("details.Maintenance trigger", out var flatTrigger))
            maintenanceTrigger = flatTrigger?.ToString()?.Trim() ?? string.Empty;

        return record.Keys.Any(k => k.StartsWith("details.", StringComparison.OrdinalIgnoreCase));
    }

    private static void MergeSyncResult(SyncResult aggregate, SyncResult addition)
    {
        aggregate.Created += addition.Created;
        aggregate.Updated += addition.Updated;
        aggregate.Skipped += addition.Skipped;
        aggregate.Failed += addition.Failed;
        aggregate.Errors.AddRange(addition.Errors);

        if (!string.IsNullOrWhiteSpace(addition.LastSuccessResponseBody))
            aggregate.LastSuccessResponseBody = addition.LastSuccessResponseBody;
    }

    /// <summary>
    /// Resolves a Reftab asset by aid, updates maintenance fields, and PUTs back
    /// the full asset payload with only those fields changed.
    /// </summary>
    public async Task<SyncResult> UpdateAssetMaintenanceByAidAsync(
        string aid,
        string maintenanceNote,
        string maintenanceTriggerValue = "Yes")
    {
        var result = new SyncResult();

        if (string.IsNullOrWhiteSpace(aid))
        {
            result.Failed = 1;
            result.Errors.Add("Asset tag (aid) is required for Reftab maintenance update.");
            return result;
        }

        try
        {
            var getRequest = BuildRequest(HttpMethod.Get, "/assets");
            var getResponse = await _httpClient.SendAsync(getRequest);
            var getBody = await getResponse.Content.ReadAsStringAsync();
            getResponse.EnsureSuccessStatusCode();

            var root = JsonNode.Parse(getBody);
            JsonArray? items = root as JsonArray;

            if (items is null && root is JsonObject wrapper)
            {
                items = wrapper["assets"]?.AsArray()
                     ?? wrapper["data"]?.AsArray()
                     ?? wrapper["results"]?.AsArray();
            }

            if (items is null)
            {
                result.Failed = 1;
                result.Errors.Add("Reftab /assets response was not an array.");
                return result;
            }

            JsonObject? matchedAsset = null;
            foreach (var item in items)
            {
                if (item is not JsonObject obj) continue;
                var existingAid = obj["aid"]?.ToString();
                if (string.Equals(existingAid, aid, StringComparison.OrdinalIgnoreCase))
                {
                    matchedAsset = obj;
                    break;
                }
            }

            if (matchedAsset is null)
            {
                _log.Information("Asset aid '{Aid}' not found in /assets list response. Trying direct GET /assets/{Aid}.", aid);

                var directGetRequest = BuildRequest(HttpMethod.Get, $"/assets/{aid}");
                var directGetResponse = await _httpClient.SendAsync(directGetRequest);
                var directGetBody = await directGetResponse.Content.ReadAsStringAsync();

                if (directGetResponse.IsSuccessStatusCode)
                {
                    var directNode = JsonNode.Parse(directGetBody);
                    if (directNode is JsonObject directObj)
                    {
                        matchedAsset = directObj;
                    }
                    else if (directNode is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject arrObj)
                    {
                        matchedAsset = arrObj;
                    }
                }

                if (matchedAsset is null)
                {
                    result.Failed = 1;
                    result.Errors.Add($"No Reftab asset found for aid '{aid}' in list or direct lookup.");
                    _log.Warning("No Reftab asset found for aid '{Aid}' in list or direct lookup.", aid);
                    return result;
                }
            }

            var putPayload = JsonNode.Parse(matchedAsset.ToJsonString()) as JsonObject;
            if (putPayload is null)
            {
                result.Failed = 1;
                result.Errors.Add("Failed to build Reftab PUT payload from matched asset.");
                return result;
            }

            var details = putPayload["details"] as JsonObject ?? new JsonObject();
            details["Maintenance Check-in Notes"] = maintenanceNote;
            details["Maintenance trigger"] = maintenanceTriggerValue;
            putPayload["details"] = details;

            // Reftab endpoint identity is inconsistent across tenants/configurations.
            // Try canonical aid from fetched record first, then input aid.
            var endpointCandidates = new List<string>();
            var canonicalAid = putPayload["aid"]?.ToString();

            if (!string.IsNullOrWhiteSpace(canonicalAid))
                endpointCandidates.Add(canonicalAid);

            if (!string.IsNullOrWhiteSpace(aid)
                && !endpointCandidates.Contains(aid, StringComparer.OrdinalIgnoreCase))
            {
                endpointCandidates.Add(aid);
            }

            if (endpointCandidates.Count == 0)
                endpointCandidates.Add(aid);

            Exception? lastError = null;

            foreach (var endpointId in endpointCandidates)
            {
                try
                {
                    _log.Information("Reftab maintenance update for aid '{Aid}' using endpoint id '{EndpointId}'.",
                        aid, endpointId);

                    var responseBody = await SendJsonAsync(HttpMethod.Put, $"/assets/{endpointId}", putPayload);

                    result.Updated = 1;
                    result.LastSuccessResponseBody = responseBody;
                    return result;
                }
                catch (HttpRequestException ex)
                {
                    lastError = ex;

                    // Some Reftab environments reject PUT when aid is present in payload
                    // even though endpoint already identifies the asset.
                    if (ex.Message.Contains("Asset number already in use", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var payloadWithoutAid = JsonNode.Parse(putPayload.ToJsonString()) as JsonObject;
                            payloadWithoutAid?.Remove("aid");

                            _log.Warning("PUT /assets/{EndpointId} reported duplicate aid; retrying without 'aid' in payload.",
                                endpointId);

                            var retryBody = await SendJsonAsync(HttpMethod.Put, $"/assets/{endpointId}", payloadWithoutAid ?? putPayload);
                            result.Updated = 1;
                            result.LastSuccessResponseBody = retryBody;
                            return result;
                        }
                        catch (Exception retryEx)
                        {
                            lastError = retryEx;
                        }
                    }
                }
            }

            throw lastError ?? new InvalidOperationException("Reftab maintenance update failed with unknown error.");
        }
        catch (Exception ex)
        {
            result.Failed = 1;
            result.Errors.Add(ex.Message);
            _log.Warning(ex, "Reftab maintenance update failed for aid {Aid}", aid);
            return result;
        }
    }

    /// <summary>
    /// Fetches all Reftab categories via the dedicated /categories endpoint.
    /// Returns the verbose name for display and the cid for use when adding items.
    /// </summary>
    public async Task<List<(int Id, string Name)>> GetCategoriesAsync()
    {
        try
        {
            var endpoint = "/categories";

            var request = BuildRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request);

            var json = await response.Content.ReadAsStringAsync();
            _log.Information("Categories raw response ({StatusCode}): {Json}",
                (int)response.StatusCode, json);

            response.EnsureSuccessStatusCode();

            // Try parsing as array first; if the response wraps in an object, dig in
            var root = JsonNode.Parse(json);
            JsonArray? items = root as JsonArray;

            // Some Reftab endpoints wrap results in an object like { "categories": [...] }
            if (items is null && root is JsonObject obj)
            {
                // Try common wrapper keys
                items = obj["categories"]?.AsArray()
                     ?? obj["data"]?.AsArray()
                     ?? obj["results"]?.AsArray();
            }

            if (items is null)
            {
                _log.Warning("Categories response was not an array. Raw: {Json}", json);
                return [];
            }

            // Log first item to identify actual field names
            if (items.Count > 0)
                _log.Information("First category item: {Item}", items[0]?.ToJsonString());

            var categories = new List<(int Id, string Name)>();
            foreach (var item in items)
            {
                if (item is null) continue;

                // Try multiple possible field names for the ID
                var id = item["cid"]?.GetValue<int>()
                      ?? item["id"]?.GetValue<int>()
                      ?? item["category_id"]?.GetValue<int>()
                      ?? 0;

                // Try multiple possible field names for the name
                var name = item["catName"]?.GetValue<string>()
                        ?? item["name"]?.GetValue<string>()
                        ?? item["category_name"]?.GetValue<string>()
                        ?? string.Empty;

                if (id > 0 && !string.IsNullOrEmpty(name))
                    categories.Add((id, name));
            }

            return categories.OrderBy(c => c.Name).ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch categories from Reftab");
            return [];
        }
    }

    /// <summary>
    /// Fetches Reftab locations for UI selection.
    /// Flattens the hierarchical tree into a single list with indented names.
    /// </summary>
    public async Task<List<(int Id, string Name)>> GetLocationsAsync()
    {
        try
        {
            var endpoint = "/locations";

            var request = BuildRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request);

            var json = await response.Content.ReadAsStringAsync();
            _log.Information("Locations raw response ({StatusCode}): {Json}",
                (int)response.StatusCode, json);

            response.EnsureSuccessStatusCode();

            var root = JsonNode.Parse(json);
            JsonArray? items = root as JsonArray;

            if (items is null && root is JsonObject obj)
            {
                items = obj["locations"]?.AsArray()
                     ?? obj["data"]?.AsArray()
                     ?? obj["results"]?.AsArray();
            }

            if (items is null)
            {
                _log.Warning("Locations response was not an array. Raw: {Json}", json);
                return [];
            }

            if (items.Count > 0)
                _log.Information("First location item: {Item}", items[0]?.ToJsonString());

            var locations = new List<(int Id, string Name)>();
            FlattenLocations(items, locations, depth: 0);

            _log.Information("Flattened {Count} total locations from tree", locations.Count);
            return locations;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch locations from Reftab");
            return [];
        }
    }

    /// <summary>
    /// Reftab loanee matching:
    /// 1. Match on email — if the email already exists in Reftab, return the lnid for PUT
    /// 2. If the push record has no email (service accounts), fall back to matching on name
    /// Always returns the lnid (integer) since that's what Reftab's PUT /loanees/{lnid} requires.
    

    /// <summary>
    /// Recursively flattens the Reftab location tree.
    /// Indents child names with "->" prefixes to show hierarchy.
    /// </summary>
    private static void FlattenLocations(JsonArray items, List<(int Id, string Name)> result, int depth)
    {
        foreach (var item in items)
        {
            if (item is null) continue;

            var id = item["clid"]?.GetValue<int>()
                  ?? item["id"]?.GetValue<int>()
                  ?? item["location_id"]?.GetValue<int>()
                  ?? 0;

            var name = item["name"]?.GetValue<string>()
                    ?? item["location_name"]?.GetValue<string>()
                    ?? string.Empty;

            if (id > 0 && !string.IsNullOrEmpty(name))
            {
                // Indent child locations for readability in the dropdown
                var prefix = depth > 0 ? string.Concat(Enumerable.Repeat("-> ", depth)) : "";
                result.Add((id, $"{prefix}{name}"));
            }

            // Recurse into children
            var children = item["children"]?.AsArray();
            if (children is not null && children.Count > 0)
            {
                FlattenLocations(children, result, depth + 1);
            }
        }
    }
}