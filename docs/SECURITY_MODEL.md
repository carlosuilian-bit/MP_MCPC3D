# Security Model

Date: 2026-09-14

Status: Phase 3C foundation with bounded low-risk AutoCAD layer writes, a bounded AEC property-set numeric write/readback probe, pure Survey geometry READ tools, FBK dry-run preview, Survey Database readiness/status reporting, and typed read-only Survey COM working-folder/project/network-list tools.

## Trust Boundary

The MCP Server and Civil 3D Bridge communicate locally through a Windows named pipe. The pipe is created with current-user isolation.

No TCP or HTTP port is exposed by the core transport.

The external MCP surface uses STDIO. It does not expose a network listener.

## Operation Classes

Every tool declares:

- `READ`
- `WRITE`
- `DESTRUCTIVE`

The current catalog implements:

- READ/Low tools: `civil3d_ping`, `civil3d_status`, `document_list`, `drawing_info`, `layer_list`, `layer_get`, `alignment_list`, `alignment_get`, `alignment_station_offset`, `alignment_point_location`, `surface_list`, `surface_get`, `surface_elevation_at_xy`, `cogo_list`, `cogo_get`, `survey_bearing_to_azimuth`, `survey_azimuth_to_bearing`, `survey_distance`, `survey_point_by_azimuth`, `survey_fbk_dry_run`, `survey_database_status`, `survey_working_folder_get`, `survey_project_list`, `survey_network_list`, `profile_list`, `profile_get`, `profile_elevation_at_station`, `corridor_list`, `corridor_get`, `list_dialogs`, `propertyset_definition_list`, `propertyset_list`, `propertyset_get`
- WRITE/Low tools: `layer_create`, `layer_set_current`, `layer_update`, `propertyset_numeric_test_create`
- no DESTRUCTIVE tools

## Risk Levels

Every tool also declares:

- `Low`
- `Medium`
- `High`
- `Critical`

All implemented tools are Low risk.

## Confirmation

The bridge must never trust a plain `confirmed=true` flag.

The current foundation implements:

- confirmation request contract
- confirmation token contract
- canonical arguments hash
- HMAC signature
- expiry
- validation of bound fields

Bound fields:

- tool
- instance
- document
- correlation ID
- arguments hash
- affected count
- operation class
- expiration

No destructive tools are present.

`layer_create`, `layer_set_current`, `layer_update`, and `propertyset_numeric_test_create` do not require confirmation because each operation is narrowly scoped, validates its target and arguments, and runs inside a document lock plus transaction. `layer_update` additionally rejects no-op updates, duplicate rename targets, and turning off or freezing the current layer. `propertyset_numeric_test_create` creates only a prefixed test definition plus a temporary `DBPoint` and rejects attempts to target existing objects or non-test definition names. Future bulk writes, deletes, purge operations, and destructive Civil 3D operations still require explicit confirmation.

## Endpoint Safety

Endpoint descriptors are accepted only when:

- JSON parses successfully
- heartbeat is fresh
- PID is alive
- process start time matches
- pipe name exists in descriptor

If multiple valid endpoints exist and no instance ID is provided, the server rejects the request with `INSTANCE_AMBIGUOUS`.

## Document Identity

Future sensitive operations must bind to:

- `instance_id`
- `document_id`

`ObjectId` and `Handle` are not sufficient for document identity.

## Autodesk API Threading

Named-pipe handlers do not directly access Autodesk APIs. The bridge routes Autodesk tool execution through `AutodeskIdleDispatcher`, which runs queued work from the AutoCAD application idle context.

AutoCAD layer write tools then enter the write pipeline:

```text
Document.LockDocument()
StartTransaction()
execute write
Commit()
```

On exception before commit, the pipeline calls transaction `Abort()`.

## Logging

Initial request logs include:

- timestamp
- correlation ID
- session
- instance
- document when available
- tool
- duration
- status
- error code/message

Default path:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\logs
```

Full sensitive payloads are not logged by default.

Bridge startup logs also record Autodesk reference/runtime probes and endpoint registration.

## MCP Adapter Safety

The MCP adapter is a protocol surface only. It does not handle bridge logic directly and does not touch Autodesk APIs.

Safety behavior:

- tool catalog comes from the bridge manifest
- tool schema, risk, operation class, confirmation, and rollback metadata come from bridge descriptors
- calls are forwarded through `HybridMcpServer.ExecuteToolAsync`
- no valid endpoint returns `BRIDGE_UNAVAILABLE`
- multiple valid endpoints without selection returns `INSTANCE_AMBIGUOUS`
- protocol major mismatch returns `PROTOCOL_INCOMPATIBLE`
- stdout is reserved for MCP messages; logs go to stderr or JSONL files

Implemented WRITE tools using this path:

- `layer_create`
- `layer_set_current`
- `layer_update`
- `propertyset_numeric_test_create`

## Survey Safety

The current Survey module is limited to pure calculations, inline FBK dry-run parsing, Survey Database readiness/status reporting, and typed read-only Survey COM working-folder/project-list reads. It has no arbitrary file access, no drawing mutation, no Survey Database open/import flow, and no transaction requirement.

`survey_fbk_dry_run` accepts FBK text content, not a bridge-side file path. This avoids arbitrary file reads from the AutoCAD process while still allowing parser validation and import preview.

`survey_database_status` reports `not_configured` when no gateway is injected. In the Autodesk bridge, Phase 4C uses a filesystem-only `environment_probe` that checks known Survey runtime files under the installed Civil 3D directory and can report `interop_available`. It does not initialize COM, open Survey Databases, set working folders, or import data.

`survey_working_folder_get`, `survey_project_list`, and `survey_network_list` are the current Phase 4E-C Survey COM operations. They are dispatched through the Autodesk host context, require an active document, and return `DocumentNotFound` if the bridge cannot see an active document. `survey_working_folder_get` reads `IAeccSurveyProjects.GetWorkingFolder()`. `survey_project_list` reads project collection metadata only. `survey_network_list` reads `IAeccSurveyProject.Networks` metadata only and never calls `IAeccSurveyNetworks.Create` or `IAeccSurveyNetworks.Remove`. None of these tools open Survey Databases or Survey projects, set working folders, create projects, import FBK, or mutate drawing state.

Survey Database reads beyond readiness/status, working-folder read, and project-list read, FBK import, figure prefix, and broader Survey COM operations remain future work and must be introduced behind typed adapters with explicit validation.

## Known Current Limits

- modal dialog listing exists, but `dismiss_dialog` and `cancel_active_command` are intentionally not implemented yet
- write support is still limited to bounded AutoCAD layer operations and the bounded property-set numeric test probe
- no layer delete
- no destructive tools
- no Survey Database/COM reads beyond readiness/status, working-folder read, project-list read, and network-list read; no FBK import, Developer Mode, generic Civil 3D writes, blocks, general property-set writes, corridor/profile writes, or Roadway tools

## Civil 3D Profile Reads

`profile_list`, `profile_get`, and `profile_elevation_at_station` are READ/Low Civil 3D .NET API tools. They resolve profiles through their parent alignments inside a read-only AutoCAD transaction and never create, edit, or delete profiles.

Profile names are unique only within a parent alignment. When a name matches profiles under more than one alignment and the caller supplies neither `alignment_name` nor `handle`, these tools return `PROFILE_AMBIGUOUS` listing the competing alignments instead of guessing a target. A name that matches nothing returns `PROFILE_NOT_FOUND`.

## Civil 3D Corridor Reads

`corridor_list` and `corridor_get` are READ/Low Civil 3D .NET API tools. They read the corridor collection inside a read-only AutoCAD transaction.

`corridor_list` returns lightweight summaries with baseline and surface counts only. `corridor_get` additionally walks each baseline and its regions, resolving alignment, profile, and applied assembly names.

These tools never call `Corridor.Rebuild()`, `SetTargets`, `ExportSolids`, or any other corridor mutation member. Corridor rebuild remains unimplemented because it is a write operation with long-running and progress semantics that the platform has not yet designed.

## Modal Dialog Monitor

`list_dialogs` is READ/Low and is the one tool that deliberately bypasses the Autodesk host dispatcher. That is a safety property, not a shortcut: it is the tool an operator calls when the host thread is already blocked, so routing it through that thread would make it useless exactly when it matters.

Its safety boundary:

- enumerates windows of its own process only
- reads titles with `SendMessageTimeout` and `SMTO_ABORTIFHUNG`, never `GetWindowText`, because the latter sends a synchronous message that a hung UI thread would never answer
- never posts input, never closes, activates, or moves a window
- does not touch the drawing, the document set, or any Autodesk API

It reports window state only. It cannot read dialog body text or button captions, so it cannot distinguish a harmless notification from a destructive confirmation. AGENTS.md section 14 therefore still applies in full: no dialog may be dismissed without first identifying it.

`dismiss_dialog` is intentionally not implemented yet. When it arrives it must be WRITE class, must require the caller to pass the expected dialog title, and must refuse any dialog outside a reviewed allowlist.

## AEC Property Set Reads

`propertyset_definition_list`, `propertyset_list`, and `propertyset_get` are READ/Low tools over the AEC property data API in `AecPropDataMgd`, not the Civil 3D API.

They read inside a read-only AutoCAD transaction and never call `PropertyDataServices.AddPropertySet`, `RemovePropertySet`, or `PropertySet.SetAt`. General property set writes remain unimplemented.

Failure policy follows the platform rule that a partial read must not be silently empty:

- an object handle that does not resolve returns `OBJECT_NOT_FOUND`, never an empty list
- a requested property set that is not attached returns `PROPERTY_SET_NOT_FOUND`
- an object with no property data at all returns an empty list with `count: 0`, which is a real answer rather than an error
- a single unreadable definition or property is skipped so one bad record cannot fail the whole request

## AEC Property Set Numeric Test Write

`propertyset_numeric_test_create` is WRITE/Low and exists to validate the complete create/write/read path for numeric AEC property data before the project exposes general `propertyset_set`.

Its safety boundary:

- requires a definition name starting with `MCP_NUMERIC_TEST`
- creates a new `DBPoint` target instead of modifying an existing production object
- supports only one property with AEC `Real` data in the initial implementation
- accepts only finite numeric values
- uses `Document.LockDocument()`, an AutoCAD transaction, commit on success, and rollback-before-commit on failure
- returns the created object handle and the attached property set so the same value can be verified through `propertyset_get`

It does not remove property sets, update arbitrary existing objects, overwrite existing production definitions, erase entities, or perform batch edits.
