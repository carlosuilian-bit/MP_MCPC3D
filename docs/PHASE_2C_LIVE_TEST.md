# Phase 2C Live Test

Date: 2026-09-11

Status: `PASSED_PHASE_2C_CIVIL3D_READ_TOOLS`

Scope:

- Civil 3D alignment READ tools
- Civil 3D surface READ tools
- drawing COGO point READ tools
- Host: Autodesk Civil 3D 2026
- DWG: `C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg`

The user confirmed this DWG is a test drawing with alignments, surfaces, and COGO points.

## Build And Test Evidence

Commands executed:

```powershell
dotnet build .\Civil3D.HybridMcp.sln -c Debug -v:q -clp:ErrorsOnly
dotnet test .\Civil3D.HybridMcp.sln -c Debug --no-build -v:minimal
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-bundle.ps1 -Configuration Release
```

Results:

| Check | Result |
| --- | --- |
| Debug build | Passed |
| Debug tests | Passed, 60 total |
| Bundle install | Passed |
| MSB3277 warnings | 9 known Autodesk reference warnings |
| New warning category | None observed |
| Build errors | 0 |

The 9 MSB3277 warnings are the known `Microsoft.VisualBasic`, `System.Drawing`, and `WindowsBase` conflict categories, reported for the AutoCAD tools project, Civil 3D tools project, and bridge project.

## Bundle Evidence

Installed bundle:

```text
%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle
```

The installed `Contents\Win64` directory included:

- `Civil3D.HybridMcp.Bridge.dll`
- `Civil3D.HybridMcp.Core.dll`
- `Civil3D.HybridMcp.Diagnostics.dll`
- `Civil3D.HybridMcp.Protocol.dll`
- `Civil3D.HybridMcp.Sdk.dll`
- `Civil3D.HybridMcp.Tools.AutoCAD.dll`
- `Civil3D.HybridMcp.Tools.Civil.dll`

The Autodesk runtime requirement remained:

```xml
<RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="R25.0" SeriesMax="R25.1" />
```

## Host Endpoint

Civil 3D was opened with PID `49140`.

Endpoint:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\endpoints\civil3d-hybrid-mcp-49140-be0148e7f35248fa8e78f1eae758f452.json
```

| Field | Value |
| --- | --- |
| Instance ID | `be0148e7f35248fa8e78f1eae758f452` |
| Document ID | `109e5d9e9b581675c82df38e9527a805d03322f194c04adfe0b2793abe05a3c6` |
| Product | Autodesk Civil 3D |
| Product version | `25.1.0.0` |
| AutoCAD API version | `25.1s (LMS Tech)` |
| Civil 3D API version | `25.1.0.0` |
| DWG | `TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg` |
| Database fingerprint | `{52311825-E449-6945-9657-BF9E8555A12E}` |

## Autoload Note

One startup opened Civil 3D to `[Start]` without registering an endpoint. For this validation, the bridge was loaded manually with `NETLOAD` from the installed bundle:

```text
%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle\Contents\Win64\Civil3D.HybridMcp.Bridge.dll
```

After `NETLOAD`, endpoint registration, named-pipe calls, and the live Civil 3D READ tools passed. This is tracked as a startup/autoload diagnostic issue, not a failure of the Phase 2C tool contracts.

## Manifest Result

| Check | Result |
| --- | --- |
| Tool count | 16 |
| Civil 3D READ tool count | 9 |
| `alignment_list` present | Yes |
| `alignment_get` present | Yes |
| `alignment_station_offset` present | Yes |
| `alignment_point_location` present | Yes |
| `surface_list` present | Yes |
| `surface_get` present | Yes |
| `surface_elevation_at_xy` present | Yes |
| `cogo_list` present | Yes |
| `cogo_get` present | Yes |

## Live Read Results

| Tool | Result |
| --- | --- |
| `alignment_list` | Passed, 32 alignments |
| `alignment_get` | Passed for `BD - 100` |
| `alignment_station_offset` | Passed |
| `alignment_point_location` | Passed and returned the original COGO XY |
| `surface_list` | Passed, 4 surfaces |
| `surface_get` | Passed for `PAV - DATUM` |
| `surface_elevation_at_xy` | Passed on surface `PAV - DATUM` |
| `cogo_list` | Passed, 100 sampled points |
| `cogo_get` | Passed for point number `1` |

Representative values:

| Field | Value |
| --- | --- |
| First alignment | `BD - 100` |
| Alignment handle | `1124B6` |
| Alignment station range | `2000` to `2357.8261343206955` |
| Alignment length | `357.8261343206952` |
| First surface | `PAV - DATUM` |
| Surface handle | `165471` |
| Surface type | `TinSurface` |
| Surface point count | `34640` |
| Surface min/max elevation | `267.4899999999844` / `292.011835760961` |
| First COGO point | `1` |
| First COGO handle | `E7EF6` |
| First COGO raw description | `CT` |
| First COGO XY/elevation | `684889.7197474907`, `7552468.6285168305`, `279.9100000000001` |
| Station/offset against `BD - 100` | `2181.022811424956`, `11.159645624911562` |
| Surface elevation sample | point `6`, `PAV - DATUM`, elevation `267.6852606193413` |

The first attempted surface elevation at COGO point `1` returned structured `CivilQueryFailed` with message `Point Outside Surface`, which is expected API behavior for an XY outside the selected surface. The validation then found a successful sample on COGO point `6`.

## Official MCP Live Test

Command:

```powershell
$env:CIVIL3D_HYBRID_LIVE_INSTANCE_ID='be0148e7f35248fa8e78f1eae758f452'
dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~OfficialMcpClientCanCallLiveBridgeWhenConfigured" -v:minimal
```

Result: passed, 1 test.

## Safety Notes

- No write operation was run during Phase 2C validation.
- No destructive operation was run.
- No save command was issued by Codex.
- The Civil 3D host remained open after validation so the user can inspect the drawing.

## Gate Result

Phase 2C passed. The project now has live validated READ coverage for:

- AutoCAD layers
- Civil 3D alignments
- Civil 3D surfaces
- drawing COGO points
