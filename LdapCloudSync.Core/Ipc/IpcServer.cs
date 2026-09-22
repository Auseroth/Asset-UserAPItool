using System.IO.Pipes;
using Serilog;

namespace LdapCloudSync.Core.Ipc;

/// <summary>
/// Named pipe server running inside the Windows Service.
/// Listens for commands from the WPF configuration app.
/// </summary>
public sealed class IpcServer : IDisposable
{
    public const string PipeName = "Connexus_ServicePipe";

    private readonly Func<IpcCommand, Task<IpcResponse>> _commandHandler;
    private readonly ILogger _log;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public IpcServer(Func<IpcCommand, Task<IpcResponse>> commandHandler, ILogger? logger = null)
    {
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _log = logger ?? Log.Logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = ListenLoopAsync(_cts.Token);
        _log.Information("IPC server started on pipe: {PipeName}", PipeName);
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_listenTask is not null)
            {
                try { await _listenTask; } catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }

        _log.Information("IPC server stopped");
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                _log.Debug("IPC client connected");

                var reader = new StreamReader(pipeServer, leaveOpen: true);
                var writer = new StreamWriter(pipeServer, leaveOpen: true) { AutoFlush = true };

                try
                {
                    var requestJson = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(requestJson))
                        continue;

                    var command = IpcCommand.Deserialize(requestJson);
                    if (command is null)
                    {
                        var errorResponse = IpcResponse.Error("Invalid command format.");
                        await writer.WriteLineAsync(errorResponse.Serialize());
                        await writer.FlushAsync();
                        continue;
                    }

                    _log.Information("IPC command received: {CommandType}", command.Type);
                    var response = await _commandHandler(command);
                    await writer.WriteLineAsync(response.Serialize());
                    await writer.FlushAsync();
                }
                finally
                {
                    reader.Dispose();
                    writer.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "IPC server error");
                // Brief delay before retrying to avoid a tight error loop
                try { await Task.Delay(1000, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}