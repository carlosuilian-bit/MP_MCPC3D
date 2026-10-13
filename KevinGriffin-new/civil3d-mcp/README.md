# Civil 3D MCP

An [MCP](https://modelcontextprotocol.io) server that drives **Autodesk Civil 3D**
from an AI agent (e.g. Claude) through a small in-process .NET bridge plugin.

## How it works

Three pieces:

- **`mcp-server/`** — a Python MCP server (`server.py`) that exposes Civil 3D
  operations as MCP tools and talks to the bridge over a local socket.
- **`plugin/`** — a .NET bridge (`Civil3dMcpBridge.cs`) that loads into a
  running Civil 3D via `NETLOAD` and executes work inside the app: COGO points,
  geometry by bearing/azimuth, layers, point/label styles, surfaces & contours,
  parcels, survey databases, and FBK import. Supports headless hot-reload
  (`build-next.ps1` / `hotreload.ps1`) so an agent can iterate without UI clicks.
- **`installer/`** — Inno Setup installer + a script to register the server with
  an MCP client.

Domain data round-trips through DWG/DXF — the agent drives real Civil 3D
commands rather than a separate side database.

## Requirements

- Windows with **Autodesk Civil 3D** installed
- The AutoCAD / Civil 3D **managed .NET SDK** (ObjectARX assemblies) to build the plugin
- Python 3.x for the server: `pip install -r mcp-server/requirements.txt`

## Build & run

See **[BUILD.md](BUILD.md)** for the build → hot-reload → test loop and
**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** for the design. A MicroSurvey CAD
adaptation is sketched in **[docs/MICROSURVEY-ADAPTATION.md](docs/MICROSURVEY-ADAPTATION.md)**.

Quick start:

1. Build the plugin: `powershell -ExecutionPolicy Bypass -File plugin/build-next.ps1`
2. `NETLOAD` the resulting DLL into a running Civil 3D (see BUILD.md).
3. Run the server: `python mcp-server/server.py`, and point your MCP client at it.

## Status & attribution

Built and used against Autodesk Civil 3D on Windows; provided as-is. This is an
independent, personal project — **not affiliated with or endorsed by Autodesk**.
"AutoCAD", "Civil 3D", and "Autodesk" are trademarks of Autodesk, Inc.

## License

MIT — see [LICENSE](LICENSE).
