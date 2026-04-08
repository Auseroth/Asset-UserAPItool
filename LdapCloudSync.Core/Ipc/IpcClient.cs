using System.IO.Pipes;
using Serilog;

namespace LdapCloudSync.Core.Ipc;

/// <summary>
/// Named pipe client used by the WPF app to send commands to the running service.
/// </summary>
public static class IpcClient
{
    private static readonly ILogger _log = Log.Logger;

    /// <summary>
    /// Sends a command to the service and returns the response.
    /// </summary>
    public static async Task<IpcResponse> SendCommandAsync(
        IpcCommand command,
        int timeoutMs = 10000)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(
                ".",
                IpcServer.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipeClient.ConnectAsync(timeoutMs);

            var writer = new StreamWriter(pipeClient, leaveOpen: true) { AutoFlush = true };
            var reader = new StreamReader(pipeClient, leaveOpen: true);

            await writer.WriteLineAsync(command.Serialize());
            await writer.FlushAsync();

            var responseJson = await reader.ReadLineAsync();

            writer.Dispose();
            reader.Dispose();

            if (string.IsNullOrEmpty(responseJson))
                return IpcResponse.Error("Empty response from service.");

            return IpcResponse.Deserialize(responseJson)
                ?? IpcResponse.Error("Failed to parse service response.");
        }
        catch (TimeoutException)
        {
            _log.Warning("IPC connection timed out. Is the service running?");
            return IpcResponse.Error("Could not connect to the service. Is it running?");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "IPC communication error");
            return IpcResponse.Error($"Communication error: {ex.Message}");
        }
    }

    /// <summary>
    /// Convenience: trigger a full sync on the service.
    /// </summary>
    public static Task<IpcResponse> TriggerSyncAsync(string? targetId = null, string category = "both") =>
        SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.RunSync,
            TargetId = targetId,
            Category = category
        });

    /// <summary>
    /// Convenience: trigger a test sync (first N records).
    /// </summary>
    public static Task<IpcResponse> TriggerTestSyncAsync(string? targetId = null, string category = "both", int maxRecords = 10) =>
        SendCommandAsync(new IpcCommand
        {
            Type = IpcCommandType.RunTestSync,
            TargetId = targetId,
            Category = category,
            MaxRecords = maxRecords
        });

    /// <summary>
    /// Convenience: tell the service to reload config from disk.
    /// </summary>
    public static Task<IpcResponse> ReloadConfigAsync() =>
        SendCommandAsync(new IpcCommand { Type = IpcCommandType.ReloadConfig });

    /// <summary>
    /// Convenience: check if the service is alive and get status.
    /// </summary>
    public static Task<IpcResponse> GetStatusAsync() =>
        SendCommandAsync(new IpcCommand { Type = IpcCommandType.GetStatus });
}