# Autodesk Assembly Resolution

Date: 2026-09-11

Status: Investigated. Build succeeds with known MSB3277 warnings. Phase 2C live Civil 3D host validation passed with the bundle runtime series preserved as `R25.0` through `R25.1`.

## Scope

This document records the exact Autodesk managed assembly resolution state observed through Phase 2C.

The bridge project references Autodesk assemblies from the installed Civil 3D/AutoCAD host with `Private=false`. Autodesk DLLs are not copied into the repository or redistributed in the bundle.

## Build Evidence

Command:

```powershell
dotnet build .\Civil3D.HybridMcp.sln -c Debug -v:minimal
```

Phase 1.5 result:

```text
3 Warning(s)
0 Error(s)
```

The warnings are all `MSB3277` from `src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj`.

Phase 2 result after adding `Civil3D.HybridMcp.Tools.AutoCAD`:

```text
6 Warning(s)
0 Error(s)
```

The warning categories did not change. They now appear for both:

- `src\Civil3D.HybridMcp.Tools.AutoCAD\Civil3D.HybridMcp.Tools.AutoCAD.csproj`
- `src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj`

Phase 2C result after adding `Civil3D.HybridMcp.Tools.Civil`:

```text
9 Warning(s)
0 Error(s)
```

The same warning categories now appear for:

- `src\Civil3D.HybridMcp.Tools.AutoCAD\Civil3D.HybridMcp.Tools.AutoCAD.csproj`
- `src\Civil3D.HybridMcp.Tools.Civil\Civil3D.HybridMcp.Tools.Civil.csproj`
- `src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj`

## Conflicts

| Assembly | Primary chosen by MSBuild | Alternate requested through Autodesk references | Build result |
| --- | --- | --- | --- |
| `Microsoft.VisualBasic` | `10.0.0.0` from `.nuget\packages\microsoft.netcore.app.ref\8.0.27\ref\net8.0\Microsoft.VisualBasic.dll` | `10.1.0.0` | Warning only |
| `System.Drawing` | `4.0.0.0` from `.nuget\packages\microsoft.netcore.app.ref\8.0.27\ref\net8.0\System.Drawing.dll` | `8.0.0.0` | Warning only |
| `WindowsBase` | `4.0.0.0` from `.nuget\packages\microsoft.netcore.app.ref\8.0.27\ref\net8.0\WindowsBase.dll` | `8.0.0.0` | Warning only |

Autodesk assemblies participating in these conflicts include:

- `C:\Program Files\Autodesk\AutoCAD 2026\acmgd.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\acdbmgd.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\accoremgd.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\ACA\AecBaseMgd.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\C3D\AeccDbMgd.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\AcTcMgd.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\C3D\AeccHydroCalcsMgd.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\AcMNUParser.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\AdUiPalettes.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\AdWindows.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\AcCustomize.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\DesktopConnectorInterop.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\AcWindows.dll`
- `C:\Program Files\Autodesk\AutoCAD 2026\AdUIMgd.dll`

## Autodesk Versions Observed

| File | Assembly version | File/Product version |
| --- | --- | --- |
| `acmgd.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `acdbmgd.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `accoremgd.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `AecBaseMgd.dll` | `8.8.58.0` | Civil 3D/AutoCAD 2026 ACA managed dependency |
| `AeccDbMgd.dll` | `13.8.0.1704` | `13.8.1704.0`, product `1704_20260318_0231` |
| `AcTcMgd.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `AeccHydroCalcsMgd.dll` | `0.0.0.0` | `13.8.1704.0`, product `1704_20260318_0231` |
| `AcMNUParser.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `AdUiPalettes.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `AdWindows.dll` | `5.2.1.1` | `5.2.1.1` |
| `AcCustomize.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `DesktopConnectorInterop.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `AcWindows.dll` | `25.1.0.0` | `25.1.164.0.0` |
| `AdUIMgd.dll` | `25.1.0.0` | `25.1.164.0.0` |

## Decision

Do not copy Autodesk DLLs into the repository or package.

Do not add binding redirects or silence these warnings blindly. The Autodesk-facing projects target `net10.0-windows` since 2026-09-24, because the 2026-09-22 Civil 3D 2026 update moved the host to .NET 10 (see `docs/decisions/ADR-0009-net10-autodesk-host.md`); before that they targeted `net8.0-windows`. The host process is Civil 3D/AutoCAD 2026. The observed conflicts are compile-time reference-resolution warnings between .NET reference assemblies and Autodesk managed assemblies. The bridge still compiles, and no direct Autodesk reference version mismatch was found among the four project references:

- `acmgd`
- `acdbmgd`
- `accoremgd`
- `AecBaseMgd`
- `AeccDbMgd`

The bridge now logs an Autodesk assembly snapshot during startup, including product/API version and loaded versions for the primary Autodesk assemblies when available.

During live smoke setup, Civil 3D 2026 reported `ACADVER=25.1s`. The Autodesk bundle manifest was updated to allow `R25.1` so autoload is not limited to `R25.0`.

## Live Check Result

The Phase 1.5 host runtime binding check passed in Civil 3D 2026:

- host: Autodesk Civil 3D 2026
- `ACADVER`: `25.1s (LMS Tech)`
- bridge product version: `25.1.0.0`
- bridge version: `0.1.0`
- protocol version: `1.0.0`
- result: autoload, named pipe, endpoint registry, CLI calls, and MCP STDIO calls passed for the four READ/diagnostic tools

Phase 2 additionally validated:

- `Civil3D.HybridMcp.Tools.AutoCAD.dll` packaged in the Autodesk bundle
- bundle manifest still allowing `SeriesMin="R25.0"` and `SeriesMax="R25.1"`
- `layer_list`
- `layer_get`
- `layer_create`
- document lock and transaction commit evidence returned by `layer_create`
- duplicate layer rejection after creating `MCP_TEST_LAYER`

At the Phase 2B gate, this did not validate future destructive tools, Survey, Developer Mode, Civil 3D alignments/surfaces/COGO, or Roadway tools.

Phase 2C additionally validated:

- `Civil3D.HybridMcp.Tools.Civil.dll` packaged in the Autodesk bundle
- `AecBaseMgd.dll` reference resolution from `C:\Program Files\Autodesk\AutoCAD 2026\ACA`
- 16-tool manifest
- `alignment_list`
- `alignment_get`
- `alignment_station_offset`
- `alignment_point_location`
- `surface_list`
- `surface_get`
- `surface_elevation_at_xy`
- `cogo_list`
- `cogo_get`

The Phase 2C live run used manual `NETLOAD` after one Civil 3D startup did not autoload the bridge. The loaded bridge, named pipe, MCP client path, and Civil 3D READ tools all passed after load.

This does not validate future destructive tools, Survey, Developer Mode, Civil 3D write operations, corridors, blocks, property sets, or Roadway tools.

If Civil 3D fails to load the bridge, capture:

- the command output from `C3DHMCPSTATUS` or `C3DHYBRIDSTATUS`
- `%LOCALAPPDATA%\Civil3DHybridMcp\logs\bridge-YYYYMMDD.jsonl`
- the Autodesk demand-load or .NET load error shown by Civil 3D
- the exact installed Civil 3D build/version
