using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Core;

public interface IToolHandler
{
    ToolDescriptor Descriptor { get; }
    Task<ToolResponse> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken);
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IToolHandler> _handlers;

    public ToolRegistry(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(handler => handler.Descriptor.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDescriptor> ListTools() =>
        _handlers.Values.Select(handler => handler.Descriptor).OrderBy(descriptor => descriptor.Name).ToArray();

    public bool TryGet(string name, out IToolHandler handler) =>
        _handlers.TryGetValue(name, out handler!);
}

public static class ToolSchema
{
    public static JsonObject EmptyInput() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false
        };

    public static JsonObject ObjectOutput(string title) =>
        new()
        {
            ["type"] = "object",
            ["title"] = title
        };
}

