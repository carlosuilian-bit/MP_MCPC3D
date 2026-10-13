# Installation

Date: 2026-09-04

Status: Phase 1.5 scaffold with MCP STDIO adapter.

## Requirements

- Windows
- .NET 8 runtime, for the MCP Server
- .NET SDK capable of building `net10.0` (the SDK builds `net8.0` too)
- Autodesk Civil 3D 2026 **with the 2026-09-22 update or later**, for live bridge loading: the host now runs .NET 10 and the bridge targets `net10.0-windows`. Civil 3D 2025 is not supported. See `docs/decisions/ADR-0009-net10-autodesk-host.md`
- AutoCAD/Civil 3D managed assemblies available locally

## Autodesk Assembly Detection

The bridge project does not copy Autodesk DLLs.

Preferred overrides:

```powershell
$env:CIVIL3D_ACAD_DIR = "C:\Program Files\Autodesk\AutoCAD 2026"
$env:CIVIL3D_C3D_DIR = "C:\Program Files\Autodesk\AutoCAD 2026\C3D"
```

Equivalent MSBuild properties:

```powershell
dotnet build .\Civil3D.HybridMcp.sln -p:AutodeskAcadDir="C:\Program Files\Autodesk\AutoCAD 2026" -p:AutodeskCivil3DDir="C:\Program Files\Autodesk\AutoCAD 2026\C3D"
```

If references are missing, the bridge build fails with a clear error.

Known MSB3277 warnings for `Microsoft.VisualBasic`, `System.Drawing`, and `WindowsBase` are documented in `docs/AUTODESK_ASSEMBLY_RESOLUTION.md`.

## Build

```powershell
.\scripts\build.ps1
```

## Test

```powershell
.\scripts\test.ps1
```

## Package Bundle

```powershell
.\scripts\package.ps1 -Configuration Release
```

Output:

```text
artifacts\Civil3D.HybridMcp.bundle
```

The packaged bundle includes project DLLs plus bridge `.deps.json` and `.runtimeconfig.json`. Autodesk DLLs are intentionally not copied.

The bundle `RuntimeRequirements` currently allow AutoCAD/Civil 3D series `R25.0` through `R25.1`; the live Civil 3D 2026 host observed during smoke testing reported `ACADVER=25.1s`.

## Install Bundle

Preview:

```powershell
.\scripts\install-bundle.ps1 -Configuration Release -WhatIf
```

Install:

```powershell
.\scripts\install-bundle.ps1 -Configuration Release
```

Target:

```text
%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle
```

## Manual NETLOAD Debug

Build the bridge, then load:

```text
src\Civil3D.HybridMcp.Bridge\bin\Debug\net10.0-windows\Civil3D.HybridMcp.Bridge.dll
```

Available diagnostic command:

```text
C3DHMCPSTATUS
C3DHYBRIDSTATUS
```

Stop command:

```text
C3DHMCPSTOP
```

## Manual Read-Only Smoke Test

This was executed by Codex against Civil 3D 2026 with a blank drawing on 2026-09-04. See `docs/LIVE_SMOKE_TEST.md` for recorded results.

Manual steps:

1. Build Release.
2. Install bundle.
3. Start Civil 3D 2026.
4. Open a non-production DWG or a blank drawing.
5. Run `C3DHMCPSTATUS`.
6. In PowerShell, run `.\scripts\run-server.ps1 discover`.
7. Copy the reported `instance_id`.
8. Run:

```powershell
.\scripts\run-server.ps1 ping --instance <instance_id>
.\scripts\run-server.ps1 status --instance <instance_id>
.\scripts\run-server.ps1 document-list --instance <instance_id>
.\scripts\run-server.ps1 drawing-info --instance <instance_id>
```

Expected:

- endpoint descriptor exists
- pipe responds
- protocol is compatible
- capabilities are returned
- active document identity is returned
- no DWG modification occurs

Detailed live validation steps are maintained in `docs/LIVE_SMOKE_TEST.md`.

## MCP Client Setup

The external MCP entrypoint is:

```powershell
src\Civil3D.HybridMcp.Server\bin\Release\net8.0\Civil3D.HybridMcp.Server.exe mcp
```

See `docs/MCP_CLIENT_SETUP.md` for JSON client configuration, environment variables, multi-instance selection, and expected structured errors.

## Uninstall

Preview:

```powershell
.\scripts\uninstall-bundle.ps1 -WhatIf
```

Uninstall:

```powershell
.\scripts\uninstall-bundle.ps1
```
