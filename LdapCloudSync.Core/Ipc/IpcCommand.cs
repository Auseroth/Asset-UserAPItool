using System.Text.Json;
using System.Text.Json.Serialization;

namespace LdapCloudSync.Core.Ipc;

/// <summary>
/// Commands sent from the WPF app to the Windows Service over named pipes.
/// </summary>
public sealed class IpcCommand
{
    public IpcCommandType Type { get; set; }

    /// <summary>
    /// Target ID for sync commands. Null = all targets.
    /// </summary>
    public string? TargetId { get; set; }

    /// <summary>
    /// Category for sync commands: "assets", "users", or "both".
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Max records for test sync.
    /// </summary>
    public int MaxRecords { get; set; } = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static IpcCommand? Deserialize(string json) =>
        JsonSerializer.Deserialize<IpcCommand>(json, JsonOptions);
}

public enum IpcCommandType
{
    /// <summary>Run a full sync now.</summary>
    RunSync,

    /// <summary>Run a test sync (limited records).</summary>
    RunTestSync,

    /// <summary>Reload configuration from disk.</summary>
    ReloadConfig,

    /// <summary>Report current service status.</summary>
    GetStatus,

    /// <summary>Gracefully stop the service.</summary>
    Stop
}