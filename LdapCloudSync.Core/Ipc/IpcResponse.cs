using System.Text.Json;
using System.Text.Json.Serialization;

namespace LdapCloudSync.Core.Ipc;

/// <summary>
/// Response sent from the service back to the WPF app.
/// </summary>
public sealed class IpcResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional structured data (e.g., sync results as JSON).
    /// </summary>
    public string? Data { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static IpcResponse? Deserialize(string json) =>
        JsonSerializer.Deserialize<IpcResponse>(json, JsonOptions);

    public static IpcResponse Ok(string message, string? data = null) =>
        new() { Success = true, Message = message, Data = data };

    public static IpcResponse Error(string message) =>
        new() { Success = false, Message = message };
}