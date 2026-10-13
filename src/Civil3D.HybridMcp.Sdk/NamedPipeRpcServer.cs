using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Sdk;

public sealed class NamedPipeRpcServer : IAsyncDisposable
{
    private readonly Func<JsonRpcRequest, CancellationToken, Task<JsonRpcResponse>> _handler;
    private readonly ConcurrentBag<Task> _connectionTasks = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public NamedPipeRpcServer(
        string pipeName,
        Func<JsonRpcRequest, CancellationToken, Task<JsonRpcResponse>> handler)
    {
        PipeName = pipeName;
        _handler = handler;
    }

    public string PipeName { get; }
    public bool IsRunning => _acceptLoop is { IsCompleted: false };

    public void Start()
    {
        if (_acceptLoop is { IsCompleted: false })
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }

        await Task.WhenAll(_connectionTasks.Where(task => !task.IsCompleted)).WaitAsync(TimeSpan.FromSeconds(2))
            .ContinueWith(_ => { }, TaskScheduler.Default)
            .ConfigureAwait(false);

        _cts.Dispose();
        _cts = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                throw;
            }

            _connectionTasks.Add(Task.Run(() => HandleConnectionAsync(pipe, cancellationToken), cancellationToken));
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };

            while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                JsonRpcResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonContractOptions.Default)
                        ?? throw new InvalidOperationException("Request was empty.");
                    response = await _handler(request, cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    response = JsonRpcResponse.Failure("unknown", ErrorCode.InvalidRequest, ex.Message);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    response = JsonRpcResponse.Failure("unknown", ErrorCode.InternalError, ex.Message);
                }

                var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
                await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

