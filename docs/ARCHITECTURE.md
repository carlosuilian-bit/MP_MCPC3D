# Architecture

Date: 2026-09-14

Status: Phase 2G generic object reads implemented and green offline, not yet live validated; Phase 3C AEC property-set numeric write/readback implemented and live validated; Phase 4E-C Survey COM network-list read remains the latest Survey milestone.

## Current Shape

```text
MCP Client
   |
   v
Official MCP C# SDK STDIO transport
   |
   v
Civil3D.HybridMcp.Server
   |
   | discovers endpoint descriptors
   | negotiates protocol/capabilities
   | reads tool manifest
   v
JSON-RPC 2.0 over Windows Named Pipe
   |
   v
Civil3D.HybridMcp.Bridge
   |
   | Autodesk host dispatcher boundary
   | endpoint heartbeat
   | read/diagnostic tools
   | AutoCAD layer read/write tools
   | Civil 3D alignment/surface/COGO read tools
   | Survey geometry / FBK dry-run / Survey DB status / Survey COM read tools
   v
Autodesk Civil 3D 2026
```

## Projects

| Project | Purpose | Autodesk references |
| --- | --- | --- |
| `Civil3D.HybridMcp.Protocol` | DTOs, protocol contracts, descriptors, envelopes | No |
| `Civil3D.HybridMcp.Core` | version negotiation, endpoint registry, confirmation hash/token, logging, pipeline interfaces | No |
| `Civil3D.HybridMcp.Sdk` | named-pipe JSON-RPC client/server | No |
| `Civil3D.HybridMcp.Diagnostics` | diagnostics interfaces and partial dialog monitor foundation | No |
| `Civil3D.HybridMcp.Server` | C# server CLI and bridge discovery/execution services | No |
| `Civil3D.HybridMcp.Tools.AutoCAD` | AutoCAD tool implementations, write pipeline for layer operations, and generic handle-addressed object reads | Yes |
| `Civil3D.HybridMcp.Tools.Civil` | Civil 3D READ tool implementations for alignments, surfaces, and drawing COGO points | Yes |
| `Civil3D.HybridMcp.Tools.Survey` | Pure Survey geometry calculations, inline FBK dry-run parsing, Survey Database readiness/status contracts, and read-only Survey COM contracts for working-folder and project-list reads | No |
| `Civil3D.HybridMcp.Bridge` | AutoCAD/Civil 3D plugin scaffold | Yes |

## Runtime Decision

The MCP Server runtime is C#/.NET. See `docs/decisions/ADR-0007-server-runtime.md`.

Reason:

- shared DTOs and contracts
- direct named-pipe support
- simpler Windows deployment
- better integration with the C# bridge
- future Roslyn Developer Mode can reuse the same assemblies

## External MCP Surface

The server exposes a real MCP STDIO entrypoint:

```powershell
Civil3D.HybridMcp.Server.exe mcp
```

The STDIO server uses the official C# MCP SDK package `ModelContextProtocol` and registers dynamic handlers for `tools/list` and `tools/call`.

The adapter behavior is:

- `tools/list` discovers a bridge endpoint, performs handshake, reads `ToolManifest`, and maps each `ToolDescriptor` to an MCP tool.
- Input and output JSON schemas are passed through from bridge descriptors.
- Tool metadata records module, operation class, risk, confirmation requirement, rollback semantics, and contract version.
- `tools/call` forwards arbitrary tool names and arguments to the bridge through the server's named-pipe client.
- MCP `structuredContent` contains the same structured success/error envelope expected by the platform.
- If no bridge is available, `tools/list` returns no tools and calls fail with `BRIDGE_UNAVAILABLE`.

STDIO logging is routed to stderr so stdout remains reserved for protocol messages.

## Transport

Transport is JSON-RPC 2.0 over Windows Named Pipes with NDJSON framing.

Pipe naming:

```text
Civil3DHybridMcp-{pid}-{instance_id}
```

Each request includes a JSON-RPC ID. Tool requests also include a `correlation_id`.

## Endpoint Registry

Directory:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\endpoints
```

File:

```text
civil3d-hybrid-mcp-{pid}-{instance_id}.json
```

Descriptors include:

- instance ID
- PID
- process start time
- product/version
- bridge/protocol version
- pipe name
- active document
- capabilities
- heartbeat timestamp

The server rejects stale descriptors, dead PIDs, process-start mismatches, and ambiguous instances.

## Capability and Version Model

Capabilities are explicit. Civil 3D being open does not imply every tool is available.

The bridge publishes:

- protocol version
- bridge version
- product version
- API versions when detectable
- module capability flags
- feature flags
- tool manifest version
- available tools

Protocol major mismatch is incompatible. Same-major lower bridge minor is partial. Same-major equal or newer bridge minor is compatible.

## Tool Manifest

The server does not hardcode the tool catalog. During handshake, the bridge returns tool descriptors containing:

- name
- description
- operation class
- risk
- input schema
- output schema
- timeout
- confirmation requirement
- rollback semantics
- module
- version

Phase 4E-B currently publishes 26 bridge tools through the MCP STDIO surface:

- `civil3d_ping`
- `civil3d_status`
- `drawing_info`
- `document_list`
- `layer_list`
- `layer_get`
- `layer_create`
- `layer_set_current`
- `layer_update`
- `alignment_list`
- `alignment_get`
- `alignment_station_offset`
- `alignment_point_location`
- `surface_list`
- `surface_get`
- `surface_elevation_at_xy`
- `cogo_list`
- `cogo_get`
- `survey_bearing_to_azimuth`
- `survey_azimuth_to_bearing`
- `survey_distance`
- `survey_point_by_azimuth`
- `survey_fbk_dry_run`
- `survey_database_status`
- `survey_working_folder_get`
- `survey_project_list`
- `survey_network_list`
- `profile_list`
- `profile_get`
- `profile_elevation_at_station`
- `corridor_list`
- `corridor_get`
- `list_dialogs` (does not cross the dispatcher; see below)
- `propertyset_definition_list`
- `propertyset_list`
- `propertyset_get`

## Autodesk Thread Boundary

The named-pipe server handles IPC on background threads. It must not touch Autodesk APIs directly.

The bridge routes Civil 3D and AutoCAD tool execution through `AutodeskIdleDispatcher`, which executes queued work from the AutoCAD `Application.Idle` event.

`layer_list`, `layer_get`, `layer_create`, `layer_set_current`, `layer_update`, the Civil 3D alignment/surface/COGO read tools, `survey_working_folder_get`, `survey_project_list`, `survey_network_list`, and the profile read tools all cross this dispatcher boundary before touching Autodesk APIs or Survey COM. Current Survey geometry, FBK dry-run, and Survey Database status tools are pure/readiness logic and do not touch Autodesk APIs or Survey COM.

## Document Identity

`DocumentDescriptor` includes:

- `instance_id`
- `document_id`
- `name`
- `full_path`
- `is_active`
- `is_saved`
- `is_read_only`
- `units`
- `database_fingerprint`
- `session_identifier`

The document ID is stable within the current bridge instance/session. It is not based solely on `ObjectId` or `Handle`.

## Confirmation Foundation

The current foundation implements:

- confirmation request contract
- confirmation token contract
- canonical arguments hash
- HMAC token service
- expiry validation
- bound-field validation

Only low-risk write tooling exists so far. No destructive tools exist yet, so no confirmation UI is implemented.

## Writes

Phase 3 implements three low-risk write tools: `layer_create`, `layer_set_current`, and `layer_update`.

The shared write flow is:

```text
deserialize request
  |
  v
validate instance_id and document_id
  |
  v
validate layer name and optional color index
  |
  v
execute on Autodesk idle context
  |
  v
Document.LockDocument()
  |
  v
StartTransaction()
  |
  v
check duplicate layer name
  |
  v
create LayerTableRecord
  |
  v
Commit()
```

If the operation fails before commit, the write pipeline calls `Abort()` and reports a structured error. The transaction state is included on successful `layer_create` responses.

No destructive tools exist yet. Current low-risk writes do not require confirmation because they operate on bounded targets, validate inputs, and use document locks plus Autodesk transactions. This includes layer writes and the Phase 3C `propertyset_numeric_test_create` probe, which creates only a prefixed test definition plus a temporary `DBPoint`.

## Dialog Contents

Phase 1D closed the gap `list_dialogs` declared about itself: it could name a blocking dialog but not read its message or its buttons.

`IWindowEnumerator` now also exposes `EnumerateChildControls`, and `DialogWindowInfo` carries `text`, `buttons`, and `content_read`.

The rules are shaped by when this tool runs, which is while the host is hung:

- contents are read only for windows already classified as dialogs; the main frame is never walked, because it owns far too many controls;
- the child sweep of one dialog stops at 128 controls;
- captions still go through `SendMessageTimeout` with `SMTO_ABORTIFHUNG`, the same rule Phase 1C established for titles;
- button and label controls are matched by Win32 class name, and anything else is ignored rather than stringified;
- a dialog whose children could not be enumerated reports `content_read: false`, because unknown buttons and absent buttons are different answers.

This is a prerequisite rather than an end in itself. `dismiss_dialog` stays unimplemented, and it cannot be designed safely until the platform can read the button a caller would be asking it to press.

## Write Failure Path

Phase 3D hardened what happens when a write does not succeed.

Two defects lived in the failure path of both write pipelines:

- when `Rollback()` itself failed, the pipeline replaced the original exception with the rollback error, so the caller was told the drawing might be dirty and never learned what had been attempted;
- `Dispose()` on both write scopes called `Rollback()` and let it throw, which replaced whatever exception was already propagating.

The rule now is:

- a failed rollback is reported as `RollbackFailed`, because a drawing left mid-change outranks a failed write, and the original cause is attached as the inner exception;
- `Dispose` never throws; it is a safety net, and the explicit `Rollback()` call in the pipeline is where a failed rollback is reported.

Both pipelines are covered for commit, rollback, rollback after commit, dispose without commit, failed rollback, and non-throwing dispose. `CivilWritePipeline`, which carries the Phase 3C AEC property-set write, had no coverage before this phase.

The fake scopes in the tests mirror the real scopes, including the completed-once guard and rollback on dispose. That is deliberate: the earlier fake ignored both, which is why a passing suite coexisted with a misbehaving scope.

## Live Validation Boundary

Automated tests validate the MCP STDIO adapter against a fake bridge and validate reconnection, protocol incompatibility, ambiguity rejection, bridge-unavailable behavior, structured errors, layer read tools, layer write behavior, property-set read/writeback behavior, duplicate rejection, rollback behavior, and correlation ID logging.

Autodesk autoload, named-pipe connection, MCP STDIO calls, layer reads, and the first safe write were live validated against Civil 3D 2026. Phase 2 used a user-confirmed test DWG and created `MCP_TEST_LAYER`.

Phase 2C was live validated against a separate user-confirmed test DWG with alignments, surfaces, and COGO points. It verified the 16-tool manifest, Civil 3D READ tools, station/offset round-trip, and surface elevation query.

Phase 4A was live validated against Civil 3D 2026 using the installed bundle. It verified the 22-tool manifest, Survey capability flags, and `survey_bearing_to_azimuth` execution through the live named pipe. See `docs/LIVE_SMOKE_TEST.md`, `docs/PHASE_2_LIVE_TEST.md`, `docs/PHASE_2C_LIVE_TEST.md`, `docs/PHASE_4A_LIVE_TEST.md`, and `docs/AUTODESK_ASSEMBLY_RESOLUTION.md`.

Phase 4B has been packaged and installed as a 23-tool bundle, passed the offline bundle diagnostic, and passed live smoke validation for `survey_fbk_dry_run` through the official MCP client against Civil 3D 2026.

Phase 4C installed and live smoke validated `survey_database_status`, a 24th READ/Low tool. It reports Survey Database adapter readiness. The pure service default remains `not_configured`, while the Autodesk bridge now performs a filesystem-only Survey runtime probe and live validated `environment_probe` / `interop_available` without initializing COM, opening Survey Databases, importing FBK, or mutating drawing state. See `docs/PHASE_4C_LIVE_TEST.md`.

Phase 4D installed and live smoke validated `survey_working_folder_get`, a 25th READ/Low tool. It adds a typed Survey COM adapter for working-folder reads only. The Autodesk bridge dispatches the COM call through the host context, requires an active Civil 3D document, and does not open Survey Databases, create projects, set working folders, import FBK, or mutate drawing state. The live Start/blank-drawing launch advertised the 25-tool catalog and returned structured `DocumentNotFound` for `survey_working_folder_get` because the bridge reported no active document. See `docs/PHASE_4D_LIVE_TEST.md`.

Phase 4E-A stabilized that same Survey COM path. The bridge now preserves structured tool exception details, maps no-active-document `drawing_info` failures to `DocumentNotFound`, returns Survey COM no-document details with `adapter`, `document_count`, and `requires_active_document`, and attaches the active `DocumentDescriptor` to successful host-context `survey_working_folder_get` responses. Live validation confirmed both the Start/no-document failure path and a successful read from a transient unsaved `Drawing1.dwg`. See `docs/PHASE_4E_A_LIVE_TEST.md`.

Phase 2F adds AEC property set reads, bringing the catalog to 36 tools. These are the first tools that reach outside the Civil 3D API into the AEC property data API in `AecPropDataMgd`, which required a new assembly reference in the Civil tools project. Definitions come from the drawing's property set definition dictionary; attached sets come from `PropertyDataServices`. See `docs/PHASE_2F_LIVE_TEST.md`.

Phase 3C adds `propertyset_numeric_test_create`, bringing the current catalog to 37 tools. It is the first AEC property-set write path: the bridge creates a prefixed test definition, creates a temporary `DBPoint`, attaches a property set, writes one `Real` value, and reads that value back through the existing property-set response shape. Live validation also showed that a `References - Not Found Files` modal can block handshake/tool execution, so live tests must read modal dialogs before dismissing or retrying. See `docs/PHASE_3C_LIVE_TEST.md`.

Phase 2G adds `object_get` and `object_properties`, bringing the development catalog to 39 tools. They are the platform's fallback path: a caller that meets an object with no typed tool can still identify it and read its properties. Identity comes from `RXClass`, so a Civil 3D alignment and an AEC object report through one shape without the AutoCAD module referencing either API, and property enumeration is reflection over the concrete runtime type rather than a hardcoded list. Both cross the Autodesk dispatcher boundary and open the object `ForRead`, which is what makes a read-only guarantee enforceable rather than merely intended: AutoCAD rejects a getter that tries to mutate a read-only object. Values are formatted with `CultureInfo.InvariantCulture` because the validation host runs pt-BR, where a default `ToString` would emit a decimal comma and break every caller that parses the number back. The AutoCAD module now routes by tool name, since it has two handlers. Live validation has not run yet; coverage so far is unit, fake bridge, and the offline bundle diagnostic.

Phase 1C installed and live smoke validated the modal dialog monitor, bringing the catalog to 33 tools. `list_dialogs` is the first tool that deliberately does not cross the Autodesk dispatcher boundary: it answers on the pipe thread from Win32 window state alone. Live validation called it while Civil 3D was blocked by the `References - Not Found Files` modal and it answered in 28 ms with `blocked_by_modal: true`, correctly separating the enabled `#32770` dialog from a disabled docked palette. `civil3d_status` now reports real dialog state and the `DialogMonitor` capability moved from `partial` to `active`. See `docs/PHASE_1C_LIVE_TEST.md`.

Phase 2E installed and live smoke validated `corridor_list` and `corridor_get`, bringing the catalog to 32 tools. `corridor_get` walks each baseline and region inside the same read-only transaction, resolving alignment, profile, and applied assembly names. Live validation read a 19-baseline corridor and, in the process, reproduced the modal-dialog blocker: `tools/list` answered instantly while every host-dispatched read hung until the `References - Not Found Files` dialog was dismissed. That is direct evidence for promoting the dialog monitor into typed tooling. See `docs/PHASE_2E_LIVE_TEST.md`.

Phase 2D installed and live smoke validated `profile_list`, `profile_get`, and `profile_elevation_at_station`, bringing the catalog to 30 READ/Low-dominated tools. Profiles are enumerated through `Alignment.GetProfileIds()` inside a read-only transaction, so every profile record carries its parent alignment. Live validation on a copy of the user-confirmed test drawing read 59 profiles across 32 alignments, resolved one by handle, and returned elevation and grade at a station. See `docs/PHASE_2D_LIVE_TEST.md`.

Phase 4E-C installed and live smoke validated `survey_network_list`, a 27th READ/Low tool. It extends the typed Survey COM adapter from the project collection to the network collection for one or all Survey projects, still requiring an active Civil 3D document and avoiding Survey Database and Survey project open/create/import/write behavior. Live validation confirmed structured `DocumentNotFound` with no active document, `status: "no_projects"` with a transient unsaved `Drawing1.dwg` on a host with no Survey projects, and structured `SurveyProjectNotFound` for an unmatched `project_name` filter. Network item enumeration remains covered by unit and fake-bridge tests only. See `docs/PHASE_4E_C_LIVE_TEST.md`.

Phase 4E-B installed and live smoke validated `survey_project_list`, a 26th READ/Low tool. It extends the typed Survey COM adapter to read the Survey project collection only, still requiring an active Civil 3D document and avoiding Survey Database open/create/import/write behavior. Live validation confirmed structured `DocumentNotFound` when Civil 3D had no active document, then opened a transient unsaved `Drawing1.dwg` and returned `status: "projects_available"`, `count: 0`, and response document `Drawing1.dwg`. See `docs/PHASE_4E_B_LIVE_TEST.md`.
