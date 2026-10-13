using System.Diagnostics;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Sdk;

namespace Civil3D.HybridMcp.Server;

public sealed record HybridMcpServerOptions
{
    public required EndpointRegistryOptions EndpointRegistry { get; init; }
    public ProtocolVersion RequiredProtocolVersion { get; init; } = ProtocolVersion.Current;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    public static HybridMcpServerOptions Default() =>
        new()
        {
            EndpointRegistry = EndpointRegistryOptions.Default()
        };
}

public sealed record BridgeSession
{
    public required EndpointDescriptor Endpoint { get; init; }
    public required HandshakeResponse Handshake { get; init; }
    public IReadOnlyList<ToolDescriptor> Tools { get; init; } = Array.Empty<ToolDescriptor>();
}

public sealed class HybridMcpServerException : Exception
{
    public HybridMcpServerException(ErrorEnvelope error)
        : base(error.Message)
    {
        Error = error;
    }

    public ErrorEnvelope Error { get; }
    public ErrorCode Code => Error.Code;
}

public sealed class HybridMcpServer
{
    private readonly HybridMcpServerOptions _options;
    private readonly EndpointRegistryReader _registryReader;
    private readonly NamedPipeRpcClient _rpcClient;
    private readonly IRequestLogger _logger;

    public HybridMcpServer(
        HybridMcpServerOptions options,
        NamedPipeRpcClient? rpcClient = null,
        IRequestLogger? logger = null)
    {
        _options = options;
        _registryReader = new EndpointRegistryReader(options.EndpointRegistry);
        _rpcClient = rpcClient ?? new NamedPipeRpcClient();
        _logger = logger ?? new JsonLinesRequestLogger();
    }

    public IReadOnlyList<EndpointDescriptor> DiscoverEndpoints() =>
        _registryReader.ListValidEndpoints(cleanupStale: true);

    public async Task<BridgeSession> ConnectAsync(
        EndpointSelectorCriteria? criteria = null,
        CancellationToken cancellationToken = default)
    {
        var endpoints = _registryReader.ListValidEndpoints(cleanupStale: true);
        if (endpoints.Count == 0 && string.IsNullOrWhiteSpace(criteria?.InstanceId))
        {
            throw new HybridMcpServerException(new ErrorEnvelope
            {
                Code = ErrorCode.BridgeUnavailable,
                Message = "No Civil 3D bridge endpoint is currently available."
            });
        }

        var selection = EndpointSelector.Select(endpoints, criteria);
        if (!selection.Success || selection.Endpoint is null)
        {
            throw new HybridMcpServerException(selection.Error!);
        }

        var handshakeRequest = new HandshakeRequest
        {
            ServerName = ProtocolConstants.ServerName,
            ServerVersion = ProtocolConstants.ServerVersion,
            ProtocolVersion = _options.RequiredProtocolVersion,
            SessionId = _options.SessionId
        };

        HandshakeResponse handshake;
        try
        {
            handshake = await _rpcClient.SendAsync<HandshakeResponse>(
                selection.Endpoint.PipeName,
                "bridge/handshake",
                handshakeRequest,
                _options.RequestTimeout,
                cancellationToken);
        }
        catch (BridgeRpcException ex)
        {
            throw new HybridMcpServerException(new ErrorEnvelope
            {
                Code = ex.Code,
                Message = ex.Message
            });
        }

        var bridgeProtocol = handshake.CapabilityManifest?.ProtocolVersion ?? selection.Endpoint.ProtocolVersion;
        var compatibility = VersionNegotiator.Evaluate(_options.RequiredProtocolVersion, bridgeProtocol);
        if (compatibility.Status == CompatibilityStatus.Incompatible ||
            !handshake.Success ||
            handshake.Compatibility == CompatibilityStatus.Incompatible)
        {
            throw new HybridMcpServerException(new ErrorEnvelope
            {
                Code = ErrorCode.ProtocolIncompatible,
                Message = handshake.Error?.Message ?? compatibility.Reason
            });
        }

        return new BridgeSession
        {
            Endpoint = selection.Endpoint,
            Handshake = handshake,
            Tools = handshake.Tools
        };
    }

    public async Task<ToolManifest> GetToolManifestAsync(
        EndpointSelectorCriteria? criteria = null,
        CancellationToken cancellationToken = default)
    {
        var session = await ConnectAsync(criteria, cancellationToken);
        return new ToolManifest
        {
            Version = ProtocolVersion.Current,
            Tools = session.Tools
        };
    }

    public Task<ToolResponse> PingAsync(EndpointSelectorCriteria? criteria = null, CancellationToken cancellationToken = default) =>
        ExecuteToolAsync("civil3d_ping", null, criteria, cancellationToken: cancellationToken);

    public Task<ToolResponse> StatusAsync(EndpointSelectorCriteria? criteria = null, CancellationToken cancellationToken = default) =>
        ExecuteToolAsync("civil3d_status", null, criteria, cancellationToken: cancellationToken);

    public Task<ToolResponse> DocumentListAsync(EndpointSelectorCriteria? criteria = null, CancellationToken cancellationToken = default) =>
        ExecuteToolAsync("document_list", null, criteria, cancellationToken: cancellationToken);

    public Task<ToolResponse> DrawingInfoAsync(EndpointSelectorCriteria? criteria = null, CancellationToken cancellationToken = default) =>
        ExecuteToolAsync("drawing_info", null, criteria, cancellationToken: cancellationToken);

    public async Task<ToolResponse> ExecuteToolAsync(
        string toolName,
        JsonNode? arguments = null,
        EndpointSelectorCriteria? criteria = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        correlationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId;
        string? instanceId = null;

        try
        {
            var session = await ConnectAsync(criteria, cancellationToken);
            instanceId = session.Endpoint.InstanceId;

            var descriptor = session.Tools.FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null)
            {
                throw new HybridMcpServerException(new ErrorEnvelope
                {
                    Code = ErrorCode.ToolNotFound,
                    Message = $"Tool '{toolName}' was not advertised by the bridge.",
                    CorrelationId = correlationId
                });
            }

            if (!VersionNegotiator.IsToolContractCompatible(_options.RequiredProtocolVersion, descriptor))
            {
                throw new HybridMcpServerException(new ErrorEnvelope
                {
                    Code = ErrorCode.ToolIncompatible,
                    Message = $"Tool '{toolName}' contract version {descriptor.Version} is not compatible with server protocol {_options.RequiredProtocolVersion}.",
                    CorrelationId = correlationId
                });
            }

            var request = new ToolRequest
            {
                CorrelationId = correlationId,
                SessionId = _options.SessionId,
                InstanceId = session.Endpoint.InstanceId,
                DocumentId = ReadStringArgument(arguments, "document_id", "documentId")
                    ?? session.Endpoint.ActiveDocument?.DocumentId,
                ToolName = descriptor.Name,
                Arguments = arguments
            };

            var response = await _rpcClient.SendAsync<ToolResponse>(
                session.Endpoint.PipeName,
                "tools/execute",
                request,
                descriptor.Timeout == default ? _options.RequestTimeout : descriptor.Timeout,
                cancellationToken);

            stopwatch.Stop();
            await _logger.LogAsync(new RequestLogEntry
            {
                CorrelationId = response.CorrelationId,
                SessionId = _options.SessionId,
                InstanceId = session.Endpoint.InstanceId,
                DocumentId = response.Document?.DocumentId,
                Tool = toolName,
                Duration = stopwatch.Elapsed,
                Status = response.Success ? "success" : "error",
                ErrorCode = response.Error?.Code,
                ErrorMessage = response.Error?.Message
            }, cancellationToken);

            return response;
        }
        catch (BridgeRpcException ex)
        {
            stopwatch.Stop();
            await LogFailure(toolName, correlationId, instanceId, stopwatch.Elapsed, ex.Code, ex.Message, cancellationToken);
            throw new HybridMcpServerException(new ErrorEnvelope
            {
                Code = ex.Code,
                Message = ex.Message,
                CorrelationId = correlationId
            });
        }
        catch (HybridMcpServerException ex)
        {
            stopwatch.Stop();
            await LogFailure(toolName, correlationId, instanceId, stopwatch.Elapsed, ex.Code, ex.Message, cancellationToken);
            throw;
        }
    }

    private Task LogFailure(
        string tool,
        string correlationId,
        string? instanceId,
        TimeSpan duration,
        ErrorCode code,
        string message,
        CancellationToken cancellationToken) =>
        _logger.LogAsync(new RequestLogEntry
        {
            CorrelationId = correlationId,
            SessionId = _options.SessionId,
            InstanceId = instanceId,
            Tool = tool,
            Duration = duration,
            Status = "error",
            ErrorCode = code,
            ErrorMessage = message
        }, cancellationToken);

    private static string? ReadStringArgument(JsonNode? arguments, params string[] names)
    {
        if (arguments is not JsonObject obj)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var value))
            {
                var text = value?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }
}
