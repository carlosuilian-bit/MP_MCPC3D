using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Sdk;

public sealed class NamedPipeRpcClient
{
    private readonly string _serverName;

    public NamedPipeRpcClient(string serverName = ".")
    {
        _serverName = serverName;
    }

    public async Task<JsonRpcResponse> SendAsync(
        string pipeName,
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var request = new JsonRpcRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Method = method,
            Params = JsonContractOptions.ToJsonNode(parameters)
        };

        try
        {
            await using var pipe = new NamedPipeClientStream(
                _serverName,
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync((int)Math.Max(1, timeout.TotalMilliseconds), timeoutCts.Token);

            await using var writerStream = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            var json = JsonSerializer.Serialize(request, JsonContractOptions.Default);
            await writerStream.WriteLineAsync(json.AsMemory(), timeoutCts.Token);

            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new BridgeRpcException(ErrorCode.BridgeUnavailable, "Bridge disconnected before returning a response.");
            }

            return JsonSerializer.Deserialize<JsonRpcResponse>(line, JsonContractOptions.Default)
                ?? throw new BridgeRpcException(ErrorCode.InternalError, "Bridge returned an empty JSON-RPC response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BridgeRpcException(ErrorCode.Timeout, $"Timed out calling pipe '{pipeName}' method '{method}'.");
        }
        catch (TimeoutException ex)
        {
            throw new BridgeRpcException(ErrorCode.Timeout, ex.Message);
        }
        catch (IOException ex)
        {
            throw new BridgeRpcException(ErrorCode.BridgeUnavailable, ex.Message);
        }
    }

    public async Task<T> SendAsync<T>(
        string pipeName,
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(pipeName, method, parameters, timeout, cancellationToken);
        return response.ReadResult<T>();
    }
}

