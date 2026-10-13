# Civil 3D / MicroSurvey CAD — MCP Bridge Architecture

## Overview

This project provides a **Model Context Protocol (MCP) server** that lets Claude control Autodesk Civil 3D and MicroSurvey CAD programmatically. It uses a two-process architecture because CAD APIs can only be called from inside the CAD application's process.

```
┌──────────┐  stdio    ┌───────────────┐   HTTP     ┌──────────────────┐
│          │  (MCP     │               │  localhost  │                  │
│  Claude  │◄─────────►│  MCP Server   │◄───────────►│  .NET Plugin     │
│          │  JSON-RPC)│  (Python)     │   :8270    │  (inside CAD)    │
│          │           │               │            │                  │
└──────────┘           └───────────────┘            └──────────────────┘
     │                        │                            │
     │ Speaks MCP protocol    │ Translates MCP tool        │ Calls Civil 3D
     │ via stdio              │ calls to HTTP requests     │ .NET API directly
     │                        │                            │
     │ This is what Claude    │ server.py                  │ Civil3dMcpBridge.cs
     │ Desktop connects to    │ ~400 lines                 │ ~600 lines
     └────────────────────────┴────────────────────────────┘
```

## Why Two Processes?

**You can't avoid this.** Civil 3D's .NET API (`Autodesk.Civil.DatabaseServices`) can only be called from a plugin running inside Civil 3D's process. But Claude's MCP protocol requires a server process that it spawns and communicates with over stdio. These are fundamentally different processes.

The solution: a lightweight HTTP bridge between them.

- **Plugin** (.NET, loaded via `NETLOAD`): Runs inside Civil 3D, listens on `localhost:8270`, executes API calls on the document thread
- **MCP Server** (Python, via FastMCP): Runs as a standalone process, speaks MCP on stdio to Claude, proxies requests to the plugin via HTTP

## File Structure

```
civil3d-mcp/
├── plugin/
│   ├── Civil3dMcpBridge.cs        # The .NET plugin (C#)
│   ├── Civil3dMcpBridge.csproj    # Build project (create this)
│   └── README.md
├── mcp-server/
│   ├── server.py                  # MCP server (Python/FastMCP)
│   └── requirements.txt
└── docs/
    ├── ARCHITECTURE.md            # This file
    └── MICROSURVEY-ADAPTATION.md  # How to port to MicroSurvey CAD
```

## Setup Steps

### 1. Build the .NET Plugin

```bash
# Prerequisites
# - Visual Studio 2022 or dotnet CLI
# - .NET Framework 4.8 targeting pack
# - Civil 3D 2025/2026 installed (for reference DLLs)

# Create project
dotnet new classlib -n Civil3dMcpBridge -f net48

# Add references (paths vary by Civil 3D version)
# In .csproj, add:
#   <Reference Include="AcMgd">
#     <HintPath>C:\Program Files\Autodesk\AutoCAD 2026\AcMgd.dll</HintPath>
#     <Private>false</Private>
#   </Reference>
#   <Reference Include="AcDbMgd">...same pattern...</Reference>
#   <Reference Include="AcCoreMgd">...same pattern...</Reference>
#   <Reference Include="AeccDbMgd">...same pattern...</Reference>

# NuGet dependency
dotnet add package Newtonsoft.Json

# Build
dotnet build -c Release
```

### 2. Load the Plugin in Civil 3D

```
Command: NETLOAD
Browse to: Civil3dMcpBridge.dll
Command: MCPSTART
=> "MCP Bridge started on http://localhost:8270/"
```

### 3. Install the MCP Server

```bash
pip install fastmcp httpx
```

### 4. Configure Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "civil3d": {
            "command": "python",
            "args": ["C:/path/to/civil3d-mcp/mcp-server/server.py"],
            "env": {
                "CAD_BRIDGE_URL": "http://localhost:8270",
                "CAD_APP_NAME": "Civil3D"
            }
        }
    }
}
```

### 5. Test

In Claude: "List all COGO points in my drawing" → should return point data from the open .dwg.

## Available Tools

### Connection
| Tool | Description |
|------|-------------|
| `ping` | Check if CAD app is running with plugin loaded |
| `get_drawing_info` | Get filename, path, units of current drawing |

### Drawing Settings (Civil 3D specific)
| Tool | Description |
|------|-------------|
| `get_drawing_settings` | Read units, scale, zone, coordinate system |
| `set_drawing_settings` | Modify scale, precision, direction type, abbreviations |

### Layers
| Tool | Description |
|------|-------------|
| `list_layers` | List all layers with color/freeze/off state |
| `create_layer` | Create new layer with color |
| `set_current_layer` | Set active layer for new entities |

### Geometry
| Tool | Description |
|------|-------------|
| `draw_line` | Draw line by X,Y coordinates |
| `draw_line_by_bearing` | Draw line by start point + bearing + distance |
| `draw_line_by_azimuth` | Draw line by start point + azimuth + distance |
| `draw_arc` | Draw arc by center, radius, angles |
| `draw_polyline` | Draw polyline through point list |
| `offset_entity` | Offset a line/arc/polyline |

### COGO Points
| Tool | Description |
|------|-------------|
| `list_cogo_points` | List all points with N/E/Z and descriptions |
| `get_cogo_point` | Get specific point by number |
| `edit_cogo_point` | Change description, elevation, coordinates |
| `create_cogo_point` | Add new COGO point |

### Styles
| Tool | Description |
|------|-------------|
| `list_point_styles` | List available point display styles |
| `create_point_style` | Create new point style (marker type/size/rotation) |
| `list_point_label_styles` | List available point label styles |
| `create_point_label_style` | Create new label style with components |
| `list_line_label_styles` | List line label styles |
| `create_line_label_style` | Create new line label style |

### Fallback
| Tool | Description |
|------|-------------|
| `send_command` | Send raw command string to CAD command line |

## Thread Safety

Civil 3D requires all API calls to execute on the document's main thread. The plugin handles this via `Document.LockDocument()` and executing handlers within the lock scope. The HTTP listener runs on a background thread but dispatches work to the main thread.

## Existing Prior Art

Several AutoCAD MCP servers already exist on GitHub:

- **puran-water/autocad-mcp** — Feature-rich, File IPC + ezdxf backends
- **ahmetcemkaraca/AutoCAD_MCP** — 19 tools for AutoCAD 2025
- **AnCode666/multiCAD-mcp** — Multi-CAD support (AutoCAD, GstarCAD, ZWCAD)

Our approach differs because we target **Civil 3D's extended API** (not just base AutoCAD) for COGO points, styles, label styles, and drawing settings.
