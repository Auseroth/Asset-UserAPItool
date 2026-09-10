using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Providers;

/// <summary>
/// Base implementation for cloud API clients.
/// Provides shared HTTP request building, JSON parsing, and standard authentication.
/// Implements both ICloudClient (push/write) and ICloudSourceClient (pull/read)
/// so every provider automatically supports both target and source roles.
/// Provider-specific clients inherit and override as needed.
/// </summary>
public abstract class BaseCloudClient : ICloudClient, ICloudSourceClient, IDisposable
{
    protected readonly CloudTargetConfig _target;
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _log;

    protected BaseCloudClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _httpClient = httpClient ?? new HttpClient();
        _log = logger ?? Log.Logger;
    }

    public virtual async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            var endpoint = !string.IsNullOrEmpty(_target.Assets.GetEndpoint)
                ? _target.Assets.GetEndpoint
                : _target.Users.GetEndpoint;

            if (string.IsNullOrEmpty(endpoint))
                return (false, "No GET endpoint configured to test connectivity.");

            var request = BuildRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _log.Information("Cloud connection test PASSED for {Target}: {StatusCode}",
                    _target.Name, (int)response.StatusCode);
                return (true, $"Connected successfully. Status: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var body = await response.Content.ReadAsStringAsync();
            _log.Warning("Cloud connection test FAILED for {Target}: {StatusCode} {Body}",
                _target.Name, (int)response.StatusCode, Truncate(body, 300));

            return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 500)}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Cloud connection test failed for target {TargetName}", _target.Name);
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    public virtual async Task<(IReadOnlyList<string> Fields, string RawResponse)> DiscoverFieldsAsync(string category)
    {
        var categoryConfig = GetCategoryConfig(category);
        if (string.IsNullOrEmpty(categoryConfig.GetEndpoint))
            return ([], "No GET endpoint configured for this category.");

        try
        {
            var request = BuildRequest(HttpMethod.Get, categoryConfig.GetEndpoint);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var rawJson = await response.Content.ReadAsStringAsync();
            var fields = ExtractFieldNames(rawJson, categoryConfig.ResponseItemsPath);

            _log.Information("Discovered {Count} fields for {Category} from {Target}",
                fields.Count, category, _target.Name);

            return (fields, rawJson);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Field discovery failed for {Category} on {Target}", category, _target.Name);
            return ([], $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches records from the source API and returns flat string dictionaries
    /// compatible with TransformEngine. Also returns the raw JSON for the editor window.
    /// </summary>
    public virtual async Task<(IReadOnlyList<Dictionary<string, string>> Records, string RawJson)> GetRecordsAsync(
        string category,
        string? filter = null,
        int maxRecords = 0)
    {
        var categoryConfig = GetCategoryConfig(category);

        if (string.IsNullOrEmpty(categoryConfig.GetEndpoint))
            return ([], "No GET endpoint configured for this category.");

        try
        {
            var endpoint = categoryConfig.GetEndpoint;

            if (!string.IsNullOrEmpty(filter))
                endpoint = endpoint.Contains('?')
                    ? $"{endpoint}&{filter}"
                    : $"{endpoint}?{filter}";

            var request = BuildRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var rawJson = await response.Content.ReadAsStringAsync();

            var doc = JsonNode.Parse(rawJson);
            if (doc is null)
                return ([], rawJson);

            var items = ResolveItemsPath(doc, categoryConfig.ResponseItemsPath);
            if (items is null)
            {
                _log.Warning("GetRecordsAsync: ResolveItemsPath returned null for path '{Path}' on {Target}",
                    categoryConfig.ResponseItemsPath, _target.Name);
                return ([], rawJson);
            }

            var records = new List<Dictionary<string, string>>();

            foreach (var item in items)
            {
                if (item is not JsonObject obj) continue;
                var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                FlattenJsonObjectToStrings(obj, record, prefix: null);
                records.Add(record);
            }

            if (maxRecords > 0)
                records = records.Take(maxRecords).ToList();

            _log.Information("GetRecordsAsync: retrieved {Count} {Category} records from {Target}",
                records.Count, category, _target.Name);

            return (records, rawJson);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "GetRecordsAsync failed for {Category} on {Target}", category, _target.Name);
            return ([], $"Error: {ex.Message}");
        }
    }

    public virtual async Task<SyncResult> PushRecordsAsync(
        string category,
        IReadOnlyList<Dictionary<string, object>> records)
    {
        var categoryConfig = GetCategoryConfig(category);
        var result = new SyncResult();

        var updateMatchCloudKey = ResolveUpdateMatchCloudKey(categoryConfig);
        var secondaryUpdateMatchCloudKey = ResolveUpdateMatchCloudKey(categoryConfig, categoryConfig.SecondaryUpdateMatchSourceField);
        var hasUpdateMatch = !string.IsNullOrEmpty(updateMatchCloudKey)
                          || !string.IsNullOrEmpty(secondaryUpdateMatchCloudKey);

        var failedRecords = new List<Dictionary<string, object>>();

        foreach (var record in records)
        {
            try
            {
                PreparePushRecord(categoryConfig, record);
                await SendJsonAsync(HttpMethod.Post, categoryConfig.PostEndpoint, record);
                result.Created++;

                    var matchValue = !string.IsNullOrEmpty(updateMatchCloudKey) && record.TryGetValue(updateMatchCloudKey, out var mv)
                ? mv?.ToString() ?? "(unknown)"
                : "(unknown)";
                _log.Debug("Created {Category} record: {MatchValue}", category, matchValue);
            }
            catch (Exception ex)
            {
                if (hasUpdateMatch)
                {
                    failedRecords.Add(record);
                    _log.Information("POST failed for {Category} record, queued for PUT retry: {Error}",
                        category, ex.Message);
                }
                else
                {
                    result.Failed++;
                    result.Errors.Add(ex.Message);
                    _log.Warning(ex, "Failed to push {Category} record (no update match configured)", category);
                }
            }
        }

        // PUT retry: fetch fresh cloud records, match, and update
        if (failedRecords.Count > 0 && hasUpdateMatch)
        {
            _log.Information("Retrying {Count} failed POST(s) as PUT for {Category}",
                failedRecords.Count, category);

            List<JsonObject> existingRecords = [];
            var existingRecordsFetched = false;

            foreach (var record in failedRecords)
            {
                try
                {
                    // Get the value from the cloud record that originated from the AD match field
                    // Handle "details.X" keys that were nested by PreparePushRecord
                    // If the failed record already has the cloud ID field, retry directly first.
                    var directId = ResolveRecordValue(record, categoryConfig.CloudIdField);
                    if (!string.IsNullOrWhiteSpace(directId))
                    {
                        try
                        {
                            var directEndpoint = categoryConfig.PutEndpoint.Replace("{id}", directId);
                            await SendJsonAsync(HttpMethod.Put, directEndpoint, record);
                            result.Updated++;
                            continue;
                        }
                        catch (Exception ex)
                        {
                            _log.Warning(ex,
                                "Direct PUT retry failed for {Category} using {IdField}={IdValue}; falling back to lookup",
                                category, categoryConfig.CloudIdField, directId);
                        }
                    }

                    // Handle "details.X" keys that were nested by PreparePushRecord.
                    var primaryMatchField = updateMatchCloudKey;
                    var matchValue = ResolveRecordValue(record, primaryMatchField!);

                    if (string.IsNullOrEmpty(matchValue))
                    {
                        result.Failed++;
                        result.Errors.Add($"Update match field '{updateMatchCloudKey}' is empty — cannot retry as PUT.");
                        continue;
                    }

                    // Search fresh cloud records for a match
                    var matchedField = primaryMatchField;
                    if (!existingRecordsFetched)
                    {
                        existingRecords = await FetchExistingRecordsAsync(categoryConfig);
                        existingRecordsFetched = true;
                        _log.Information("Fetched {Count} existing {Category} records for PUT retry matching",
                            existingRecords.Count, category);
                    }

                    var existingId = FindExistingRecordByUpdateMatch(existingRecords, categoryConfig, matchedField, matchValue);

                    if (existingId is null && !string.IsNullOrEmpty(secondaryUpdateMatchCloudKey))
                    {
                        var secondaryMatchValue = ResolveRecordValue(record, secondaryUpdateMatchCloudKey);
                        if (!string.IsNullOrWhiteSpace(secondaryMatchValue))
                        {
                            existingId = FindExistingRecordByUpdateMatch(existingRecords, categoryConfig, secondaryUpdateMatchCloudKey, secondaryMatchValue);
                            if (existingId is not null)
                            {
                                matchedField = secondaryUpdateMatchCloudKey;
                                matchValue = secondaryMatchValue;
                            }
                        }
                    }

                    if (existingId is not null)
                    {
                        var endpoint = categoryConfig.PutEndpoint.Replace("{id}", existingId);
                        await SendJsonAsync(HttpMethod.Put, endpoint, record);
                        result.Updated++;
                    }
                    else
                    {
                        result.Failed++;
                        result.Errors.Add($"POST failed and no existing record matched '{matchedField}'='{matchValue}' for PUT retry.");
                    }
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"PUT retry failed: {ex.Message}");
                    _log.Warning(ex, "PUT retry failed for {Category} record", category);
                }
            }
        }

        _log.Information(
            "Push complete for {Category} on {Target}: {Created} created, {Updated} updated, {Failed} failed",
            category, _target.Name, result.Created, result.Updated, result.Failed);

        return result;
    }

    /// <summary>
    /// Resolves which cloud field key in the push record contains the value from UpdateMatchAdField.
    /// Looks through FieldMappings to find the mapping where AdAttributes contains UpdateMatchAdField,
    /// then returns the corresponding CloudField.
    /// Returns null if UpdateMatchAdField is not configured or no mapping is found.
    /// </summary>
    protected string? ResolveUpdateMatchCloudKey(SyncCategoryConfig categoryConfig)
    {
        if (string.IsNullOrEmpty(categoryConfig.UpdateMatchSourceField))
            return null;

        foreach (var mapping in categoryConfig.FieldMappings)
        {
            if (mapping.SourceFields.Any(f =>
                string.Equals(f, categoryConfig.UpdateMatchSourceField, StringComparison.OrdinalIgnoreCase)))
            {
                _log.Debug("Resolved UpdateMatchSourceField '{SourceField}' -> cloud key '{CloudField}'",
                    categoryConfig.UpdateMatchSourceField, mapping.CloudField);
                return mapping.CloudField;
            }
        }

        _log.Warning("UpdateMatchSourceField '{Field}' not found in any field mapping — PUT retry disabled",
            categoryConfig.UpdateMatchSourceField);
        return null;
    }

    /// <summary>
    /// Resolves the cloud field key for a specified source match field.
    /// </summary>
    protected string? ResolveUpdateMatchCloudKey(SyncCategoryConfig categoryConfig, string? sourceField)
    {
        if (string.IsNullOrEmpty(sourceField))
            return null;

        foreach (var mapping in categoryConfig.FieldMappings)
        {
            if (mapping.SourceFields.Any(f =>
                string.Equals(f, sourceField, StringComparison.OrdinalIgnoreCase)))
            {
                _log.Debug("Resolved UpdateMatchSourceField '{SourceField}' -> cloud key '{CloudField}'",
                    sourceField, mapping.CloudField);
                return mapping.CloudField;
            }
        }

        _log.Warning("UpdateMatchSourceField '{Field}' not found in any field mapping; PUT retry disabled", sourceField);
        return null;
    }

    /// <summary>
    /// Searches existing cloud records for one where UpdateMatchCloudField equals the given value,
    /// and returns the CloudIdField from that record.
    /// Handles dotted field paths like "details.objectsid" by traversing nested objects.
    /// </summary>
    protected virtual string? FindExistingRecordByUpdateMatch(
        List<JsonObject> existingRecords,
        SyncCategoryConfig categoryConfig,
        string matchValue)
    {
        var matchField = categoryConfig.UpdateMatchCloudField;

        foreach (var record in existingRecords)
        {
            var cloudVal = ResolveJsonValue(record, matchField);

            if (cloudVal is not null
                && string.Equals(cloudVal, matchValue, StringComparison.OrdinalIgnoreCase))
            {
                if (record.TryGetPropertyValue(categoryConfig.CloudIdField, out var idVal) && idVal is not null)
                    return idVal.ToString();

                var keys = string.Join(", ", record.Select(p => p.Key));
                _log.Warning(
                    "Update match found for '{Value}' but ID field '{IdField}' not present. Available: [{Keys}]",
                    matchValue, categoryConfig.CloudIdField, keys);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches existing cloud records for one where the specified cloud field equals the given value,
    /// and returns the CloudIdField from that record.
    /// Handles dotted field paths like "details.objectsid" by traversing nested objects.
    /// </summary>
    protected virtual string? FindExistingRecordByUpdateMatch(
        List<JsonObject> existingRecords,
        SyncCategoryConfig categoryConfig,
        string lookupField,
        string matchValue)
    {
        foreach (var record in existingRecords)
        {
            var cloudVal = ResolveJsonValue(record, lookupField);

            if (cloudVal is not null
                && string.Equals(cloudVal, matchValue, StringComparison.OrdinalIgnoreCase))
            {
                if (record.TryGetPropertyValue(categoryConfig.CloudIdField, out var idVal) && idVal is not null)
                    return idVal.ToString();

                var keys = string.Join(", ", record.Select(p => p.Key));
                _log.Warning(
                    "Update match found for '{Value}' but ID field '{IdField}' not present. Available: [{Keys}]",
                    matchValue, categoryConfig.CloudIdField, keys);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a value from a JsonObject, handling dotted paths like "details.objectsid"
    /// by traversing into nested objects.
    /// </summary>
    protected static string? ResolveJsonValue(JsonObject record, string fieldPath)
    {
        // Try flat key first
        if (record.TryGetPropertyValue(fieldPath, out var directVal) && directVal is not null)
            return directVal.ToString();

        // Handle dotted paths: "details.objectsid" -> record["details"]["objectsid"]
        var dotIndex = fieldPath.IndexOf('.');
        if (dotIndex > 0)
        {
            var parentKey = fieldPath[..dotIndex];
            var childKey = fieldPath[(dotIndex + 1)..];

            if (record.TryGetPropertyValue(parentKey, out var parentNode) && parentNode is JsonObject nested)
            {
                if (nested.TryGetPropertyValue(childKey, out var nestedVal) && nestedVal is not null)
                    return nestedVal.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Override to modify records before pushing (e.g., add provider-specific fields like Reftab's cid).
    /// </summary>
    protected virtual void PreparePushRecord(SyncCategoryConfig categoryConfig, Dictionary<string, object> record)
    {
        // Base implementation does nothing
    }

    /// <summary>
    /// Public accessor for PreparePushRecord, used by the app for test sync JSON preview.
    /// Applies provider-specific record modifications (e.g., Reftab cid, clid, details nesting).
    /// </summary>
    public void PrepareRecordForPreview(SyncCategoryConfig categoryConfig, Dictionary<string, object> record)
    {
        PreparePushRecord(categoryConfig, record);
    }

    protected HttpRequestMessage BuildRequest(HttpMethod method, string endpoint, HttpContent? content = null)
    {
        var url = _target.Connection.BaseUrl.TrimEnd('/') + endpoint;
        var request = new HttpRequestMessage(method, url) { Content = content };

        ApplyAuthentication(request, method, endpoint);
        return request;
    }

    protected virtual void ApplyAuthentication(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;

        switch (conn.AuthType)
        {
            case AuthType.ApiKey:
                var apiKeyValue = conn.ApiKeyFormat
                    .Replace("{key}", conn.ApiKey)
                    .Replace("{secret}", conn.ApiSecret);
                request.Headers.TryAddWithoutValidation(conn.ApiKeyHeader, apiKeyValue);
                break;

            case AuthType.BearerToken:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.ApiKey);
                break;

            case AuthType.BasicAuth:
                var basicCreds = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{conn.BasicUsername}:{conn.BasicPassword}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCreds);
                break;

            case AuthType.Hmac:
                ApplyStandardHmacAuth(request, method, endpoint);
                break;
        }
    }

    /// <summary>
    /// Standard HMAC authentication (AWS-style). Override for custom HMAC schemes.
    /// </summary>
    protected virtual void ApplyStandardHmacAuth(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var contentMd5 = string.Empty;

        if (request.Content is not null)
        {
            var contentBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (contentBytes.Length > 0)
            {
                contentMd5 = Convert.ToBase64String(MD5.HashData(contentBytes));
            }
        }

        var stringToSign = $"{method.Method}\n{conn.ContentType}\n{contentMd5}\n{endpoint}\n{timestamp}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(conn.ApiSecret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var signature = Convert.ToBase64String(signatureBytes);

        request.Headers.TryAddWithoutValidation("Authorization", $"HMAC {conn.ApiKey}:{signature}");
        request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp);
    }

    /// <summary>
    /// When true, SendJsonAsync captures the payload instead of sending it.
    /// </summary>
    public bool DryRunMode { get; set; }

    /// <summary>
    /// Captured payloads from dry-run mode. Each entry contains the method, endpoint, and exact JSON body.
    /// </summary>
    public List<DryRunCapture> DryRunCaptures { get; } = [];

    protected async Task<string> SendJsonAsync(HttpMethod method, string endpoint, object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        // Dry-run: capture exactly what would be sent, skip HTTP
        if (DryRunMode)
        {
            _log.Information("[DRY RUN] Would send {Method} {Endpoint} | Body: {Json}", method.Method, endpoint, json);
            DryRunCaptures.Add(new DryRunCapture
            {
                Method = method.Method,
                Endpoint = endpoint,
                JsonBody = json
            });
            return "{}"; // Simulate empty success response
        }

        _log.Information("Sending {Method} {Endpoint} | Body: {Json}", method.Method, endpoint, json);

        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var request = BuildRequest(method, endpoint, content);
        var response = await _httpClient.SendAsync(request);

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _log.Warning("Request failed: {Method} {Endpoint} -> {StatusCode} {Body}",
                method.Method, endpoint, (int)response.StatusCode, Truncate(responseBody, 300));

            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(responseBody, 500)}");
        }

        _log.Information("Request succeeded: {Method} {Endpoint} -> {StatusCode}",
            method.Method, endpoint, (int)response.StatusCode);

        return responseBody;
    }

    protected static List<string> ExtractFieldNames(string rawJson, string itemsPath)
    {
        var fields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var doc = JsonNode.Parse(rawJson);
            if (doc is null) return [];

            var items = ResolveItemsPath(doc, itemsPath);
            if (items is null) return [];

            foreach (var item in items)
            {
                if (item is JsonObject obj)
                {
                    foreach (var prop in obj)
                    {
                        fields.Add(prop.Key);

                        if (prop.Value is JsonObject nested)
                        {
                            foreach (var nestedProp in nested)
                            {
                                fields.Add($"{prop.Key}.{nestedProp.Key}");
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Failed to parse JSON for field extraction");
        }

        return fields.ToList();
    }

    protected static JsonArray? ResolveItemsPath(JsonNode root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
            return root as JsonArray;

        var segments = path.TrimStart('$', '.').Split('.');
        JsonNode? current = root;

        foreach (var segment in segments)
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next))
                current = next;
            else
                return null;
        }

        return current as JsonArray;
    }

    protected virtual async Task<List<JsonObject>> FetchExistingRecordsAsync(SyncCategoryConfig categoryConfig)
    {
        if (string.IsNullOrEmpty(categoryConfig.GetEndpoint))
            return [];

        try
        {
            var request = BuildRequest(HttpMethod.Get, categoryConfig.GetEndpoint);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("FetchExisting failed: {StatusCode}", (int)response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            _log.Information("FetchExisting raw response length: {Length} chars", json.Length);

            var doc = JsonNode.Parse(json);
            if (doc is null) return [];

            var items = ResolveItemsPath(doc, categoryConfig.ResponseItemsPath);
            if (items is null)
            {
                _log.Warning("FetchExisting: ResolveItemsPath returned null for path '{Path}'",
                    categoryConfig.ResponseItemsPath);
                return [];
            }

            var records = items.OfType<JsonObject>().ToList();

            _log.Information("FetchExisting: {Count} records from {Endpoint}. UpdateMatchCloud: '{MatchField}', ID field: '{IdField}'",
                records.Count, categoryConfig.GetEndpoint, categoryConfig.UpdateMatchCloudField, categoryConfig.CloudIdField);

            // Log first record's full key list so we can verify field names
            if (records.Count > 0)
            {
                var firstKeys = string.Join(", ", records[0].Select(p => $"{p.Key}={p.Value}"));
                _log.Information("FetchExisting first record: {Record}", firstKeys);
            }

            return records;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not fetch existing records for matching; will create all as new");
            return [];
        }
    }

    /// <summary>
    /// Flattens a JsonObject into a flat string dictionary.
    /// Nested objects use dot notation (e.g., "details.model").
    /// Used by GetRecordsAsync to produce TransformEngine-compatible records.
    /// </summary>
    protected static void FlattenJsonObjectToStrings(
        JsonObject obj,
        Dictionary<string, string> result,
        string? prefix)
    {
        foreach (var prop in obj)
        {
            var key = prefix is null ? prop.Key : $"{prefix}.{prop.Key}";

            switch (prop.Value)
            {
                case JsonObject nested:
                    FlattenJsonObjectToStrings(nested, result, key);
                    break;
                case JsonArray arr:
                    result[key] = arr.ToJsonString();
                    break;
                case null:
                    result[key] = string.Empty;
                    break;
                default:
                    result[key] = prop.Value.ToString();
                    break;
            }
        }
    }

    protected SyncCategoryConfig GetCategoryConfig(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "assets"            => _target.Assets,
            "users" or "loanees" => _target.Users,
            _ => throw new ArgumentException($"Unknown category: {category}", nameof(category))
        };
    }

    protected static string? ResolveRecordValue(Dictionary<string, object> record, string key)
    {
        if (record.TryGetValue(key, out var val))
            return val?.ToString();

        // Handle "details.X" nested keys stored flat
        foreach (var kvp in record)
        {
            if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value?.ToString();
        }

        return null;
    }

    protected static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
