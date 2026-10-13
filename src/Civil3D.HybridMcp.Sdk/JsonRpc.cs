using System.Text.Json.Nodes;
using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Sdk;

public sealed record JsonRpcRequest
{
    public string JsonRpc { get; init; } = "2.0";
    public required string Id { get; init; }
    public required string Method { get; init; }
    public JsonNode? Params { get; init; }
}

public sealed record JsonRpcResponse
{
    public string JsonRpc { get; init; } = "2.0";
    public required string Id { get; init; }
    public JsonNode? Result { get; init; }
    public JsonRpcError? Error { get; init; }

    public static JsonRpcResponse Success(string id, object? result) =>
        new()
        {
            Id = id,
            Result = JsonContractOptions.ToJsonNode(result)
        };

    public static JsonRpcResponse Failure(string id, ErrorCode code, string message) =>
        new()
        {
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message
            }
        };

    public T ReadResult<T>()
    {
        if (Error is not null)
        {
            throw new BridgeRpcException(Error.Code, Error.Message);
        }

        return Result is null
            ? throw new BridgeRpcException(ErrorCode.InternalError, "JSON-RPC response had no result.")
            : Result.Deserialize<T>(JsonContractOptions.Default)
                ?? throw new BridgeRpcException(ErrorCode.InternalError, "JSON-RPC result could not be deserialized.");
    }
}

public sealed record JsonRpcError
{
    public required ErrorCode Code { get; init; }
    public required string Message { get; init; }
    public JsonObject? Details { get; init; }
}

public sealed class BridgeRpcException : Exception
{
    public BridgeRpcException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}
