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
/// Provider-specific clients (Reftab, etc.) inherit and override as needed.
/// </summary>
public abstract class BaseCloudClient : ICloudClient, IDisposable
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

    public virtual async Task<SyncResult> PushRecordsAsync(
        string category,
        IReadOnlyList<Dictionary<string, object>> records)
    {
        var categoryConfig = GetCategoryConfig(category);
        var result = new SyncResult();

        // Skip the real HTTP fetch in dry-run mode — assume all records are new
        var existingRecords = DryRunMode
            ? []
            : await FetchExistingRecordsAsync(categoryConfig);

        _log.Information("Fetched {Count} existing {Category} records for matching",
            existingRecords.Count, category);

        foreach (var record in records)
        {
            try
            {
                // Allow subclasses to modify record before push
                PreparePushRecord(categoryConfig, record);

                // Use the full record for matching (allows multi-field fallback)
                var existingId = FindExistingRecordId(existingRecords, categoryConfig, record);

                if (existingId is not null)
                {
                    var endpoint = categoryConfig.PutEndpoint.Replace("{id}", existingId);
                    await SendJsonAsync(HttpMethod.Put, endpoint, record);
                    result.Updated++;
                    _log.Debug("Updated {Category} record: {Id}", category, existingId);
                }
                else
                {
                    // Skip records missing required match field (e.g., no email for loanees)
                    var matchValue = record.TryGetValue(categoryConfig.CloudMatchField, out var mv)
                        ? mv?.ToString() ?? string.Empty
                        : string.Empty;

                    if (string.IsNullOrEmpty(matchValue))
                    {
                        _log.Warning("Skipping {Category} record: match field '{Field}' is empty",
                            category, categoryConfig.CloudMatchField);
                        result.Failed++;
                        result.Errors.Add($"Match field '{categoryConfig.CloudMatchField}' is empty — record skipped.");
                        continue;
                    }

                    await SendJsonAsync(HttpMethod.Post, categoryConfig.PostEndpoint, record);
                    result.Created++;
                    _log.Debug("Created {Category} record: {MatchValue}", category, matchValue);
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add(ex.Message);
                _log.Warning(ex, "Failed to push {Category} record", category);
            }
        }

        _log.Information(
            "Push complete for {Category} on {Target}: {Created} created, {Updated} updated, {Failed} failed",
            category, _target.Name, result.Created, result.Updated, result.Failed);

        return result;
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

            _log.Information("FetchExisting: {Count} records from {Endpoint}. Match field: '{MatchField}', ID field: '{IdField}'",
                records.Count, categoryConfig.GetEndpoint, categoryConfig.CloudMatchField, categoryConfig.CloudIdField);

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

    protected virtual string? FindExistingRecordId(
        List<JsonObject> existingRecords,
        SyncCategoryConfig categoryConfig,
        Dictionary<string, object> pushRecord)
    {
        // Get the primary match value from the record being pushed
        var matchValue = pushRecord.TryGetValue(categoryConfig.CloudMatchField, out var mv)
            ? mv?.ToString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(matchValue) || string.IsNullOrEmpty(categoryConfig.CloudMatchField))
            return null;

        foreach (var record in existingRecords)
        {
            if (record.TryGetPropertyValue(categoryConfig.CloudMatchField, out var cloudVal)
                && string.Equals(cloudVal?.ToString(), matchValue, StringComparison.OrdinalIgnoreCase))
            {
                if (record.TryGetPropertyValue(categoryConfig.CloudIdField, out var idVal) && idVal is not null)
                    return idVal.ToString();

                var keys = string.Join(", ", record.Select(p => p.Key));
                Log.Warning(
                    "Match found for '{MatchValue}' but ID field '{IdField}' not present. Available fields: [{Keys}]",
                    matchValue, categoryConfig.CloudIdField, keys);
                return null;
            }
        }

        return null;
    }

    protected SyncCategoryConfig GetCategoryConfig(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "assets" => _target.Assets,
            "users" or "loanees" => _target.Users,
            _ => throw new ArgumentException($"Unknown category: {category}", nameof(category))
        };
    }

    protected static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    public virtual void Dispose() => _httpClient.Dispose();
}