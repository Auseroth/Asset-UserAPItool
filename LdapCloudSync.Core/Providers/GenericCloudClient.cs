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
            // Try the assets GET endpoint first, then users, to verify connectivity
            var endpoint = !string.IsNullOrEmpty(_target.Assets.GetEndpoint)
                ? _target.Assets.GetEndpoint
                : _target.Users.GetEndpoint;

            if (string.IsNullOrEmpty(endpoint))
                return (false, "No GET endpoint configured to test connectivity.");

            var request = BuildRequest(HttpMethod.Get, endpoint);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return (true, $"Connected successfully. Status: {(int)response.StatusCode} {response.ReasonPhrase}");

            var body = await response.Content.ReadAsStringAsync();
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
    /// Applies HMAC authentication following the Reftab pattern:
    /// Signature = HMAC(secret, "method,contentType,contentMd5,uri,timestamp")
    /// This is a common HMAC REST pattern; works generically with configurable algorithm.
    /// </summary>
    private void ApplyHmacAuthentication(HttpRequestMessage request, HttpMethod method, string endpoint)
    {
        var conn = _target.Connection;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var contentMd5 = string.Empty;

        if (request.Content is not null)
        {
            var contentBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            contentMd5 = Convert.ToBase64String(MD5.HashData(contentBytes));
        }

        var stringToSign = $"{method.Method},{conn.ContentType},{contentMd5},{endpoint},{timestamp}";

        using var hmac = CreateHmac(conn.HmacAlgorithm, Encoding.UTF8.GetBytes(conn.ApiSecret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var signature = Convert.ToBase64String(signatureBytes);

        request.Headers.TryAddWithoutValidation("x-public-key", conn.ApiKey);
        request.Headers.TryAddWithoutValidation("x-signature", signature);
        request.Headers.TryAddWithoutValidation("x-timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("Content-Type", conn.ContentType);
    }

    private static HMAC CreateHmac(string algorithm, byte[] key)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "HMACSHA256" => new HMACSHA256(key),
            "HMACSHA384" => new HMACSHA384(key),
            "HMACSHA512" => new HMACSHA512(key),
            "HMACSHA1" => new HMACSHA1(key),
            _ => new HMACSHA256(key)
        };
    }

    private async Task<string> SendJsonAsync(HttpMethod method, string endpoint, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, _target.Connection.ContentType);
        var request = BuildRequest(method, endpoint, content);
        var response = await _httpClient.SendAsync(request);

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(responseBody, 500)}");
        }

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

    public void Dispose() => _httpClient.Dispose();
}