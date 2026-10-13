# Phase 2 Live Test

Date: 2026-09-11

Status: `PASSED_PHASE_2_LIVE_LAYER_READ_WRITE`

Scope:

- Phase 2A: AutoCAD layer READ tools
- Phase 2B: first safe WRITE tool
- Host: Autodesk Civil 3D 2026
- DWG: `C:\Users\CGomes\Desktop\24-149 - EXE_R3.dwg`

The user confirmed this DWG is a test drawing before write validation.

## Build And Test Evidence

Commands executed:

```powershell
dotnet build .\Civil3D.HybridMcp.sln -c Debug -v:minimal
dotnet test .\Civil3D.HybridMcp.sln -c Debug --no-build -v:minimal
dotnet build .\Civil3D.HybridMcp.sln -c Release -v:minimal
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-bundle.ps1 -Configuration Release
```

Results:

| Check | Result |
| --- | --- |
| Debug build | Passed |
| Debug tests | Passed, 51 total |
| Release build | Passed |
| Bundle install | Passed |
| MSB3277 warnings | 6 known Autodesk reference warnings |
| New warning category | None observed |
| Build errors | 0 |

The 6 MSB3277 warnings are the known `Microsoft.VisualBasic`, `System.Drawing`, and `WindowsBase` conflict categories, reported for both the AutoCAD tools project and the bridge project.

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

The Autodesk runtime requirement remained:

```xml
<RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="R25.0" SeriesMax="R25.1" />
```

## Phase 2A Live Read Gate

Initial live read validation used Civil 3D PID `13544`:

| Field | Value |
| --- | --- |
| Instance ID | `832ae8d697694875816c10e6510136f1` |
| Document ID | `1a525dee9bf59c91f32093e00ab9dc71e32019fd3d8876764c5f3e2590efc019` |
| Product | Autodesk Civil 3D |
| Product version | `25.1.0.0` |
| DWG | `24-149 - EXE_R3.dwg` |
| Units | Meters |
| Database fingerprint | `{4000CA8B-3946-8943-BD3B-DAB117CA464A}` |

Results:

| Tool | Result |
| --- | --- |
| `tools/list` | 6 tools before Phase 2B reload |
| `layer_list` | Passed, 245 layers returned |
| `layer_get` name `0` | Passed, handle `9`, type `LayerTableRecord` |
| Official MCP live test | Passed |

This completed the Phase 2A gate before moving to `layer_create`.

## Phase 2B Live Write Gate

After installing the Phase 2B bundle, Civil 3D was reopened with PID `44200`.

Endpoint:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\endpoints\civil3d-hybrid-mcp-44200-a20e9289cc3146cf9e09d5b2370c3c2f.json
```

| Field | Value |
| --- | --- |
| Instance ID | `a20e9289cc3146cf9e09d5b2370c3c2f` |
| Document ID | `76b72a9f74eac5fe7a4d434e8e121681f08adb27249750c4136bf588a1216747` |
| Product | Autodesk Civil 3D |
| Product version | `25.1.0.0` |
| AutoCAD API version | `25.1s (LMS Tech)` |
| Civil 3D API version | `25.1.0.0` |
| DWG | `24-149 - EXE_R3.dwg` |
| Database fingerprint | `{4000CA8B-3946-8943-BD3B-DAB117CA464A}` |

Manifest result:

| Check | Result |
| --- | --- |
| Tool count | 7 |
| `layer_create` present | Yes |
| `layer_create` class | WRITE |
| `layer_create` risk | Low |
| `layer_create` rollback | Transaction rollback before commit |
| `layer_create` confirmation | Not required |

Official MCP live test:

```powershell
$env:CIVIL3D_HYBRID_LIVE_INSTANCE_ID='a20e9289cc3146cf9e09d5b2370c3c2f'
dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~OfficialMcpClientCanCallLiveBridgeWhenConfigured" -v:minimal
```

Result: passed, 1 test.

## layer_create Result

Request:

```json
{
  "toolName": "layer_create",
  "arguments": {
    "instance_id": "a20e9289cc3146cf9e09d5b2370c3c2f",
    "document_id": "76b72a9f74eac5fe7a4d434e8e121681f08adb27249750c4136bf588a1216747",
    "name": "MCP_TEST_LAYER",
    "color_index": 3,
    "is_plottable": true,
    "description": "Created by Civil3D Hybrid MCP Phase 2B live test."
  }
}
```

Result:

| Field | Value |
| --- | --- |
| Success | `true` |
| Created | `true` |
| Layer | `MCP_TEST_LAYER` |
| Handle | `55724` |
| Type | `LayerTableRecord` |
| Color index | `3` |
| Plottable | `true` |
| Document locked | `true` |
| Transaction started | `true` |
| Committed | `true` |
| Rolled back | `false` |

`layer_get` then read back `MCP_TEST_LAYER` successfully with the same handle `55724`.

## Duplicate Protection Check

A second `layer_create` call for `MCP_TEST_LAYER` returned:

| Field | Value |
| --- | --- |
| Success | `false` |
| Error | `LayerAlreadyExists` |

A filtered `layer_list` with `name_contains = MCP_TEST_LAYER` returned exactly one matching layer.

## Safety Notes

- No destructive operation was run.
- No layer delete/update/set-current tool exists in this phase.
- No save command was issued by Codex.
- The test layer was not removed because deletion is outside Phase 2 scope and would require its own destructive policy.
- The Civil 3D host remained open after validation so the user can inspect the test drawing state.

## Gate Result

Phase 2A and Phase 2B passed. The project now has:

- a live validated AutoCAD layer READ surface
- a live validated first safe WRITE surface
- a reusable AutoCAD write pipeline ready to be generalized carefully for the next write tools
