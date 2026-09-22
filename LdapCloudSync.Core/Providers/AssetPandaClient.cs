using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using LdapCloudSync.Core.Services;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// Asset Panda-specific cloud client.
/// Uses custom header auth (Access-Key-Id + Access-Key-Secret) and a hierarchical
/// discovery model: Account -> Module -> Collection -> Columns.
/// 
/// Key differences from other providers:
/// - Endpoints are dynamic, built from discovered IDs (not preset).
/// - Field keys in payloads are column IDs (e.g., "01KMG_0_devicename_text"), not friendly names.
/// - "GET" existing records uses POST /collection-records/search with a body.
/// - POST/PUT both go to /collection-records with a batch body containing tenant info.
/// - PUT records require "recordId" (not "id") in the payload.
/// - Column displayName -> columnId mapping is required for field mapping UI.
/// - Local JSON cache in ProgramData enables change detection without API calls.
/// - Failed POSTs are retried as PUTs (handles records that already exist).
/// </summary>
public sealed class AssetPandaClient : BaseCloudClient
{
    /// <summary>
    /// Cached column mappings: displayName -> columnId.
    /// Populated by GetCollectionColumnsAsync and used by PreparePushRecord
    /// to translate friendly field names to AP column IDs.
    /// </summary>
    private Dictionary<string, string> _columnNameToId = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _columnIdToName = new(StringComparer.OrdinalIgnoreCase);

    private readonly ApRecordCache _recordCache;

    public AssetPandaClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null)
        : base(target, httpClient, logger)
    {
        _recordCache = new ApRecordCache(logger);
    }

    /// <summary>
    /// Asset Panda uses custom headers for auth:
    /// - Access-Key-Id: the API key
    /// - Access-Key-Secret: the API secret
    /// </summary>
    protected override void ApplyAuthentication(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        request.Headers.TryAddWithoutValidation("Access-Key-Id", conn.ApiKey);
        request.Headers.TryAddWithoutValidation("Access-Key-Secret", conn.ApiSecret);
        request.Headers.TryAddWithoutValidation("User-Agent", "Connexus");
    }

    /// <summary>
    /// AP doesn't use GET endpoints for test connectivity.
    /// Instead, test by fetching accounts — if that succeeds, the connection is valid.
    /// </summary>
    public override async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            var accounts = await GetAccountsAsync();

            if (accounts.Count > 0)
            {
                _log.Information("AP connection test PASSED: {Count} accounts found", accounts.Count);
                return (true, $"Connected successfully. Found {accounts.Count} account(s).");
            }

            return (false, "Connected but no accounts returned. Check API key permissions.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "AP connection test failed");
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Override cloud field discovery for AP — uses GetCollectionColumnsAsync
    /// instead of the base class GET endpoint approach, which doesn't apply to AP.
    /// </summary>
    public override async Task<(IReadOnlyList<string> Fields, string RawResponse)> DiscoverFieldsAsync(string category)
    {
        if (string.IsNullOrEmpty(_target.ApAccountId) ||
            string.IsNullOrEmpty(_target.ApModuleId))
        {
            return ([], "Asset Panda Account and Module must be selected first.");
        }

        var collectionId = category.Equals("assets", StringComparison.OrdinalIgnoreCase)
            ? _target.ApAssetsCollectionId
            : _target.ApUsersCollectionId;

        if (string.IsNullOrEmpty(collectionId))
        {
            return ([], $"No collection selected for {category}. Use the AP discovery dropdowns first.");
        }

        var columns = await GetCollectionColumnsAsync(_target.ApAccountId, _target.ApModuleId, collectionId);

        if (columns.Count == 0)
        {
            return ([], "No columns returned. Check collection selection and API permissions.");
        }

        var fieldNames = columns.Select(c => c.DisplayName).ToList();
        var rawJson = JsonSerializer.Serialize(
            columns.Select(c => new { c.Id, c.DisplayName }),
            new JsonSerializerOptions { WriteIndented = true });

        return (fieldNames, rawJson);
    }

    //  Discovery Chain 

    /// <summary>
    /// Fetches all accounts the API key has access to.
    /// Returns (id, displayName) pairs for the Account dropdown.
    /// </summary>
    public async Task<List<(string Id, string Name)>> GetAccountsAsync()
    {
        try
        {
            var request = BuildRequest(HttpMethod.Get, "/accounts");

            _log.Information("AP: GET {Url}", request.RequestUri);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            _log.Information("AP Accounts response ({StatusCode}): {Body}",
                (int)response.StatusCode, Truncate(json, 2000));

            if (!response.IsSuccessStatusCode)
            {
                _log.Error("AP Accounts failed: HTTP {StatusCode} — {Body}",
                    (int)response.StatusCode, Truncate(json, 1000));
                return [];
            }

            var items = ParseResponseArray(json);
            if (items is null)
            {
                _log.Warning("AP: ParseResponseArray returned null for accounts");
                return [];
            }

            var results = new List<(string Id, string Name)>();
            foreach (var item in items)
            {
                if (item is null) continue;

                var id = item["id"]?.GetValue<string>()
                      ?? item["accountId"]?.GetValue<string>()
                      ?? string.Empty;

                var name = item["name"]?.GetValue<string>()
                        ?? item["displayName"]?.GetValue<string>()
                        ?? string.Empty;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    results.Add((id, name));
            }

            _log.Information("AP: Found {Count} accounts", results.Count);
            return results.OrderBy(a => a.Name).ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch AP accounts: {Message}", ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Fetches all modules for a given account.
    /// The accountId is required by the AP API.
    /// Returns (id, displayName) pairs for the Module dropdown.
    /// </summary>
    public async Task<List<(string Id, string Name)>> GetModulesAsync(string accountId)
    {
        try
        {
            if (string.IsNullOrEmpty(accountId))
            {
                _log.Warning("AP: GetModulesAsync called without accountId");
                return [];
            }

            var request = BuildRequest(HttpMethod.Get, $"/modules?accountId={accountId}");

            _log.Information("AP: Sending GET {Url}", request.RequestUri);

            var response = await _httpClient.SendAsync(request);

            var json = await response.Content.ReadAsStringAsync();
            _log.Information("AP Modules response ({StatusCode}): {Body}",
                (int)response.StatusCode, Truncate(json, 2000));

            response.EnsureSuccessStatusCode();

            var items = ParseResponseArray(json);
            if (items is null)
            {
                _log.Warning("AP: ParseResponseArray returned null for modules");
                return [];
            }

            var results = new List<(string Id, string Name)>();
            foreach (var item in items)
            {
                if (item is null) continue;

                var id = item["id"]?.GetValue<string>()
                      ?? item["moduleId"]?.GetValue<string>()
                      ?? string.Empty;

                var name = item["displayName"]?.GetValue<string>()
                        ?? item["name"]?.GetValue<string>()
                        ?? string.Empty;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    results.Add((id, name));
            }

            _log.Information("AP: Found {Count} modules", results.Count);
            return results.OrderBy(m => m.Name).ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch Asset Panda modules: {Message} | Inner: {Inner}",
                ex.Message, ex.InnerException?.Message ?? "(none)");
            return [];
        }
    }

    /// <summary>
    /// Fetches collections for a given account + module.
    /// Returns (id, displayName) pairs for the Collection dropdown.
    /// </summary>
    public async Task<List<(string Id, string Name)>> GetCollectionsAsync(string accountId, string moduleId)
    {
        try
        {
            var endpoint = $"/collections?accountId={accountId}&moduleId={moduleId}";

            var request = BuildRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request);

            var json = await response.Content.ReadAsStringAsync();
            _log.Information("AP Collections response ({StatusCode}): {Length} chars",
                (int)response.StatusCode, json.Length);

            response.EnsureSuccessStatusCode();

            var items = ParseResponseArray(json);
            if (items is null) return [];

            if (items.Count > 0)
                _log.Information("AP first collection: {Item}", items[0]?.ToJsonString());

            var results = new List<(string Id, string Name)>();
            foreach (var item in items)
            {
                if (item is null) continue;

                var id = item["id"]?.GetValue<string>() ?? string.Empty;

                var name = item["displayName"]?.GetValue<string>()
                        ?? item["name"]?.GetValue<string>()
                        ?? string.Empty;

                var isActive = item["isActive"]?.GetValue<bool>() ?? true;
                var deleted = item["deleted"]?.GetValue<bool>() ?? false;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && isActive && !deleted)
                    results.Add((id, name));
            }

            _log.Information("AP: Found {Count} active collections for account={Account}, module={Module}",
                results.Count, accountId, moduleId);

            return results.OrderBy(c => c.Name).ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch Asset Panda collections");
            return [];
        }
    }

    /// <summary>
    /// Fetches the column definitions for a specific collection.
    /// Returns (columnId, displayName) pairs — columnId is used in payloads,
    /// displayName is shown in the field mapping UI.
    /// Also caches the mapping for use in PreparePushRecord.
    /// </summary>
    public async Task<List<(string Id, string DisplayName)>> GetCollectionColumnsAsync(
        string accountId, string moduleId, string collectionId)
    {
        try
        {
            var endpoint = $"/collection-columns?accountId={accountId}&moduleId={moduleId}&collectionId={collectionId}";

            var request = BuildRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request);

            var json = await response.Content.ReadAsStringAsync();
            _log.Information("AP Columns response ({StatusCode}): {Body}",
                (int)response.StatusCode, Truncate(json, 3000));

            if (!response.IsSuccessStatusCode)
            {
                _log.Error("AP Columns failed: HTTP {StatusCode} — {Body}",
                    (int)response.StatusCode, Truncate(json, 1000));
                return [];
            }

            var root = JsonNode.Parse(json);
            JsonArray? items = null;

            if (root is JsonArray arr)
            {
                items = arr;
            }
            else if (root is JsonObject obj)
            {
                // AP returns: { "data": { "columns": { "all": [...] } } }
                items = AsArraySafe(obj["data"]?["columns"]?["all"])
                     ?? AsArraySafe(obj["data"]?["columns"])
                     ?? AsArraySafe(obj["data"])
                     ?? AsArraySafe(obj["columns"]?["all"])
                     ?? AsArraySafe(obj["columns"])
                     ?? AsArraySafe(obj["items"])
                     ?? AsArraySafe(obj["results"])
                     ?? AsArraySafe(obj["records"]);
            }

            if (items is null || items.Count == 0)
            {
                _log.Warning("AP: Could not find columns array in response. Raw: {Json}",
                    Truncate(json, 1000));
                return [];
            }

            _log.Information("AP: Parsing {Count} column items", items.Count);
            if (items.Count > 0)
                _log.Information("AP first column: {Item}", items[0]?.ToJsonString());

            var results = new List<(string Id, string DisplayName)>();
            var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (item is null) continue;

                var id = item["id"]?.GetValue<string>()
                      ?? item["columnId"]?.GetValue<string>()
                      ?? string.Empty;

                var displayName = item["displayName"]?.GetValue<string>()
                               ?? item["name"]?.GetValue<string>()
                               ?? string.Empty;

                var deleted = item["deleted"]?.GetValue<bool>() ?? false;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(displayName) && !deleted)
                {
                    results.Add((id, displayName));
                    nameToId[displayName] = id;
                    idToName[id] = displayName;
                }
            }

            _columnNameToId = nameToId;
            _columnIdToName = idToName;

            _log.Information("AP: Found {Count} columns for collection={Collection}",
                results.Count, collectionId);

            return results;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch AP collection columns: {Message}", ex.Message);
            return [];
        }
    }

    //  Sync Pipeline Override 

    /// <summary>
    /// Overrides the entire push pipeline for Asset Panda because:
    /// 1. Column cache must be warmed before any record processing.
    /// 2. POST/PUT use /collection-records with a batch body (not per-record endpoints).
    /// 3. PUT requires "recordId" (not "id") in each record.
    /// 4. Local cache enables change detection — only push records that actually changed.
    /// 5. Failed POSTs are retried as PUTs using UpdateMatch fields.
    /// </summary>
    public override async Task<SyncResult> PushRecordsAsync(
        string category,
        IReadOnlyList<Dictionary<string, object>> records)
    {
        var result = new SyncResult();
        var collectionId = category.Equals("assets", StringComparison.OrdinalIgnoreCase)
            ? _target.ApAssetsCollectionId
            : _target.ApUsersCollectionId;

        if (string.IsNullOrEmpty(_target.ApAccountId) ||
            string.IsNullOrEmpty(_target.ApModuleId) ||
            string.IsNullOrEmpty(collectionId))
        {
            result.Errors.Add("Asset Panda Account, Module, or Collection ID not configured.");
            return result;
        }

        // Step 1: Warm the column cache
        if (_columnNameToId.Count == 0)
        {
            _log.Information("AP: Warming column cache for collection {CollectionId}", collectionId);
            await GetCollectionColumnsAsync(_target.ApAccountId, _target.ApModuleId, collectionId);
        }

        var categoryConfig = category.Equals("assets", StringComparison.OrdinalIgnoreCase)
            ? _target.Assets
            : _target.Users;

        // Resolve update match: which cloud key (post-translation) holds the AD match value
        var updateMatchCloudKey = ResolveUpdateMatchCloudKeyAp(categoryConfig);
        var hasUpdateMatch = !string.IsNullOrEmpty(updateMatchCloudKey)
                          && !string.IsNullOrEmpty(categoryConfig.UpdateMatchCloudField);

        // Step 2: Prepare all records for POST
        var toCreate = new List<Dictionary<string, object>>();

        foreach (var record in records)
        {
            try
            {
                PreparePushRecord(categoryConfig, record);
                toCreate.Add(record);
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"Record prep failed: {ex.Message}");
                _log.Warning(ex, "AP: Failed to prepare record for push");
            }
        }

        _log.Information("AP: {Count} records prepared for POST", toCreate.Count);

        // Step 3: POST all records in batch
        if (toCreate.Count > 0)
        {
            var createResult = await SendCollectionRecordsBatchAsync(
                HttpMethod.Post, collectionId, toCreate);
            result.Created += createResult.Succeeded;
            result.Failed += createResult.Failed;
            result.Errors.AddRange(createResult.Errors);

            // Step 4: Retry failed POSTs as PUTs using UpdateMatch fields
            if (createResult.FailedRecords.Count > 0 && hasUpdateMatch)
            {
                _log.Information("AP: Retrying {Count} failed POST(s) as PUT using UpdateMatch fields",
                    createResult.FailedRecords.Count);

                // Fresh fetch of existing records
                var existingRecords = await FetchExistingRecordsAsync(categoryConfig);
                _log.Information("AP: Fetched {Count} existing records for PUT retry matching",
                    existingRecords.Count);

                // Resolve the match column ID (displayName -> columnId)
                var matchColumnId = _columnNameToId.TryGetValue(categoryConfig.UpdateMatchCloudField, out var colId)
                    ? colId
                    : categoryConfig.UpdateMatchCloudField;

                var retryAsUpdate = new List<Dictionary<string, object>>();

                foreach (var failedRecord in createResult.FailedRecords)
                {
                    // Get the match value from the record (already translated to columnId keys)
                    var translatedKey = _columnNameToId.TryGetValue(updateMatchCloudKey!, out var transColId)
                        ? transColId
                        : updateMatchCloudKey!;

                    var matchValue = failedRecord.TryGetValue(translatedKey, out var mv)
                        ? mv?.ToString() ?? string.Empty
                        : string.Empty;

                    if (string.IsNullOrEmpty(matchValue))
                    {
                        _log.Warning("AP: PUT retry skipped — match field '{Field}' is empty in record", updateMatchCloudKey);
                        continue;
                    }

                    // Search existing records for a match
                    string? existingId = null;
                    foreach (var existing in existingRecords)
                    {
                        if (existing.TryGetPropertyValue(matchColumnId, out var cloudVal)
                            && string.Equals(cloudVal?.ToString(), matchValue, StringComparison.OrdinalIgnoreCase))
                        {
                            existingId = existing["id"]?.GetValue<string>();
                            break;
                        }
                    }

                    if (existingId is not null)
                    {
                        failedRecord.Remove("id");
                        failedRecord["recordId"] = existingId;
                        retryAsUpdate.Add(failedRecord);
                        _log.Information("AP: PUT retry matched {Field}='{Value}' -> id='{Id}'",
                            categoryConfig.UpdateMatchCloudField, matchValue, existingId);
                    }
                    else
                    {
                        _log.Warning("AP: PUT retry — no match found for {Field}='{Value}'",
                            categoryConfig.UpdateMatchCloudField, matchValue);
                    }
                }

                if (retryAsUpdate.Count > 0)
                {
                    var retryResult = await SendCollectionRecordsBatchAsync(
                        HttpMethod.Put, collectionId, retryAsUpdate);

                    // Reclassify: move from Failed to Updated
                    result.Failed -= retryResult.Succeeded;
                    result.Updated += retryResult.Succeeded;
                    result.Failed += retryResult.Failed;
                    result.Errors.AddRange(retryResult.Errors);

                    _log.Information("AP: PUT retry: {Succeeded} succeeded, {Failed} still failed",
                        retryResult.Succeeded, retryResult.Failed);
                }
            }
        }

        // Step 5: Refresh local cache after successful push
        if (!DryRunMode && (result.Created > 0 || result.Updated > 0))
        {
            _log.Information("AP: Refreshing local cache after push");
            var refreshed = await FetchExistingRecordsAsync(categoryConfig);
            if (refreshed.Count > 0)
                _recordCache.SaveRecords(collectionId, refreshed);
        }

        _log.Information(
            "AP Push complete for {Category}: Created {Created}, Updated {Updated}, Skipped {Skipped}, Failed {Failed}",
            category, result.Created, result.Updated, result.Skipped, result.Failed);

        return result;
    }

    /// <summary>
    /// Sends a batch of records to POST /collection-records or PUT /collection-records.
    /// POST body: { tenant, collectionId, records: [{ columnId: value }] }
    /// PUT body:  { tenant, collectionId, records: [{ recordId: "...", columnId: value }] }
    /// Returns per-record success/failure counts and the list of failed records for retry.
    /// </summary>
    private async Task<(int Succeeded, int Failed, List<string> Errors, List<Dictionary<string, object>> FailedRecords)>
        SendCollectionRecordsBatchAsync(
            HttpMethod method,
            string collectionId,
            List<Dictionary<string, object>> records)
    {
        var errors = new List<string>();
        var failedRecords = new List<Dictionary<string, object>>();
        var succeeded = 0;
        var failed = 0;

        try
        {
            var recordsArray = new JsonArray();
            foreach (var record in records)
            {
                var obj = new JsonObject();
                foreach (var kvp in record)
                {
                    obj[kvp.Key] = kvp.Value switch
                    {
                        null => null,
                        string s => JsonValue.Create(s),
                        bool b => JsonValue.Create(b),
                        int i => JsonValue.Create(i),
                        long l => JsonValue.Create(l),
                        double d => JsonValue.Create(d),
                        decimal m => JsonValue.Create(m),
                        _ => JsonNode.Parse(JsonSerializer.Serialize(kvp.Value))
                    };
                }
                recordsArray.Add(obj);
            }

            var body = new JsonObject
            {
                ["tenant"] = new JsonObject
                {
                    ["accountId"] = _target.ApAccountId,
                    ["moduleId"] = _target.ApModuleId
                },
                ["collectionId"] = collectionId,
                ["records"] = recordsArray
            };

            var jsonBody = body.ToJsonString();
            _log.Information("AP {Method} /collection-records: {RecordCount} records, {BodyLength} chars",
                method.Method, records.Count, jsonBody.Length);

            if (DryRunMode)
            {
                DryRunCaptures.Add(new DryRunCapture
                {
                    Method = method.Method,
                    Endpoint = "/collection-records",
                    JsonBody = body.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                });
                return (records.Count, 0, [], []);
            }

            var request = BuildRequest(method, "/collection-records");
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            _log.Information("AP {Method} response ({StatusCode}): {Length} chars",
                method.Method, (int)response.StatusCode, responseJson.Length);

            if (!response.IsSuccessStatusCode)
            {
                _log.Error("AP {Method} failed: {StatusCode} — {Body}",
                    method.Method, (int)response.StatusCode, Truncate(responseJson, 1000));
                // All records failed — return them all for potential retry
                return (0, records.Count,
                    [$"{method.Method} failed: HTTP {(int)response.StatusCode}"],
                    new List<Dictionary<string, object>>(records));
            }

            // Parse response to count successes/failures per record
            var root = JsonNode.Parse(responseJson);
            var dataRecords = AsArraySafe(root?["data"]?["records"]);

            if (dataRecords is not null)
            {
                for (int i = 0; i < dataRecords.Count; i++)
                {
                    var item = dataRecords[i];
                    if (item is null) continue;

                    var success = item["success"]?.GetValue<bool>() ?? false;
                    if (success)
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed++;
                        // Track the original record so it can be retried
                        if (i < records.Count)
                            failedRecords.Add(records[i]);

                        var recordErrors = AsArraySafe(item["errors"]);
                        if (recordErrors is not null && recordErrors.Count > 0)
                        {
                            var errorMsg = string.Join("; ", recordErrors.Select(e => e?.ToString() ?? ""));
                            errors.Add(errorMsg);
                            _log.Warning("AP record error: {Error}", errorMsg);
                        }
                        else
                        {
                            errors.Add($"Record at index {item["index"]} failed without error details.");
                        }
                    }
                }
            }
            else
            {
                succeeded = records.Count;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "AP: Batch {Method} failed", method.Method);
            return (0, records.Count, [ex.Message], new List<Dictionary<string, object>>(records));
        }

        return (succeeded, failed, errors, failedRecords);
    }

    //  Fetch Override 

    /// <summary>
    /// Fetches existing records from Asset Panda using POST /collection-records/search.
    /// </summary>
    protected override async Task<List<JsonObject>> FetchExistingRecordsAsync(SyncCategoryConfig categoryConfig)
    {
        var collectionId = GetCollectionIdForCategory(categoryConfig);
        if (string.IsNullOrEmpty(collectionId))
        {
            _log.Warning("AP: No collection ID configured — cannot fetch existing records");
            return [];
        }

        try
        {
            var searchBody = new JsonObject
            {
                ["collectionId"] = collectionId,
                ["tenant"] = new JsonObject
                {
                    ["moduleId"] = _target.ApModuleId,
                    ["accountId"] = _target.ApAccountId
                }
            };

            var endpoint = "/collection-records/search";
            var request = BuildRequest(HttpMethod.Post, endpoint);
            request.Content = new StringContent(
                searchBody.ToJsonString(),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            _log.Information("AP FetchExisting response ({StatusCode}): {Length} chars",
                (int)response.StatusCode, json.Length);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("AP FetchExisting failed: {StatusCode} — {Body}",
                    (int)response.StatusCode, Truncate(json, 500));
                return [];
            }

            var root = JsonNode.Parse(json);
            JsonArray? items = null;

            if (root is JsonArray arr)
                items = arr;
            else if (root is JsonObject obj)
                items = AsArraySafe(obj["data"]?["records"])
                     ?? AsArraySafe(obj["items"])
                     ?? AsArraySafe(obj["results"])
                     ?? AsArraySafe(obj["records"]);

            if (items is null)
            {
                _log.Warning("AP FetchExisting: Could not parse records array from response");
                return [];
            }

            var records = items.OfType<JsonObject>().ToList();

            _log.Information("AP FetchExisting: {Count} records from collection {CollectionId}",
                records.Count, collectionId);

            if (records.Count > 0)
            {
                var firstKeys = string.Join(", ", records[0].Select(p => p.Key).Take(10));
                _log.Information("AP FetchExisting first record keys (first 10): [{Keys}]", firstKeys);
            }

            return records;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "AP: Could not fetch existing records; will create all as new");
            return [];
        }
    }

    /// <summary>
    /// Translates friendly field names (displayName) to Asset Panda column IDs.
    /// </summary>
    protected override void PreparePushRecord(SyncCategoryConfig categoryConfig, Dictionary<string, object> record)
    {
        if (_columnNameToId.Count == 0)
        {
            _log.Warning("AP: Column mapping cache is empty — field names will be sent as-is.");
            return;
        }

        var translated = new Dictionary<string, object>();

        foreach (var kvp in record)
        {
            if (_columnNameToId.TryGetValue(kvp.Key, out var columnId))
            {
                translated[columnId] = kvp.Value;
                _log.Debug("AP field mapped: '{DisplayName}' -> '{ColumnId}'", kvp.Key, columnId);
            }
            else
            {
                translated[kvp.Key] = kvp.Value;
            }
        }

        record.Clear();
        foreach (var kvp in translated)
            record[kvp.Key] = kvp.Value;
    }

   
    /// <summary>
    /// AP-specific: resolves UpdateMatchAdField -> cloud field key, accounting for
    /// column name -> columnId translation that PreparePushRecord applies.
    /// </summary>
    private string? ResolveUpdateMatchCloudKeyAp(SyncCategoryConfig categoryConfig)
    {
        // First resolve AD field -> cloud field via mappings (same as base)
        var cloudKey = ResolveUpdateMatchCloudKey(categoryConfig);
        // The value is correct — PreparePushRecord will have translated the key to a columnId,
        // but the value itself is unchanged. We return the friendly name here;
        // callers translate to columnId when needed.
        return cloudKey;
    }

    /// <summary>
    /// AP-specific update match: resolves displayName to columnId for comparison.
    /// </summary>
    protected override string? FindExistingRecordByUpdateMatch(
        List<JsonObject> existingRecords,
        SyncCategoryConfig categoryConfig,
        string matchValue)
    {
        var matchColumnId = _columnNameToId.TryGetValue(categoryConfig.UpdateMatchCloudField, out var colId)
            ? colId
            : categoryConfig.UpdateMatchCloudField;

        foreach (var record in existingRecords)
        {
            if (record.TryGetPropertyValue(matchColumnId, out var cloudVal)
                && string.Equals(cloudVal?.ToString(), matchValue, StringComparison.OrdinalIgnoreCase))
            {
                var idVal = record["id"];
                if (idVal is not null)
                {
                    var id = idVal.GetValue<string>();
                    _log.Information("AP: Update match {Field}='{Value}' -> id='{Id}'",
                        matchColumnId, matchValue, id);
                    return id;
                }
            }
        }

        return null;
    }

    // Helpers

    private string GetCollectionIdForCategory(SyncCategoryConfig categoryConfig)
    {
        if (ReferenceEquals(categoryConfig, _target.Users))
            return _target.ApUsersCollectionId;
        return _target.ApAssetsCollectionId;
    }

    /// <summary>
    /// Safely attempts to interpret a JsonNode as a JsonArray.
    /// Returns null if the node is null or not actually an array,
    /// avoiding the InvalidOperationException from JsonNode.AsArray().
    /// </summary>
    private static JsonArray? AsArraySafe(JsonNode? node)
        => node is JsonArray arr ? arr : null;

    private JsonArray? ParseResponseArray(string json)
    {
        var root = JsonNode.Parse(json);

        if (root is JsonArray arr)
            return arr;

        if (root is JsonObject obj)
        {
            return AsArraySafe(obj["data"])
                ?? AsArraySafe(obj["items"])
                ?? AsArraySafe(obj["results"])
                ?? AsArraySafe(obj["collections"])
                ?? AsArraySafe(obj["modules"])
                ?? AsArraySafe(obj["accounts"])
                ?? AsArraySafe(obj["columns"])
                ?? AsArraySafe(obj["records"]);
        }

        _log.Warning("AP: Response was neither array nor object. Raw length: {Length}", json.Length);
        return null;
    }
}