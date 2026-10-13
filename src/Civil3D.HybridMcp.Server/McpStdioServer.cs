using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Civil3D.HybridMcp.Server;

public sealed record McpStdioServerOptions
{
    public required HybridMcpServerOptions Server { get; init; }
    public EndpointSelectorCriteria? EndpointSelector { get; init; }
    public string? LogDirectory { get; init; }
}

public static class McpStdioServer
{
    public static async Task RunAsync(
        McpStdioServerOptions options,
        string[] hostArgs,
        CancellationToken cancellationToken = default)
    {
        var requestLogger = string.IsNullOrWhiteSpace(options.LogDirectory)
            ? new JsonLinesRequestLogger()
            : new JsonLinesRequestLogger(options.LogDirectory);
        var hybridServer = new HybridMcpServer(options.Server, logger: requestLogger);
        var toolSurface = new McpToolSurface(hybridServer, new McpToolSurfaceOptions
        {
            EndpointSelector = options.EndpointSelector
        });

        var builder = Host.CreateApplicationBuilder(hostArgs);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console =>
            console.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddMcpServer(serverOptions =>
            {
                serverOptions.ServerInfo = new Implementation
                {
                    Name = ProtocolConstants.ServerName,
                    Title = ProtocolConstants.ProductName,
                    Version = ProtocolConstants.ServerVersion,
                    Description = "STDIO MCP adapter for Civil3D Hybrid MCP bridge tools."
                };
                serverOptions.Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability
                    {
                        ListChanged = false
                    }
                };
            })
            .WithStdioServerTransport()
            .WithListToolsHandler(toolSurface.HandleListToolsAsync)
            .WithCallToolHandler(toolSurface.HandleCallToolAsync);

        await builder.Build().RunAsync(cancellationToken);
    }
}
