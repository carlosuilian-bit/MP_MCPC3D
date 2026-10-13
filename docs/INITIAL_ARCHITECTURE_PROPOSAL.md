# Initial Architecture Proposal

Date: 2026-09-04

Scope: proposal only. This document defines the recommended direction for Phase 1 after completing the Phase 0 audit. It does not implement production code.

## Recommendation

Adopt a production core based primarily on the `hjlrosales/Civil3D_MCP` architecture, with a Survey module informed by `KevinGriffin-new/civil3d-mcp` and a disabled-by-default Developer Mode informed by `barbosaihan/civil3d-mcp`.

The platform should not merge the three repositories. It should reimplement a new modular system with explicit safety boundaries.

## Target Shape

```text
MCP Client
   |
   v
Hybrid MCP Server
   |
   |-- Tool Registry
   |-- Tool Schemas
   |-- Permissions
   |-- Confirmation Policy
   |-- Session Manager
   |-- Logging
   |-- Developer Mode Gate
   |
   v
Secure Local IPC - Windows Named Pipe, current user only
   |
   v
Civil 3D Bridge
   |
   |-- Core
   |-- AutoCAD API
   |-- Civil 3D API
   |-- Survey
   |-- Dialog Monitor
   |-- Dynamic Execution Host
   |
   v
Autodesk Civil 3D 2026
```

## Proposed Source Layout

```text
src/
  Civil3D.HybridMcp.Server/
  Civil3D.HybridMcp.Protocol/
  Civil3D.HybridMcp.Sdk/
  Civil3D.HybridMcp.Bridge/
  Civil3D.HybridMcp.Core/
  Civil3D.HybridMcp.Tools.AutoCAD/
  Civil3D.HybridMcp.Tools.Civil3D/
  Civil3D.HybridMcp.Tools.Survey/
  Civil3D.HybridMcp.Tools.Roadway/
  Civil3D.HybridMcp.DeveloperMode/
  Civil3D.HybridMcp.Diagnostics/
tests/
docs/
packaging/
```

Names can change, but the boundaries should remain.

## Design Answers

### 1. Which reference should define the core?

Use `hjlrosales/Civil3D_MCP` as the primary core reference.

Reason:

- strongest separation between server, bridge, SDK/contracts, domain, and tools
- best dynamic manifest discovery model
- best transaction and command-pipeline model
- best server hardening and fake bridge tests

### 2. Which IPC should be used first?

Use Windows Named Pipes with current-user restriction.

Reason:

- matches the project safety requirement to avoid unnecessary network exposure
- supports per-process endpoints
- avoids fixed TCP/HTTP port collisions
- aligns with the strongest production reference

Do not use fixed TCP 8080 or HTTP 8270 as the production core transport.

### 3. How should Civil 3D instances be discovered?

The bridge should write endpoint descriptors under a per-user local folder, for example:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\endpoints
```

Each descriptor should include:

- `instance_id`
- `pid`
- `process_start_time`
- `product`
- `product_version`
- `bridge_version`
- `pipe_name`
- `started_utc`
- `last_heartbeat_utc`
- `active_document`
- `capabilities`

The server should ignore stale descriptors and never execute a sensitive write when multiple matching instances are ambiguous.

### 4. How should documents be identified?

Use a structured document reference:

```json
{
  "instance_id": "...",
  "document_id": "...",
  "name": "Projeto.dwg",
  "full_path": "C:\\...",
  "is_active": true,
  "database_fingerprint": "...",
  "session_handle": "..."
}
```

`ObjectId` is transaction/session scoped. `Handle` can survive saves better but can still be invalid after certain drawing operations. The hybrid platform should document identifier lifetime explicitly.

### 5. Where should the Tool Registry live?

The bridge should be the source of truth for available Civil 3D tools because the bridge knows which Autodesk APIs are actually loaded.

The server should:

- request the tool manifest during handshake
- dynamically register MCP tools
- validate input schemas
- forward calls with correlation IDs
- enforce server-side policy

The bridge should:

- validate again
- enforce operation class and confirmation requirements
- execute through the proper module

### 6. How should schemas work?

Each tool should have typed request/response DTOs in .NET. Schemas should be generated from those DTOs and published in the bridge manifest.

Validation should run at two layers:

- MCP server: early user-facing schema rejection
- bridge: final trusted validation before touching Civil 3D

### 7. What operation classes should exist?

Use explicit operation classes:

- `READ`: no drawing mutation
- `WRITE`: controlled create/update operations
- `DESTRUCTIVE`: delete, erase, purge, mass modification, or irreversible/low-rollback actions

Risk should be separate from operation class:

- `Low`
- `Medium`
- `High`
- `Critical`

Example: `save_drawing` is WRITE and may be Medium risk. `object_delete` is DESTRUCTIVE and High risk.

### 8. How should confirmation work?

Confirmation must be a first-class protocol object, not a boolean passed casually.

Recommended flow:

1. Tool validates input and computes impact preview.
2. Server receives `requires_confirmation` result or manifest flag.
3. User confirms exact action, scope, affected count, document, and rollback semantics.
4. Server sends a signed or correlation-bound confirmation token to the bridge.
5. Bridge verifies token, document, tool, arguments hash, and expiration.
6. Bridge executes.

Confirmation payload should include:

```json
{
  "action": "delete",
  "tool": "object_delete",
  "document": "Projeto.dwg",
  "object_type": "Alignment",
  "affected_count": 3,
  "scope": "selected handles",
  "rollback": "transaction rollback before commit",
  "arguments_hash": "..."
}
```

### 9. How should rollback be implemented?

All production WRITE and DESTRUCTIVE drawing operations should run through a central command pipeline:

```text
validate
  -> acquire document lock
  -> start transaction
  -> execute
  -> validate result
  -> commit
```

Failure path:

```text
exception/cancel/timeout
  -> abort transaction
  -> return structured error
  -> log rollback
```

Survey COM and command-queued operations may not be fully transaction rollback capable. Such tools must state this in the preview and may require stronger confirmation.

### 10. How should modal dialogs be handled?

Implement `Dialog Monitor` in `Diagnostics`, informed by KevinGriffin.

Core tools:

- `civil3d_status`
- `list_dialogs`
- `dismiss_dialog`
- `cancel_active_command`

Design:

- pre-check dialogs before normal tool execution
- allow bypass tools for status/dialog/cancel only
- include dialog title, message, buttons, class name, hwnd, and owned-by-main signal
- never auto-click destructive or ambiguous prompts
- attach post-call dialog information to results

### 11. How should Survey be integrated?

Create a separate Survey module.

Recommended abstractions:

- `ISurveyGateway`
- `ISurveyProjectRepository`
- `ISurveyNetworkRepository`
- `IFbkParser`
- `IFbkImportService`
- `IFigurePrefixRepository`

Survey module should support:

- working folder
- projects
- networks
- control/non-control points
- setups
- observations
- figures
- figure prefix databases
- FBK dry-run
- import workflows

Survey COM behavior must be isolated so the rest of the platform is not tied to COM quirks.

### 12. How should Developer Mode work?

Developer Mode should be a separate module, disabled by default.

It should not register `civil3d_execute_csharp` unless explicitly enabled by configuration and visible status.

When enabled, it should expose:

- `civil3d_query_csharp`
- `civil3d_execute_csharp`
- `developer_mode_skills`
- possibly `promote_script_to_tool` later

### 13. How should the Roslyn sandbox be designed?

Do not reuse regex-only sandboxing as the final design.

Use:

- assembly allowlist
- namespace allowlist/blocklist
- Roslyn syntax tree analyzer
- Roslyn semantic analyzer where possible
- explicit global object surface
- filesystem path allowlist
- no shell/process/network/registry/PInvoke/reflection by default
- per-script timeout and cancellation
- code hash logging

### 14. How should dynamic C# destructive detection work?

Developer Mode risk analyzer should detect:

- `.Erase(`
- delete/remove/purge methods
- `OpenMode.ForWrite` over broad enumerations
- mass collection edits
- `Database.Purge`
- `SendStringToExecute`
- COM invocation
- file writes outside allowed paths
- reflection or dynamic assembly loading
- calls into unsafe APIs

If destructive risk is detected, require explicit confirmation with preview. If the analyzer cannot classify the code, deny by default or require elevated developer override.

### 15. How should Civil 3D 2026 compatibility be handled?

Target Civil 3D 2026 first, but avoid hardcoded install paths.

Recommended:

- configurable `AutodeskRoot`
- probe common install paths
- allow environment variable override
- do not commit Autodesk DLLs
- make bridge build fail clearly when required assemblies are missing
- document 2025 compatibility only after local API verification

### 16. How should packaging work?

Use Autodesk Application Package format:

```text
Civil3D.HybridMcp.bundle
```

Development load:

- support `NETLOAD` for debug

Production load:

- install to `%APPDATA%\Autodesk\ApplicationPlugins`
- `LoadOnAutoCADStartup=True`

### 17. How should logging work?

Each tool execution log entry should include:

- timestamp
- correlation ID
- session ID
- instance ID
- document ID/path
- tool
- operation class
- risk
- arguments summary
- affected count
- duration
- result status
- error code
- rollback status
- confirmation token ID when applicable
- Developer Mode code hash and sandbox classification when applicable

Logs should avoid sensitive full payloads unless debug/audit mode explicitly enables them.

### 18. How should tests be layered?

Unit tests:

- protocol serialization
- schema generation/validation
- permissions
- confirmation hashing/token matching
- angle/bearing/azimuth math
- FBK parsing
- sandbox analyzer

Integration tests:

- MCP server to fake bridge
- named pipe reconnect
- manifest loading
- timeout/cancel/progress
- multiple fake instances
- transaction wrapper behavior with mocked Autodesk adapters

Live smoke tests:

- open Civil 3D test DWG
- ping/status
- list documents/layers/alignments/surfaces/COGO
- create temporary object
- rollback/delete temporary object in lab drawing only
- detect/dismiss safe test modal

### 19. What should Phase 1 implement first?

Phase 1 should implement the core skeleton only:

1. solution/project structure
2. shared protocol contracts
3. named-pipe host/client
4. endpoint registry
5. bridge startup/autoload scaffold
6. dynamic manifest handshake
7. `civil3d_ping`
8. `civil3d_status`
9. `drawing_info`
10. `document_list`
11. initial logging
12. fake bridge/server tests

Do not implement Survey or Developer Mode in the first slice unless the user explicitly reprioritizes.

### 20. What decisions remain open?

Open decisions:

- exact project and package names
- final endpoint registry folder name
- whether server is TypeScript, C#, or another runtime
- exact confirmation UX supported by the active MCP client
- exact bridge command names for dev/debug
- minimum supported Civil 3D version besides 2026
- whether Survey COM interop references are early-bound, late-bound, or both
- whether to include a `NOTICE.md` immediately for conceptual attribution
- how to distribute installer/bundle during development

## Initial Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Running commands in the wrong DWG | High | Instance/document IDs, ambiguity rejection, active document checks. |
| Incomplete confirmation model | High | Confirmation token verified by bridge and bound to args hash. |
| Survey COM operations lack rollback | High | Dry-run first, explicit non-rollback warning, lab DWG tests. |
| Dynamic C# escapes sandbox | High | Developer Mode disabled by default, allowlists, Roslyn analyzers, deny unknowns. |
| Modal dialog blocks Civil 3D | High | Core dialog monitor and bypass recovery tools. |
| Hardcoded Autodesk paths | Medium | Configuration and probing. |
| Fixed TCP/HTTP port conflicts | Medium | Named pipe endpoint per process. |
| Autodesk API version differences | Medium | Adapter interfaces, feature detection, versioned capability manifests. |

## Final Phase 0 Architecture Position

The hybrid platform should be:

- robust like hjlrosales
- extensible like barbosaihan, but only inside controlled Developer Mode
- Survey-capable like KevinGriffin, but modular and safer

No reference repository should be merged directly. The next step is Phase 1 core scaffolding after the ADRs are accepted.

