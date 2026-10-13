using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Civil3D.HybridMcp.Server;

public sealed record McpToolSurfaceOptions
{
    public EndpointSelectorCriteria? EndpointSelector { get; init; }
}

public sealed class McpToolSurface
{
    private readonly HybridMcpServer _server;
    private readonly McpToolSurfaceOptions _options;

    public McpToolSurface(HybridMcpServer server, McpToolSurfaceOptions? options = null)
    {
        _server = server;
        _options = options ?? new McpToolSurfaceOptions();
    }

    public async ValueTask<ListToolsResult> HandleListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken) =>
        await ListToolsAsync(cancellationToken);

    public async ValueTask<CallToolResult> HandleCallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken) =>
        await CallToolAsync(request.Params?.Name, request.Params?.Arguments, cancellationToken);

    public async Task<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manifest = await _server.GetToolManifestAsync(_options.EndpointSelector, cancellationToken);
            return new ListToolsResult
            {
                Tools = manifest.Tools.Select(MapTool).ToArray()
            };
        }
        catch (HybridMcpServerException ex) when (
            ex.Code is ErrorCode.BridgeUnavailable
                or ErrorCode.InstanceNotFound
                or ErrorCode.InstanceAmbiguous
                or ErrorCode.ProtocolIncompatible)
        {
            return new ListToolsResult();
        }
    }

    public async Task<CallToolResult> CallToolAsync(
        string? toolName,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken cancellationToken = default)
    {
        var correlationId = ReadCorrelationId(arguments) ?? Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return ToCallToolResult(ErrorResponse(
                toolName ?? "",
                correlationId,
                ErrorCode.InvalidRequest,
                "MCP tool request did not include a tool name."));
        }

        try
        {
            var response = await _server.ExecuteToolAsync(
                toolName,
                ToJsonNode(arguments),
                _options.EndpointSelector,
                correlationId,
                cancellationToken);

            return ToCallToolResult(response);
        }
        catch (HybridMcpServerException ex)
        {
            return ToCallToolResult(ErrorResponse(toolName, correlationId, ex.Error));
        }
    }

    public static McpTool MapTool(ToolDescriptor descriptor) =>
        new()
        {
            Name = descriptor.Name,
            Title = descriptor.Name,
            Description = descriptor.Description,
            InputSchema = ToJsonElement(descriptor.InputSchema),
            OutputSchema = ToJsonElement(descriptor.OutputSchema),
            Annotations = new ToolAnnotations
            {
                Title = descriptor.Name,
                ReadOnlyHint = descriptor.OperationClass == OperationClass.Read,
                DestructiveHint = descriptor.OperationClass == OperationClass.Destructive,
                IdempotentHint = descriptor.OperationClass == OperationClass.Read,
                OpenWorldHint = false
            },
            Meta = new JsonObject
            {
                ["civil3d_hybrid_mcp.module"] = descriptor.Module,
                ["civil3d_hybrid_mcp.operation_class"] = descriptor.OperationClass.ToString(),
                ["civil3d_hybrid_mcp.risk"] = descriptor.Risk.ToString(),
                ["civil3d_hybrid_mcp.requires_confirmation"] = descriptor.RequiresConfirmation,
                ["civil3d_hybrid_mcp.rollback"] = descriptor.RollbackSemantics.ToString(),
                ["civil3d_hybrid_mcp.tool_contract_version"] = descriptor.Version.ToString()
            }
        };

    public static CallToolResult ToCallToolResult(ToolResponse response)
    {
        var structured = ToStructuredContent(response);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = JsonSerializer.Serialize(structured, JsonContractOptions.Default)
                }
            ],
            StructuredContent = ToJsonElement(structured),
            IsError = !response.Success,
            Meta = new JsonObject
            {
                ["correlation_id"] = response.CorrelationId,
                ["tool"] = response.Tool
            }
        };
    }

    private static ToolResponse ErrorResponse(
        string toolName,
        string correlationId,
        ErrorEnvelope error) =>
        new()
        {
            Success = false,
            Tool = toolName,
            CorrelationId = correlationId,
            Error = error with { CorrelationId = error.CorrelationId ?? correlationId }
        };

    private static ToolResponse ErrorResponse(
        string toolName,
        string correlationId,
        ErrorCode code,
        string message) =>
        ErrorResponse(toolName, correlationId, new ErrorEnvelope
        {
            Code = code,
            Message = message,
            CorrelationId = correlationId
        });

    private static JsonObject ToStructuredContent(ToolResponse response)
    {
        var structured = new JsonObject
        {
            ["success"] = response.Success,
            ["tool"] = response.Tool,
            ["correlationId"] = response.CorrelationId,
            ["data"] = CloneNode(response.Data),
            ["warnings"] = JsonContractOptions.ToJsonNode(response.Warnings)
        };

        if (response.Document is not null)
        {
            structured["document"] = JsonContractOptions.ToJsonNode(response.Document);
        }

        if (response.Duration is not null)
        {
            structured["durationMs"] = response.Duration.Value.TotalMilliseconds;
        }

        if (response.Error is not null)
        {
            structured["error"] = new JsonObject
            {
                ["code"] = ToExternalErrorCode(response.Error.Code),
                ["internalCode"] = response.Error.Code.ToString(),
                ["message"] = response.Error.Message,
                ["correlationId"] = response.Error.CorrelationId ?? response.CorrelationId,
                ["details"] = CloneNode(response.Error.Details)
            };
        }

        return structured;
    }

    private static JsonNode? ToJsonNode(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        var node = new JsonObject();
        foreach (var (key, value) in arguments)
        {
            node[key] = JsonNode.Parse(value.GetRawText());
        }

        return node;
    }

    private static string? ReadCorrelationId(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        foreach (var key in new[] { "correlationId", "correlation_id" })
        {
            if (arguments.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static JsonElement ToJsonElement(JsonNode node) =>
        JsonSerializer.SerializeToElement(node, JsonContractOptions.Default);

    private static JsonNode? CloneNode(JsonNode? node) =>
        node is null ? null : JsonNode.Parse(node.ToJsonString(JsonContractOptions.Default));

    private static string ToExternalErrorCode(ErrorCode code)
    {
        var text = code.ToString();
        var builder = new StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (i > 0 && char.IsUpper(ch))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(ch));
        }

        return builder.ToString();
    }
}
