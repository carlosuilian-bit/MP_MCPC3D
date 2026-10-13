using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Server;

var jsonOptions = new JsonSerializerOptions(JsonContractOptions.Default) { WriteIndented = true };
var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var instanceId = ReadOption(args, "--instance");
var endpointRegistry = ReadOption(args, "--endpoint-registry")
    ?? Environment.GetEnvironmentVariable("CIVIL3D_HYBRID_ENDPOINT_DIR");
var logDirectory = ReadOption(args, "--log-dir")
    ?? Environment.GetEnvironmentVariable("CIVIL3D_HYBRID_LOG_DIR");
instanceId ??= Environment.GetEnvironmentVariable("CIVIL3D_HYBRID_INSTANCE_ID");
var criteria = string.IsNullOrWhiteSpace(instanceId)
    ? null
    : new EndpointSelectorCriteria { InstanceId = instanceId };
var serverOptions = CreateServerOptions(endpointRegistry);

if (command is "mcp" or "serve-mcp")
{
    await McpStdioServer.RunAsync(new McpStdioServerOptions
    {
        Server = serverOptions,
        EndpointSelector = criteria,
        LogDirectory = logDirectory
    }, args.Skip(1).ToArray());

    return 0;
}

var server = new HybridMcpServer(serverOptions);

try
{
    object result = command switch
    {
        "discover" => server.DiscoverEndpoints(),
        "ping" => await server.PingAsync(criteria),
        "status" => await server.StatusAsync(criteria),
        "document-list" => await server.DocumentListAsync(criteria),
        "drawing-info" => await server.DrawingInfoAsync(criteria),
        _ => new
        {
            commands = new[] { "discover", "ping", "status", "document-list", "drawing-info", "mcp" },
            options = new[] { "--instance <instance_id>", "--endpoint-registry <path>", "--log-dir <path>" },
            environment = new[]
            {
                "CIVIL3D_HYBRID_INSTANCE_ID",
                "CIVIL3D_HYBRID_ENDPOINT_DIR",
                "CIVIL3D_HYBRID_LOG_DIR"
            }
        }
    };

    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    return 0;
}
catch (HybridMcpServerException ex)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(ex.Error, jsonOptions));
    return 2;
}

static string? ReadOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static HybridMcpServerOptions CreateServerOptions(string? endpointRegistry)
{
    var options = HybridMcpServerOptions.Default();
    if (string.IsNullOrWhiteSpace(endpointRegistry))
    {
        return options;
    }

    return options with
    {
        EndpointRegistry = options.EndpointRegistry with
        {
            DirectoryPath = endpointRegistry
        }
    };
}
