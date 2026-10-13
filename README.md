# Civil3D Hybrid MCP

Civil3D Hybrid MCP is a new, modular MCP platform for Autodesk Civil 3D. It is not a fork or direct merge of the reference repositories.

Current status: Phase 3C has been implemented, adding `propertyset_numeric_test_create` as a bounded WRITE/Low AEC property-set probe. It creates a prefixed test property-set definition, attaches it to a temporary `DBPoint`, writes one `Real` value, and reads it back through the existing property-set response shape under `Document.LockDocument()` plus an AutoCAD transaction. The installed bundle was live validated against Civil 3D 2026 with a 37-tool catalog. Phase 2F added AEC property-set reads; `propertyset_definition_list`, `propertyset_list`, and `propertyset_get` expose the property data that the future roadway module depends on. Phase 1C added the modal dialog monitor. `list_dialogs` answers without using the Autodesk host context, so it still reports state while Civil 3D is blocked by a modal dialog, which previously made every drawing tool hang with no diagnostic. Phase 2E added `corridor_list` and `corridor_get`, validated against a 19-baseline corridor. Phase 2D added Civil 3D profile reads; `profile_list`, `profile_get`, and `profile_elevation_at_station` read profiles through their parent alignments, including station range, elevation range, PVI count, and grade at a station. Phase 4E-C added the first Survey network read; `survey_network_list` lists Survey networks for one or all Survey projects through the typed Survey COM adapter when an active Civil 3D document exists, and returns structured no-active-document behavior otherwise. It does not open Survey Databases, create projects, import FBK, or mutate drawing state. Phase 4E-A Survey COM read-stability, Phase 4D Survey COM working-folder read, Phase 4C Survey Database readiness/status, Phase 4B FBK dry-run, Phase 4A Survey geometry, Phase 3B `layer_update`, Phase 3A safe AutoCAD write validation, and Phase 2C Civil 3D read validation passed against Civil 3D 2026.

Roadmap progress estimates are tracked in `docs/ROADMAP_PROGRESS.md`.

Implemented in this phase:

- C#/.NET solution and project structure
- shared protocol contracts without Autodesk references
- Windows named-pipe JSON-RPC foundation
- endpoint registry under `%LOCALAPPDATA%\Civil3DHybridMcp\endpoints`
- server discovery and bridge handshake
- capability and version model
- document identity contracts
- confirmation token/hash contracts
- fake bridge integration tests
- official MCP C# SDK STDIO adapter
- MCP tool mapping from bridge `ToolManifest`
- structured MCP tool results and errors
- correlation ID propagation through MCP, named pipe, and JSONL audit logs
- initial bridge scaffold for Civil 3D
- Autodesk bundle scaffold
- four core read/diagnostic tools:
  - `civil3d_ping`
  - `civil3d_status`
  - `document_list`
  - `drawing_info`
- AutoCAD layer tools:
  - `layer_list`
  - `layer_get`
  - `layer_create`
  - `layer_update`
  - `layer_set_current`
- Civil 3D read tools:
  - `alignment_list`
  - `alignment_get`
  - `alignment_station_offset`
  - `alignment_point_location`
  - `surface_list`
  - `surface_get`
  - `surface_elevation_at_xy`
  - `cogo_list`
  - `cogo_get`
  - `profile_list`
  - `profile_get`
  - `profile_elevation_at_station`
  - `corridor_list`
  - `corridor_get`
  - `propertyset_definition_list`
  - `propertyset_list`
  - `propertyset_get`
- Civil 3D bounded write/test tools:
  - `propertyset_numeric_test_create`
- AutoCAD generic object read tools:
  - `object_get`
  - `object_properties`
- Diagnostics tools:
  - `list_dialogs`
- Survey geometry tools:
  - `survey_bearing_to_azimuth`
  - `survey_azimuth_to_bearing`
  - `survey_distance`
  - `survey_point_by_azimuth`
- Survey FBK dry-run tools:
  - `survey_fbk_dry_run`
- Survey Database readiness/status tools:
  - `survey_database_status`
- Survey COM read-only tools:
  - `survey_working_folder_get`
  - `survey_project_list`
  - `survey_network_list`
- first reusable low-risk write pipeline:
  - document target validation
  - AutoCAD `Document.LockDocument()`
  - AutoCAD `Transaction`
  - commit on success
  - rollback before commit on error

Not implemented yet:

- Survey Database/COM workflows beyond working-folder and project-list reads
- FBK import
- Developer Mode/Roslyn
- layer delete
- COGO writes
- alignment writes
- surface writes
- corridor writes/rebuild
- blocks
- general property-set writes such as `propertyset_set`
- object select and object delete
- roadway tools

## Build

```powershell
.\scripts\build.ps1
```

## Test

```powershell
.\scripts\test.ps1
```

## Run Server CLI

```powershell
.\scripts\run-server.ps1 discover
.\scripts\run-server.ps1 ping --instance <instance_id>
.\scripts\run-server.ps1 status --instance <instance_id>
.\scripts\run-server.ps1 document-list --instance <instance_id>
.\scripts\run-server.ps1 drawing-info --instance <instance_id>
```

## Run MCP STDIO Server

```powershell
src\Civil3D.HybridMcp.Server\bin\Release\net8.0\Civil3D.HybridMcp.Server.exe mcp
```

See `docs/MCP_CLIENT_SETUP.md` for client configuration.

The current MCP catalog contains 39 tools: four core read/diagnostic tools, one dialog monitor tool, five AutoCAD layer tools, two AutoCAD generic object read tools, eighteen Civil 3D/AEC tools, four Survey geometry tools, one Survey FBK dry-run tool, one Survey Database readiness/status tool, and three Survey COM read-only tools.

## Package Bundle

```powershell
.\scripts\package.ps1 -Configuration Release
```

Install manually or with:

```powershell
.\scripts\install-bundle.ps1 -Configuration Release
```

Validate the installed bundle from the filesystem without touching Civil 3D:

```powershell
.\scripts\check-bundle-install.ps1
.\scripts\check-bundle-install.ps1 -AsJson
```

Uninstall:

```powershell
.\scripts\uninstall-bundle.ps1
```

The install script supports PowerShell `-WhatIf`.

## Autodesk References

Autodesk DLLs are not copied into this repository. The bridge build detects:

- `CIVIL3D_ACAD_DIR`
- `CIVIL3D_C3D_DIR`
- `AutodeskAcadDir`
- `AutodeskCivil3DDir`

Default target path:

```text
C:\Program Files\Autodesk\AutoCAD 2026
```

The build fails clearly if required Autodesk assemblies are unavailable.

Known MSB3277 assembly-resolution warnings are documented in `docs/AUTODESK_ASSEMBLY_RESOLUTION.md`.

Live host validation results are tracked in `docs/LIVE_SMOKE_TEST.md` and `docs/PHASE_2_LIVE_TEST.md`.
Phase 2C Civil 3D read validation is tracked in `docs/PHASE_2C_LIVE_TEST.md`.
Phase 3A safe-write validation is tracked in `docs/PHASE_3A_LIVE_TEST.md`.
Phase 3B `layer_update` validation is tracked in `docs/PHASE_3B_LIVE_TEST.md`.
Phase 3C property-set numeric write/readback validation is tracked in `docs/PHASE_3C_LIVE_TEST.md`.
Phase 4A Survey geometry validation is tracked in `docs/PHASE_4A_LIVE_TEST.md`.
Phase 4C Survey Database readiness/status validation is tracked in `docs/PHASE_4C_LIVE_TEST.md`.
Phase 4D Survey COM working-folder validation is tracked in `docs/PHASE_4D_LIVE_TEST.md`.
Phase 4E-A Survey COM read-stability validation is tracked in `docs/PHASE_4E_A_LIVE_TEST.md`.
Phase 4E-B Survey project-list validation is tracked in `docs/PHASE_4E_B_LIVE_TEST.md`.
Phase 4E-C Survey network-list validation is tracked in `docs/PHASE_4E_C_LIVE_TEST.md`.
