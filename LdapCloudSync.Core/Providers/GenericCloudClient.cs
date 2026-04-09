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
/// Generic ICloudClient implementation driven entirely by CloudTargetConfig.
/// Supports ApiKey, BearerToken, BasicAuth, and HMAC authentication.
/// No provider-specific code -- works with any REST API.
/// </summary>
public sealed class GenericCloudClient : ICloudClient, IDisposable
{
    private readonly CloudTargetConfig _target;
    private readonly HttpClient _httpClient;
    private readonly ILogger _log;

    public GenericCloudClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _httpClient = httpClient ?? new HttpClient();
        _log = logger ?? Log.Logger;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync()
    {
        try
        {
            var endpoint = !string.IsNullOrEmpty(_target.Assets.GetEndpoint)
                ? _target.Assets.GetEndpoint
                : _target.Users.GetEndpoint;

            if (string.IsNullOrEmpty(endpoint))
                return (false, "No GET endpoint configured to test connectivity.");

            var conn = _target.Connection;

            if (conn.AuthType == AuthType.Hmac)
            {
                if (string.IsNullOrEmpty(conn.ApiKey))
                    return (false, "API Key (public key) is empty. Enter your public key.");
                if (string.IsNullOrEmpty(conn.ApiSecret))
                    return (false, "API Secret is empty. Enter your secret key.");
            }

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

            if ((int)response.StatusCode == 401 && conn.AuthType == AuthType.Hmac)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var signPreview = $"GET,{conn.ContentType},,{endpoint},{timestamp}";
                return (false, $"HTTP 401 Unauthorized. " +
                    $"Signed: '{signPreview}' | " +
                    $"Key: '{conn.ApiKey[..Math.Min(8, conn.ApiKey.Length)]}...' | " +
                    $"Secret length: {conn.ApiSecret.Length} | " +
                    $"Response: {Truncate(body, 150)}");
            }

            return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 500)}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Cloud connection test failed for target {TargetName}", _target.Name);
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    public async Task<(IReadOnlyList<string> Fields, string RawResponse)> DiscoverFieldsAsync(string category)
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

    public async Task<SyncResult> PushRecordsAsync(
        string category,
        IReadOnlyList<Dictionary<string, object>> records)
    {
        var categoryConfig = GetCategoryConfig(category);
        var result = new SyncResult();

        // First, fetch existing records to determine create vs. update
        var existingRecords = await FetchExistingRecordsAsync(categoryConfig);

        foreach (var record in records)
        {
            try
            {
                // Add Reftab category ID if configured
                if (categoryConfig.TargetCategoryId > 0 && !record.ContainsKey("cid"))
                {
                    record["cid"] = categoryConfig.TargetCategoryId;
                }

                var matchValue = record.TryGetValue(categoryConfig.CloudMatchField, out var mv)
                    ? mv?.ToString() ?? string.Empty
                    : string.Empty;

                var existingId = FindExistingRecordId(existingRecords, categoryConfig, matchValue);
                
                if (existingId is not null)
                {
                    // Update existing record
                    var endpoint = categoryConfig.PutEndpoint.Replace("{id}", existingId);
                    await SendJsonAsync(HttpMethod.Put, endpoint, record);
                    result.Updated++;
                    _log.Debug("Updated {Category} record: {MatchValue}", category, matchValue);
                }
                else
                {
                    // Create new record
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

    #region HTTP Helpers

    private HttpRequestMessage BuildRequest(HttpMethod method, string endpoint, HttpContent? content = null)
    {
        var url = _target.Connection.BaseUrl.TrimEnd('/') + endpoint;
        var request = new HttpRequestMessage(method, url) { Content = content };

        // Pass the relative endpoint for HMAC signing (Reftab signs the endpoint, not the full URI path)
        ApplyAuthentication(request, method, endpoint);
        return request;
    }

    private void ApplyAuthentication(HttpRequestMessage request, HttpMethod method, string endpoint)
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
                ApplyHmacAuthentication(request, method, endpoint);
                break;
        }
    }

    /// <summary>
    /// Applies HMAC authentication following Reftab's actual pattern:
    /// 1. Date = RFC 2822 format (e.g., "Mon, 09 Apr 2026 13:45:00 GMT")
    /// 2. String-to-sign = "METHOD\n\n\nDATE\nFULL_URL"
    /// 3. HMAC = SHA256(secret, string-to-sign)
    /// 4. Signature = Base64(UTF8(ToHex(hmacBytes)))  <-- Reftab's special encoding
    /// 5. Authorization header = "RT {publicKey}:{signature}"
    /// </summary>
    private void ApplyHmacAuthentication(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;
        
        // 1. RFC 2822 date format (matching Python's email.utils.formatdate(usegmt=True))
        var rtDate = DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss", 
            System.Globalization.CultureInfo.InvariantCulture) + " GMT";
        
        // 2. Full URL (not just path)
        var fullUrl = conn.BaseUrl.TrimEnd('/') + endpoint;
        
        // 3. String to sign: METHOD\n\n\nDATE\nURL
        var stringToSign = $"{method.Method}\n\n\n{rtDate}\n{fullUrl}";
        
        _log.Information("Reftab Auth: {Method} {Url} | Date: {Date}", 
            method.Method, fullUrl, rtDate);
        
        // 4. Compute HMAC-SHA256
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(conn.ApiSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        
        // 5. Convert to lowercase hex string
        var hexHash = string.Concat(hashBytes.Select(b => b.ToString("x2")));
        
        // 6. Base64-encode the hex string (Reftab's unique approach)
        var signature = Convert.ToBase64String(Encoding.UTF8.GetBytes(hexHash));
        
        // 7. Set headers
        request.Headers.TryAddWithoutValidation("x-rt-date", rtDate);
        request.Headers.TryAddWithoutValidation("Authorization", $"RT {conn.ApiKey}:{signature}");
    }

    private async Task<string> SendJsonAsync(HttpMethod method, string endpoint, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        _log.Information("Sending {Method} {Endpoint} | Body length: {Length}", method.Method, endpoint, json.Length);

        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
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

    #endregion

    #region JSON Parsing Helpers

    /// <summary>
    /// Extracts all unique field names from a JSON response using the configured items path.
    /// </summary>
    private static List<string> ExtractFieldNames(string rawJson, string itemsPath)
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

                        // Also include nested object fields with dot notation
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

    /// <summary>
    /// Resolves a simplified JSONPath-like expression to find the items array.
    /// Supports: "$" (root), "$.property", "$.property.nested"
    /// </summary>
    private static JsonArray? ResolveItemsPath(JsonNode root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
        {
            return root as JsonArray;
        }

        // Strip leading "$."
        var segments = path.TrimStart('$', '.').Split('.');
        JsonNode? current = root;

        foreach (var segment in segments)
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next))
            {
                current = next;
            }
            else
            {
                return null;
            }
        }

        return current as JsonArray;
    }

    /// <summary>
    /// Fetches existing records from the cloud to support create-vs-update logic.
    /// </summary>
    private async Task<List<JsonObject>> FetchExistingRecordsAsync(SyncCategoryConfig categoryConfig)
    {
        if (string.IsNullOrEmpty(categoryConfig.GetEndpoint))
            return [];

        try
        {
            var request = BuildRequest(HttpMethod.Get, categoryConfig.GetEndpoint);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonNode.Parse(json);
            if (doc is null) return [];

            var items = ResolveItemsPath(doc, categoryConfig.ResponseItemsPath);
            if (items is null) return [];

            return items.OfType<JsonObject>().ToList();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Could not fetch existing records for matching; will create all as new");
            return [];
        }
    }

    /// <summary>
    /// Looks up an existing cloud record ID by matching field value.
    /// </summary>
    private static string? FindExistingRecordId(
        List<JsonObject> existingRecords,
        SyncCategoryConfig categoryConfig,
        string matchValue)
    {
        if (string.IsNullOrEmpty(matchValue) || string.IsNullOrEmpty(categoryConfig.CloudMatchField))
            return null;

        foreach (var record in existingRecords)
        {
            if (record.TryGetPropertyValue(categoryConfig.CloudMatchField, out var cloudVal)
                && string.Equals(cloudVal?.ToString(), matchValue, StringComparison.OrdinalIgnoreCase))
            {
                if (record.TryGetPropertyValue(categoryConfig.CloudIdField, out var idVal))
                    return idVal?.ToString();
            }
        }

        return null;
    }

    #endregion

    private SyncCategoryConfig GetCategoryConfig(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "assets" => _target.Assets,
            "users" or "loanees" => _target.Users,
            _ => throw new ArgumentException($"Unknown category: {category}", nameof(category))
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    /// <summary>
    /// Fetches the list of asset categories from Reftab.
    /// Returns list of (id, name) tuples.
    /// </summary>
    public async Task<List<(int Id, string Name)>> GetAssetCategoriesAsync()
    {
        if (!_target.Connection.BaseUrl.Contains("reftab", StringComparison.OrdinalIgnoreCase))
        {
            _log.Warning("GetAssetCategories called on non-Reftab target");
            return [];
        }

        try
        {
            var request = BuildRequest(HttpMethod.Get, "/categories");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonNode.Parse(json);
            
            if (doc is not JsonArray categories)
                return [];

            var result = new List<(int, string)>();
            foreach (var cat in categories.OfType<JsonObject>())
            {
                if (cat.TryGetPropertyValue("id", out var idNode) && 
                    cat.TryGetPropertyValue("name", out var nameNode) &&
                    idNode?.GetValue<int>() is int id &&
                    nameNode?.ToString() is string name)
                {
                    result.Add((id, name));
                }
            }

            _log.Information("Discovered {Count} asset categories from Reftab", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to fetch asset categories");
            return [];
        }
    }

    public void Dispose() => _httpClient.Dispose();
}