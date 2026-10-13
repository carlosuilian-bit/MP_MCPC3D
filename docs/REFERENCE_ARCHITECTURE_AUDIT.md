# Reference Architecture Audit

Date: 2026-09-04

Scope: Phase 0 audit only. No production code was implemented. The three reference repositories were treated as read-only technical references.

Reference repositories found:

- `hjlrosales/Civil3D_MCP`: `hjlrosales/Civil3D_MCP`
- `barbosaihan/civil3d-mcp`: `barbosaihan/civil3d-mcp`
- `KevinGriffin-new/civil3d-mcp`: `KevinGriffin-new/civil3d-mcp`

## Executive Conclusion

The initial product hypothesis is valid with some caveats:

- `hjlrosales/Civil3D_MCP` is the strongest reference for production core architecture, typed tools, dynamic tool discovery, named-pipe IPC, endpoint discovery, transaction discipline, and server hardening.
- `barbosaihan/civil3d-mcp` is the strongest reference for dynamic C# execution with Roslyn, but it must be treated as Developer Mode only. Its sandbox and transport model are not sufficient for production DWG operations.
- `KevinGriffin-new/civil3d-mcp` is the strongest reference for Survey, FBK, Survey COM exploration, Civil 3D modal dialog recovery, and practical field workflows. Its bridge is useful as a research artifact, but it is too monolithic and UI/COM heavy to become the core directly.

Recommended Phase 1 base:

1. Start with a `hjlrosales`-style separated MCP server, SDK/contracts, and Civil 3D bridge.
2. Use a named pipe restricted to the current user as the first IPC.
3. Create first-class `Diagnostics` and `Dialog Monitor` components from the start, informed by KevinGriffin.
4. Keep Roslyn/dynamic C# behind a disabled-by-default Developer Mode module informed by barbosaihan.
5. Build Survey as a separate module behind interfaces, with KevinGriffin as the main behavioral reference.

## Audit Method

Inspected locally:

- top-level repository structure
- README/build docs
- package manifests
- `.csproj` files
- Autodesk bundle manifests
- MCP server implementations
- bridge/plugin implementations
- tool registration and dispatch paths
- transaction, confirmation, timeout, cancellation, and logging code
- Survey/FBK/dialog handling code
- local test directories and regression scripts
- local license files and license metadata

No internet license lookup was needed for this phase because each repository contains local license evidence. Direct code reuse should still receive a final legal review before publication.

## 1. hjlrosales/Civil3D_MCP

### Summary

This repository is a production-oriented Autodesk MCP architecture. It separates the Node/TypeScript MCP server from a .NET Civil 3D bridge, uses JSON-RPC 2.0 over local Windows named pipes, discovers tools dynamically from bridge manifests, and contains a meaningful command/transaction framework.

It is the best reference for the core of the hybrid platform.

### Inventory

| Field | Finding |
| --- | --- |
| Language | C#/.NET 8 for SDK, bridge, domain, and tool modules; TypeScript/Node.js for MCP server and E2E tests. |
| Top-level structure | `.github`, `.vscode`, `ai-workflow`, `benchmarks`, `docs`, `e2e`, `eng`, `examples`, `packaging`, `src`, `tests`, `tools_tmp`, solution files, root `package.json`, `Directory.Build.props`, `README.md`, `LICENSE`. |
| Civil 3D target | Civil 3D 2025/2026 according to README and bundle/runtime requirements. |
| AutoCAD target | AutoCAD/Civil 3D `R24.3` to `R25.0` in bundle metadata; default SDK path points at AutoCAD 2025. |
| .NET target | `net8.0` and `net8.0-windows`. |
| Autodesk libraries | `acmgd`, `acdbmgd`, `accoremgd`, `AeccDbMgd`. Autodesk references are isolated mostly to bridge and Autodesk adapter projects. |
| MCP server | `src/server/Autodesk.Mcp.Server`, TypeScript ESM, Node >=20, `@modelcontextprotocol/sdk`, `ajv`, `pino`. |
| Bridge | `src/bridges/Civil3D.Bridge`, AutoCAD `IExtensionApplication`, DI composition root, hosted named-pipe service. |
| Protocol | JSON-RPC 2.0 over local Windows named pipes with NDJSON framing. Methods include handshake, tool list, execute, ping/health, shutdown, progress/cancel notifications. |
| Loading in Civil 3D | Autodesk `.bundle` under `packaging/Civil3D.Bridge.Bundle`, `LoadOnAutoCADStartup=True`. |
| Tool discovery | Reflection over `ITool` classes annotated with `[McpTool]`; bridge emits manifests; server registers tools dynamically. |
| Schemas | Generated from typed C# input/output DTOs with `NJsonSchema`; server normalizes/validates with AJV. |
| Multiple instances | Endpoint registry under `%LOCALAPPDATA%\AutodeskMcp\endpoints`, descriptor per process/bridge, stale cleanup by PID, server picks freshest/preferred endpoint. |
| Logging | Bridge uses Serilog rolling files under `%LOCALAPPDATA%\AutodeskMcp\logs`; server uses pino. Command pipeline publishes events. |
| Tests | Broad xUnit tests, server unit/integration tests, fake bridge tests, E2E tests for startup/discovery, execution, reconnect, cancel/progress, confirmation, multi-instance, and shutdown. |
| License | MIT, copyright `(c) 2026 hjlrosales`. |

### Tools Found

34 `[McpTool]` classes were found.

Representative tool catalog:

| Category | Tools |
| --- | --- |
| Bridge/general | `bridge_ping`, `bridge_version`, `bridge_get_capabilities` |
| Drawing/workflows | `drawing_info`, `drawing_summary`, `save_drawing`, `drawing_health_report`, `project_summary_report`, `design_validation_report`, `quantity_takeoff_report` |
| Alignments | `list_alignments`, `get_alignment`, `rename_alignment` |
| Surfaces | `list_surfaces`, `get_surface`, `rename_surface`, `surface_comparison_report`, `calculate_cut_fill` |
| Profiles | `list_profiles`, `get_profile` |
| Corridors | `list_corridors`, `get_corridor`, `corridor_analysis_report` |
| Pipe networks | `list_pipe_networks`, `get_pipe_network`, `create_pipe_network`, `create_pipe`, `update_pipe`, `delete_pipe` |
| COGO | `list_cogo_points`, `get_cogo_point` |
| Styles | `list_styles`, `get_style` |
| Objects/search | `search_objects` |
| Export | `export_landxml` |

Not observed as production tools in this reference: layers, blocks, property sets, Roslyn dynamic execution, Survey database, FBK, modal dialog tools.

### Read Commands

Strong read-only coverage for core Civil 3D object discovery:

- drawing info and summaries
- alignment list/get
- surface list/get
- profile list/get
- corridor list/get
- pipe network list/get
- COGO point list/get
- styles list/get
- search objects
- workflow/report reads

Read tools are marked with `ToolPermission.ReadOnly` and low/medium risk annotations depending on the operation.

### Write Commands

Observed writes:

- `save_drawing`
- `rename_alignment`
- `rename_surface`
- `create_pipe_network`
- `create_pipe`
- `update_pipe`
- `delete_pipe`
- export/report style workflows where applicable

Write tools use typed DTOs and many route through a command pipeline.

### Transactions, Rollback, and Document Lock

This is the strongest reference in the audit for transaction safety.

Observed design:

- command validation
- permission check
- confirmation gate
- document lock
- Autodesk transaction provider
- transaction pipeline
- commit on success
- rollback on exception, timeout, or cancellation
- structured transaction events

`AutodeskTransactionProvider` resolves the active document, locks it with `LockDocument()`, starts a transaction from `document.Database.TransactionManager`, and exposes commit/abort/dispose behavior. `TransactionPipeline` bypasses transactions for read-only commands and enforces one active writer.

### Confirmation

Architecture documents describe permission/confirmation flow and bridge-side re-checks. Code also includes `IConfirmationGate` and `NullConfirmationGate`.

Important gap: the inspected bridge composition registers `NullConfirmationGate`; `ExecuteToolRequest` has a `Confirm` field, but the inspected `ExecuteToolHandler` does not propagate that confirmation into `ToolInvocation` or the confirmation gate. Therefore confirmation exists architecturally, and tests/docs exercise the idea, but production-grade confirmation must be reworked rather than copied as complete.

### Timeout and Cancellation

Strong support:

- tool manifests carry timeout metadata
- server request timeout handling
- bridge dispatcher timeout handling
- cancellation registry
- JSON-RPC `$/cancel`
- progress notifications

This should be inherited conceptually for long-running Civil 3D operations.

### Modal Dialogs

No first-class modal dialog monitor was found in this reference. Timeout/cancellation exist, but modal detection/recovery should be sourced mainly from KevinGriffin and integrated into this style of core.

### Survey

No Survey database/FBK module was found in the inspected tool catalog. COGO points are present as Civil 3D drawing objects, but not Survey project/network/control point workflows.

### Roslyn / Sandbox

No dynamic C# execution mode was found. This is good for production core safety, but the hybrid platform needs a separate Developer Mode inspired by barbosaihan.

### Strengths to Inherit

- modular server/bridge separation
- dynamic tool discovery
- typed tool metadata
- generated JSON schemas
- CurrentUserOnly named-pipe IPC
- endpoint registry for multiple instances
- command pipeline with validation/permission/transaction
- structured result envelope
- robust server manager/reconnect logic
- fake bridge and E2E testing patterns

### Risks / Gaps

- confirmation path needs validation and likely redesign
- no modal dialog monitor
- no Developer Mode
- no Survey database/FBK support
- default SDK path targets AutoCAD 2025 and must be configurable for Civil 3D 2026
- direct code reuse still requires preserving MIT attribution and resolving product identity/namespaces

## 2. barbosaihan/civil3d-mcp

### Summary

This repository is centered on dynamic C# execution inside Civil 3D. The MCP server exposes only a small set of meta-tools and sends arbitrary C# to a .NET plugin, where Roslyn compiles and runs it with access to Civil 3D objects.

It is the best reference for Developer Mode, not for production tools.

### Inventory

| Field | Finding |
| --- | --- |
| Language | TypeScript MCP server; C#/.NET 8 Windows plugin; Markdown skill templates. |
| Top-level structure | `assets`, `C_References`, `plugin`, `skills`, `src`, package files, TypeScript config, README, LICENSE. |
| Civil 3D target | README and local references discuss Civil 3D/AutoCAD managed assemblies; `C_References` README mentions copying Autodesk DLLs locally, commonly from AutoCAD 2025. |
| AutoCAD target | Not formally expressed in bundle metadata. Depends on DLLs copied into `C_References`. |
| .NET target | `net8.0-windows`; plugin has `EnableDynamicLoading`. |
| Autodesk libraries | `accoremgd`, `AcDbMgd`, `acmgd`, `AecBaseMgd`, `AeccDbMgd` from `C_References`. |
| MCP server | TypeScript stdio MCP server using `@modelcontextprotocol/sdk`, `zod`, and `ws`. |
| Bridge | .NET AutoCAD plugin with manual commands `C3DMCPSTART`, `C3DMCPSTOP`, `C3DMCPSTATUS`. |
| Protocol | TCP socket on loopback, default port 8080, JSON-RPC-like payloads. |
| Loading in Civil 3D | Manual `NETLOAD` implied; no Autodesk bundle/autoload manifest found. |
| Tool discovery | Static registration of three MCP meta-tools. No dynamic production tool catalog. |
| Schemas | Zod schemas for meta-tools only. C# code itself is not schema-typed as a Civil 3D operation. |
| Multiple instances | Not supported cleanly; fixed port 8080 conflicts across instances. |
| Logging | Console/debug style logging in server and plugin. No audit log model comparable to hjlrosales. |
| Tests | No local test suite was found in the inspected repository. |
| License | Local `LICENSE` is MIT, copyright `(c) 2025 lisiting01`; `package.json` says `ISC`, creating a metadata conflict. |

### Tools Found

3 MCP meta-tools:

- `civil3d_execute`: run C# intended to modify the drawing and commit transaction
- `civil3d_query`: run C# intended for read-only queries
- `civil3d_skills`: list/search/get local skill templates

Skill templates found:

- alignments: list alignments, station/offset
- drawing: drawing info
- geometry: create polyline
- points: create COGO points, list COGO points
- surfaces: create TIN surface, list surfaces, surface elevation, surface volume
- workflows: earthwork report

### Read Commands

`civil3d_query` marks the operation as read-only and the bridge does not commit the transaction. However, the dynamic code still receives powerful globals and can invoke APIs that create side effects outside the transaction model. This is not enough to treat arbitrary query code as guaranteed non-mutating.

### Write Commands

`civil3d_execute` sends arbitrary C# to the plugin with `readOnly=false`. The plugin locks the active document, starts a transaction, executes the script, and commits.

There are no production-grade typed write tools with impact previews, explicit permission metadata, or confirmation policy.

### Transactions, Rollback, and Document Lock

Observed:

- main-thread execution through AutoCAD command context
- active document resolution
- `doc.LockDocument()`
- `database.TransactionManager.StartTransaction()`
- commit only for non-read-only executions
- uncommitted transaction disposal acts as rollback for transactional DB changes

Gaps:

- no semantic enforcement that `civil3d_query` is read-only
- arbitrary C# may use non-transactional APIs
- no operation-scoped impact preview
- no tool-specific validators
- no structured permission model

### Confirmation

No confirmation flow was found. The distinction is only `query` versus `execute`.

### Timeout and Cancellation

Roslyn execution uses a 120 second timeout token. The socket client also has request timeouts. There is no robust per-operation cancellation protocol comparable to hjlrosales.

### Modal Dialogs

No dedicated modal dialog detection or recovery tools were found.

### Survey

No Survey database/FBK implementation was found. COGO point templates exist, but no Survey project/network/control point workflows.

### Roslyn / Sandbox

This is the strongest Roslyn reference.

Observed design:

- runtime compilation with `Microsoft.CodeAnalysis.CSharp.Scripting`
- globals exposed through `ScriptContext`: `Document`, `CivilDoc`, `Database`, `Transaction`, `Editor`
- imports AutoCAD/Civil namespaces
- references loaded AppDomain assemblies
- script caching
- timeout around script execution

Sandbox risks:

- regex-based blocked pattern scanning
- broad assembly exposure through all loaded non-dynamic assemblies
- no Roslyn syntax/semantic analyzer
- no detection of Autodesk destructive calls such as `.Erase(`, purge, collection deletion, or mass write operations
- no allowlist-based assembly policy
- fixed TCP loopback port without per-user ACL

### Strengths to Inherit

- Developer Mode concept
- useful C# globals for Civil 3D scripting
- Roslyn scripting host pattern
- local skill-template library concept
- separation between query and execute as a first risk signal

### Risks / Gaps

- should not be used as production core
- dynamic execution disabled by default in the hybrid platform
- sandbox must be rebuilt around allowlists and Roslyn analyzers
- no strong transport security for production
- no multi-instance support
- no confirmation/preview/permission model
- license metadata conflict must be resolved before direct reuse

## 3. KevinGriffin-new/civil3d-mcp

### Summary

This repository is a practical Civil 3D/MicroSurvey automation bridge with strong Survey, FBK, field-data, and modal dialog handling work. It uses a Python FastMCP server that proxies requests to a .NET plugin over localhost HTTP.

It is the best Survey and modal-dialog reference, but not a good core architecture reference because the bridge is monolithic and includes experimental escape hatches.

### Inventory

| Field | Finding |
| --- | --- |
| Language | Python MCP server; C#/.NET 8 Windows plugin; batch/build scripts. |
| Top-level structure | `docs`, `installer`, `mcp-server`, `plugin`, `testdata`, build docs, README, LICENSE. |
| Civil 3D target | Civil 3D 2026 is the practical target in build docs and hardcoded references. |
| AutoCAD target | AutoCAD 2026 paths hardcoded in `Civil3dMcpBridge.csproj`; bundle runtime range includes `R24.0` to `R25.0`. |
| .NET target | `net8.0-windows`, x64. |
| Autodesk libraries | `AcMgd`, `AcDbMgd`, `AcCoreMgd`, `AecBaseMgd`, `AeccDbMgd`, `AeccCogoMgd`, `AeccUiMgd`; AutoCAD and Civil 3D COM interop; Survey COM interop. |
| MCP server | Python FastMCP stdio server using `fastmcp` and `httpx`. |
| Bridge | Single large C# plugin file, `Civil3dMcpBridge.cs`, about 6,341 lines. |
| Protocol | HTTP JSON over `http://localhost:8270/` using `HttpListener`. |
| Loading in Civil 3D | Manual `NETLOAD`/commands in docs; installer/bundle metadata exists. Hot-reload workflow loads versioned bridge assemblies. |
| Tool discovery | Static Python `@mcp.tool()` functions and a C# switch dispatcher. |
| Schemas | Python type hints/docstrings for tool signatures; no bridge-generated schema catalog. |
| Multiple instances | Not robust; fixed localhost port creates ambiguity/conflict across instances. |
| Logging | Python stderr logging; bridge trace logs under plugin traces with timestamp/session/action/params/result/status/duration/error. |
| Tests | Regression script for live bridge and no-CAD parser-style checks; test data exists. No broad unit/integration suite comparable to hjlrosales. |
| License | MIT, copyright `(c) 2026 Kevin Griffin`. |

### Tools Found

54 Python MCP tools were found in `mcp-server/server.py`.

Representative catalog:

| Category | Tools |
| --- | --- |
| Status/drawing | `ping`, `get_drawing_info`, `get_drawing_settings`, `set_drawing_settings` |
| Layers | `list_layers`, `create_layer`, `set_current_layer` |
| Parcels | `list_parcels`, `get_parcel_layout_settings`, `create_parcels_from_objects` |
| Geometry | `draw_line`, `draw_line_by_bearing`, `draw_line_by_azimuth`, `draw_arc`, `draw_polyline`, `offset_entity`, `import_plan_json` |
| COGO | `list_cogo_points`, `get_cogo_point`, `edit_cogo_point`, `create_cogo_point`, `import_points_file` |
| Styles/labels | `list_point_styles`, `create_point_style`, `list_point_label_styles`, `create_point_label_style`, `list_line_label_styles`, `create_line_label_style`, `set_text_style_font`, `add_labels_to_layer` |
| Surfaces/raster | `create_surface_from_points`, `surface_volume`, `generate_contours`, `attach_raster_image` |
| Survey database | `get_survey_working_folder`, `set_survey_working_folder`, `create_survey_project`, `list_survey_projects`, `create_survey_network`, `list_survey_networks` |
| Survey details | `survey_network_summary`, `list_survey_control_points`, `list_survey_non_control_points`, `list_survey_setups`, `list_survey_observations`, `add_survey_points_to_drawing` |
| FBK | `parse_fbk`, `import_fbk_parsed` |
| Dialog/recovery | `list_dialogs`, `dismiss_dialog`, `cancel_command` |
| Fallback | `send_command` |

The bridge switch contains additional actions not exposed as MCP tools, including `open_drawing`, `switch_document`, raw `import_fbk`, figure prefix database operations, figure operations, COM discovery/invoke helpers, variable get/set, and assembly loading helpers.

### Read Commands

Strong read coverage in:

- drawing info/settings
- layers
- parcels
- COGO points
- point/label styles
- surfaces/volume/contours
- Survey working folder/projects/networks
- Survey control/non-control points
- Survey setups/observations
- figures and figure properties in bridge-only code
- dialog enumeration

### Write Commands

Observed writes include:

- drawing settings
- create layer / set current layer
- draw line/arc/polyline
- import plan JSON
- create/edit COGO point
- create styles and label styles
- import point file
- create surface from points
- create volume surface
- configure contours
- attach raster image
- create Survey project/network
- import parsed FBK via COM
- add Survey points to drawing
- set Survey user settings
- create Figure Prefix Database and entries
- set figure elevations
- COM invoke, command/script, set system variables, load assemblies

This breadth is useful for discovery, but too powerful for a safe production tool surface without a stricter permission model.

### Transactions, Rollback, and Document Lock

Observed:

- most non-bypass bridge actions are executed on AutoCAD main command context
- `ExecuteOnMainThread` locks the active document
- many handlers start and commit their own AutoCAD transactions

Gaps:

- no central command pipeline
- no uniform rollback policy across all write operations
- Survey COM operations and `SendStringToExecute` commands are often outside standard Autodesk transaction rollback
- queued commands can complete after the tool returns
- write and destructive classes are not centrally annotated

### Confirmation

No systematic confirmation policy was found. Dangerous helpers such as `send_command`, `setvar`, `load_assembly`, `com_invoke`, and bulk Survey/figure operations show why this reference must be mined carefully rather than exposed directly.

### Timeout and Cancellation

Python HTTP client uses a 30 second timeout. Bridge main-thread dispatch waits up to 5 minutes before timeout. `cancel_command` sends ESC characters using `SendStringToExecute` from a bypass path.

This is useful operationally, but should be redesigned into explicit long-running operation, heartbeat, progress, and cancellation support.

### Modal Dialogs

This is the strongest modal dialog reference.

Observed design:

- bridge has `DialogProbe` using Win32 enumeration
- strict current-process PID filter
- main-window-disabled signal to identify true modal blockers
- child controls inspected for static text and button captions/IDs
- normal actions pre-check for open dialogs and return `blocked_by_dialog`
- post-check attaches dialog details to successful responses
- bypass actions include `ping`, `list_dialogs`, `dismiss_dialog`, `cancel_command`
- `dismiss_dialog` supports `[CLOSE]`, `[OK]`, `[CANCEL]`, `#controlId`, and caption matching

This should become a core `Diagnostics` component, not only a Survey feature.

### Survey

This is the strongest Survey reference.

Observed capabilities:

- Survey working folder get/set
- Survey project create/list
- Survey network create/list/summary/update
- control point and non-control point enumeration
- setup and observation enumeration
- adding network points to drawing
- Survey user settings
- Figure Prefix Database list/create/entries
- figure list/properties/elevations
- PNEZD import to SDB
- FBK dry-run parser
- direct parsed FBK import using Survey COM objects
- raw command-based FBK import fallback

Notable Survey implementation lessons:

- Civil 3D Survey COM requires several resolution strategies.
- Some `Projects.Create()` return values can be unsafe to touch immediately in Civil 3D 2026; the repository works around this by discarding the returned COM pointer and re-resolving later.
- `OpenEx` must be controlled to avoid reopening already-open Survey projects.
- FBK direct import is implemented by parsing records and creating control points, setups, and observations through COM.
- Raw `IMPORTFIELDBOOK` command fallback is queued and cannot synchronously guarantee completion.

### Roslyn / Sandbox

No Roslyn dynamic C# host was found.

Bridge-only diagnostic helpers such as COM discovery/invoke and assembly loading are dynamic/powerful, but they are not a structured Roslyn Developer Mode.

### Strengths to Inherit

- Survey COM exploration results
- FBK parser and dry-run/import workflow concept
- Survey project/network/control/setup/observation concepts
- figure and figure prefix database concepts
- modal dialog monitor design
- practical recovery tools: list dialogs, dismiss dialog, cancel command
- regression script ideas around point import and no-CAD parser checks

### Risks / Gaps

- bridge is a monolithic dispatcher
- fixed HTTP port exposes ambiguity for multiple instances
- no per-user pipe ACL
- static duplicated schemas between Python and C# switch cases
- broad fallback tools are too dangerous for production exposure
- Survey COM and queued command operations are not uniformly transactional
- hardcoded AutoCAD/Civil 3D 2026 reference paths
- some docs are stale relative to implementation size/capabilities

## Cross-Reference Findings

### Best Source by Concern

| Concern | Best reference | Rationale |
| --- | --- | --- |
| Production server/bridge split | hjlrosales | Cleanest separation and strongest protocol design. |
| Typed tool catalog | hjlrosales | Attributes, DTOs, generated schemas, dynamic manifests. |
| Secure local IPC | hjlrosales | Current-user Windows named pipes and endpoint registry. |
| Multiple Civil 3D instances | hjlrosales | Registry descriptors and endpoint selection. |
| Transactions/rollback | hjlrosales | Central command/transaction pipeline. |
| Dynamic C# / Roslyn | barbosaihan | Purpose-built runtime C# execution host. |
| Developer skill templates | barbosaihan | Practical skill library for code generation. |
| Survey/FBK | KevinGriffin | Broadest Survey COM and FBK coverage. |
| Modal dialog recovery | KevinGriffin | Concrete Win32 dialog probing and bypass tools. |
| Bulk field workflows | KevinGriffin | Practical COGO/import/surface/field-data workflows. |

### Target Gaps None of the References Solve Fully

- single, enforced confirmation model across server and bridge
- production-grade Roslyn sandbox with syntax/semantic checks and Civil 3D destructive API classification
- unified Survey operations with safe rollback or explicit non-rollback semantics
- reliable long-running operation model with progress/heartbeat/cancel for Civil 3D rebuild/import work
- complete MVP catalog including layers, blocks, property sets, alignments, profiles, corridors, surfaces, COGO, and diagnostics in one typed system
- precise object identity model across transaction/session/save/reopen
- formal security model for destructive operations and dynamic execution

## Recommended Inheritance Policy

| Repository | Inherit | Do not inherit directly |
| --- | --- | --- |
| hjlrosales | architecture, dynamic manifests, named-pipe endpoint registry, command pipeline, transaction patterns, test architecture | exact confirmation implementation without fixing propagation; product naming; 2025 path assumptions |
| barbosaihan | Developer Mode concept, Roslyn globals, skill-template idea | fixed TCP port, regex-only sandbox, all-assembly exposure, meta-tool-only production surface |
| KevinGriffin | Survey domain lessons, modal dialog monitor, FBK parser/import concepts, regression ideas | monolithic dispatcher, fixed HTTP port, broad COM/command escape hatches as normal tools, hardcoded 2026 paths |

## Phase 0 Decision

Proceed to Phase 1 only after accepting the initial architecture proposal and ADRs. No production code should be created until the team confirms:

- final project/module names
- IPC endpoint directory and pipe naming convention
- confirmation UX/token strategy
- Civil 3D 2026 SDK path detection strategy
- minimum Phase 1 tool slice

