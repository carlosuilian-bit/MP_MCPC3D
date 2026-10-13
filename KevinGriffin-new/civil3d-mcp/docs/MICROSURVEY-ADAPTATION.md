# Adapting the Bridge for MicroSurvey CAD

## Why This Works

MicroSurvey CAD is built on the **IntelliCAD engine**, which deliberately mirrors AutoCAD's API structure. This means:

- The **.NET plugin loading** mechanism is nearly identical (`NETLOAD` command)
- **Base geometry APIs** (Line, Arc, Polyline, Layer management) use the same class names and namespaces under IntelliCAD's managed wrappers
- **DWG file format** is natively supported (same entity database)

The key difference: MicroSurvey CAD does **not** have the `Autodesk.Civil.DatabaseServices` namespace. It has its own surveying features, point management, and coordinate geometry tools.

## What Changes vs. Civil 3D

### Layer 1: Same (no changes needed)
- **MCP Server** (`server.py`) — identical, just change `CAD_APP_NAME` env var
- **HTTP bridge protocol** — identical JSON request/response format
- **Geometry tools** — `draw_line`, `draw_arc`, `draw_polyline`, `offset_entity` all work the same

### Layer 2: Namespace swaps
The C# plugin needs different `using` statements and references:

```csharp
// Civil 3D version:
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;

// MicroSurvey/IntelliCAD version:
using Teigha.Runtime;                    // or IntelliCAD equivalent
using Teigha.ApplicationServices;
using Teigha.DatabaseServices;
// No Civil 3D equivalent — use MicroSurvey's own APIs
```

IntelliCAD uses **Teigha** (now Open Design Alliance / ODA) as its DWG engine, so the managed wrappers are in `Teigha.*` namespaces rather than `Autodesk.AutoCAD.*`.

Some IntelliCAD builds also support the `Autodesk.AutoCAD.*` namespace mapping for compatibility — check your MicroSurvey CAD version's developer documentation.

### Layer 3: Surveying-specific features (biggest changes)

MicroSurvey CAD has its own point management, traverse, and COGO features that don't map 1:1 to Civil 3D's. You'll need to:

1. **Identify MicroSurvey's point API** — how points are stored, queried, and edited
2. **Map the tool interface** — keep the same MCP tool names (`list_cogo_points`, `edit_cogo_point`) but implement them against MicroSurvey's API
3. **Style system** — MicroSurvey has its own point display and labeling system; the style creation tools need new implementations

## Adaptation Strategy

### Option A: Shared Interface, Separate Plugins (Recommended)

```
                                ┌─ Civil3dMcpBridge.dll (Civil 3D)
MCP Server ──── HTTP ──────────┤
  (shared)                      └─ MicroSurveyMcpBridge.dll (MicroSurvey)
```

- One `server.py` serves both apps
- Two separate .NET plugins, each implementing the same HTTP API contract
- Switch between them by changing the port/URL in config
- The MCP tools (bearing/distance, azimuth/distance, point operations) stay the same from Claude's perspective

### Option B: Single Plugin with Conditional Compilation

```csharp
#if CIVIL3D
using Autodesk.Civil.DatabaseServices;
#elif MICROSURVEY
using MicroSurvey.CAD.DatabaseServices; // hypothetical namespace
#endif
```

Less clean but fewer files to maintain.

### Option C: Abstract Interface

```csharp
public interface ICadBridge
{
    object ListCogoPoints();
    object CreatePointStyle(JObject parameters);
    // ...etc
}

public class Civil3dBridge : ICadBridge { /* Civil 3D implementation */ }
public class MicroSurveyBridge : ICadBridge { /* MicroSurvey implementation */ }
```

Most maintainable long-term, especially if you plan to support additional CAD platforms.

## Getting Started with MicroSurvey's API

1. **Check for developer docs**: MicroSurvey's helpdesk and IntelliCAD's developer portal
   - https://helpdesk.microsurvey.com/
   - https://www.intellicad.org/cad-development-platform-framework/

2. **Plugin loading**: Use `NETLOAD` or the IntelliCAD equivalent. The plugin interface uses `IICadPlugin` with `PluginLoaded()` and `PluginUnLoading()` methods.

3. **Examine the available assemblies**: Look in MicroSurvey CAD's install directory for managed DLLs. Key ones to reference:
   - Base CAD engine (Teigha/ODA wrappers)
   - MicroSurvey-specific extensions (survey points, traverses, etc.)

4. **Start with the geometry layer**: Get `draw_line`, `list_layers`, `send_command` working first, since these use the base CAD engine. Then add the surveying-specific tools.

## Claude Desktop Config for MicroSurvey

```json
{
    "mcpServers": {
        "microsurvey": {
            "command": "python",
            "args": ["C:/path/to/civil3d-mcp/mcp-server/server.py"],
            "env": {
                "CAD_BRIDGE_URL": "http://localhost:8271",
                "CAD_APP_NAME": "MicroSurveyCAD"
            }
        }
    }
}
```

Note the different port (8271 vs 8270) so both can run simultaneously if needed.

## Running Both Simultaneously

If you have both Civil 3D and MicroSurvey CAD open at the same time, you can register two MCP servers in Claude's config — one per app, on different ports. Claude will see both tool sets and can direct commands to either application.
