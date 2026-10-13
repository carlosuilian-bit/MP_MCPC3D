# MCP Client Setup

Date: 2026-09-04

Status: Phase 1.5 implemented for STDIO using the official C# MCP SDK.

## Server Command

Build first:

```powershell
dotnet build .\Civil3D.HybridMcp.sln -c Release
```

STDIO command:

```powershell
src\Civil3D.HybridMcp.Server\bin\Release\net8.0\Civil3D.HybridMcp.Server.exe mcp
```

Development builds can use the same command under `bin\Debug\net8.0`.

## Generic MCP Configuration

Use this shape in an MCP client that accepts JSON server configuration:

```json
{
  "mcpServers": {
    "civil3d-hybrid-mcp": {
      "command": "C:\\Users\\CGomes\\Desktop\\MCPMP_Civil3D\\src\\Civil3D.HybridMcp.Server\\bin\\Release\\net8.0\\Civil3D.HybridMcp.Server.exe",
      "args": ["mcp"],
      "env": {
        "CIVIL3D_HYBRID_ENDPOINT_DIR": "%LOCALAPPDATA%\\Civil3DHybridMcp\\endpoints",
        "CIVIL3D_HYBRID_LOG_DIR": "%LOCALAPPDATA%\\Civil3DHybridMcp\\logs"
      }
    }
  }
}
```

Some MCP clients do not expand `%LOCALAPPDATA%` in JSON values. If so, replace those two paths with the expanded absolute path for the current Windows user.

If more than one Civil 3D bridge is running, select one explicitly:

```json
{
  "CIVIL3D_HYBRID_INSTANCE_ID": "<instance_id>"
}
```

The same instance can also be selected with:

```powershell
src\Civil3D.HybridMcp.Server\bin\Release\net8.0\Civil3D.HybridMcp.Server.exe mcp --instance <instance_id>
```

## Tool Discovery Behavior

The MCP adapter does not hardcode the Civil 3D tool catalog. On `tools/list`, it:

1. discovers bridge endpoint descriptors;
2. handshakes over the named pipe;
3. reads the bridge `ToolManifest`;
4. maps each bridge `ToolDescriptor` to an MCP tool;
5. preserves the input and output JSON schemas from the bridge manifest.

If no bridge is available, `tools/list` returns an empty list. Tool calls return a structured MCP error with:

```json
{
  "success": false,
  "error": {
    "code": "BRIDGE_UNAVAILABLE",
    "internalCode": "BridgeUnavailable"
  }
}
```

## Structured Results

Every MCP tool call returns `structuredContent` with:

- `success`
- `tool`
- `correlationId`
- `data`
- `document` when available
- `durationMs` when available
- `warnings`
- `error` when failed

The adapter accepts `correlation_id` or `correlationId` in tool arguments and forwards it through the named pipe to the bridge.

## Logging

STDIO logs are routed to stderr so stdout remains reserved for MCP protocol messages.

Request audit logs are written as JSONL to:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\logs
```

Override with:

```powershell
$env:CIVIL3D_HYBRID_LOG_DIR = "C:\temp\civil3d-hybrid-mcp-logs"
```

## Validation

Phase 1.5 integration tests start the official MCP client against the compiled server executable over STDIO, list the four bridge-published tools, execute each tool against a fake bridge, and verify structured bridge-unavailable errors without a bridge.

References:

- https://csharp.sdk.modelcontextprotocol.io/v1/concepts/getting-started.html
- https://github.com/modelcontextprotocol/csharp-sdk
