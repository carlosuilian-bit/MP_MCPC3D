# Feature Matrix

Date: 2026-09-24

Scope: Phase 0 comparison, compatibilized 2026-09-24 with the phases shipped through 6B and with the geometric/exported and data-carrier decisions of 2026-09-19/20. Status vocabulary normalized: `Done` merged into `Implemented`.

Legend:

- `Yes`: present or mostly present in the reference.
- `Partial`: present but incomplete, experimental, or not production-safe.
- `No`: not found in the inspected repository.
- `Reference only`: use the idea, not direct code in Phase 1.
- `Reimplement`: build a new implementation for the hybrid platform.

| Feature | Reference implementation | Reuse? | Reimplement? | Priority | Target module | Status | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Separated MCP server and Civil 3D bridge | hjlrosales: strong TypeScript server plus .NET bridge; KevinGriffin: Python server plus .NET bridge; barbosaihan: TS server plus .NET plugin | Reference only | Yes | P0 | Core | Implemented | Implemented as new C# projects, not copied from references. |
| Official MCP STDIO surface | References use TypeScript/Python MCP SDKs; this project uses official C# SDK | No direct code | Yes | P0 | Server | Implemented | `Civil3D.HybridMcp.Server.exe mcp` exposes bridge tools over STDIO with official MCP C# SDK handlers. |
| Dynamic tool discovery | hjlrosales: bridge-generated manifests; others static | Reference only | Yes | P0 | Core | Implemented | Server receives bridge manifest during handshake and MCP `tools/list` maps it dynamically. |
| Typed tool schemas | hjlrosales: DTOs plus generated JSON schema; barbosaihan: zod for meta-tools only; KevinGriffin: Python signatures only | Reference only | Yes | P0 | Core | Partial | Input schemas are typed. Output naming is inconsistent: defect D2, properties without `[JsonPropertyName]` serialize camelCase under `JsonSerializerDefaults.Web` while attributed ones are snake_case; `DocumentDescriptor` is the most visible case. |
| Structured result envelope | hjlrosales: success/error/data envelope; KevinGriffin: success/result/error JSON; barbosaihan: JSON-RPC-like | Reference only | Yes | P0 | Core | Implemented | Protocol has `ToolResponse`, `ErrorEnvelope`, `SuccessEnvelope`, and MCP structured content mapping. |
| Named pipe IPC | hjlrosales: Windows named pipe with CurrentUserOnly | Reference only | Yes | P0 | Core | Implemented | SDK implements JSON-RPC over current-user named pipes. |
| HTTP localhost IPC | KevinGriffin: `HttpListener` on port 8270 | No | No | P2 | Core | Rejected for core | Useful for experimentation, not preferred production IPC. |
| TCP loopback IPC | barbosaihan: fixed port 8080 | No | No | P2 | Core | Rejected for core | Multiple-instance and ACL issues. |
| Endpoint registry | hjlrosales: `%LOCALAPPDATA%` descriptors by product/PID | Reference only | Yes | P0 | Core | Implemented | `%LOCALAPPDATA%\Civil3DHybridMcp\endpoints`, PID/start/heartbeat validation. |
| Multiple Civil 3D instances | hjlrosales: strongest support; others fixed-port | Reference only | Yes | P0 | Core | Implemented | Integration tests cover multi-endpoint discovery and ambiguous rejection. |
| Document list | Not complete in references; hjlrosales has active drawing info | No | Yes | P0 | Core | Implemented | `document_list` exists in bridge/server/fake bridge. |
| Document activate | KevinGriffin bridge has `switch_document` action; not exposed as main MCP tool | Reference only | Yes | P1 | Core | Needed | Must avoid silent wrong-DWG execution. |
| Ping/status | All have ping/status variations | Reference only | Yes | P0 | Diagnostics | Implemented | `civil3d_ping` and `civil3d_status` implemented. |
| Modal dialog detection | KevinGriffin: Win32 `DialogProbe`, list/dismiss/cancel | Reference only | Yes | P0 | Diagnostics | Implemented | Closed end to end and live validated 2026-09-17: 1C detect (`list_dialogs`), 1D read message and buttons, 1E operator decision store (default or one-time), 1F `dismiss_dialog` via BM_CLICK to the button handle with close confirmation. See `docs/PHASE_1F_LIVE_TEST.md`. |
| Cancel active command | KevinGriffin: ESC queue bypass; hjlrosales: protocol cancel | Reference only | Yes | P0 | Diagnostics | Not Started | Still deferred. The dialog side is now covered by Phases 1D through 1F; cancelling a running command is a separate problem with no decision-registry equivalent yet. |
| Logging/audit | hjlrosales: Serilog/pino/events; KevinGriffin: trace logs; barbosaihan: basic logging | Reference only | Yes | P0 | Diagnostics | Implemented | JSONL request logging, MCP correlation propagation, and bridge bootstrap logging exist. |
| Timeout model | hjlrosales: per-tool timeout/cancel; KevinGriffin: HTTP 30s and 5min bridge wait; barbosaihan: 120s script timeout | Reference only | Yes | P0 | Core | Implemented | Pipe client and integration tests cover delayed response timeout. |
| Progress notifications | hjlrosales: protocol support | Reference only | Yes | P1 | Core | Not Started | Needed for rebuild/import/batch. |
| Transaction pipeline | hjlrosales: central provider/pipeline; others per-handler | Reference only | Yes | P0 | Core/AutoCAD | Implemented | Two pipelines (AutoCAD, Civil) with lock, transaction, commit/abort; Phase 3D rollback regression coverage; used live by every write tool. |
| Rollback on write failure | hjlrosales: strongest; barbosaihan: transaction disposal; KevinGriffin: uneven | Reference only | Yes | P0 | Core/AutoCAD | Partial | Offline coverage complete (Phase 3D). Live: the abort path ran in Phase 6B, but that batch failed before writing, so undoing a real write is not yet proven live. `AGENTS.md` section 38 criterion 12. |
| Document Lock | hjlrosales and barbosaihan central; KevinGriffin around dispatch | Reference only | Yes | P0 | Core/AutoCAD | Implemented | `Document.LockDocument()` in both write pipelines; exercised live by every write tool. |
| Confirmation model | hjlrosales: architecture present but inspected code gap; others absent | Reference only | Yes | P0 | Permissions | Implemented | HMAC-signed confirmation tokens bound to canonical argument hash; preview, affected count and token returned in error details. Live validated in 3E and 6B. Defect D3: the confirmed-write plumbing is duplicated in two places; extract before a third tool. |
| READ/WRITE/DESTRUCTIVE classification | hjlrosales has permission/risk; requested hybrid has explicit classes | Reference only | Yes | P0 | Permissions | Implemented | Present in protocol and tool manifests. |
| Layer list/get/create/update/current | KevinGriffin: list/create/current; hjlrosales: not observed | Reference only | Yes | P1 | AutoCAD | Implemented | All five live validated (Phases 2, 3A, 3B). |
| Object get/properties/select/delete | hjlrosales search only; KevinGriffin entity handles/fallbacks | Reference only | Yes | P1 | AutoCAD | Partial | `object_get` and `object_properties` live validated (Phase 2G, inside `docs/PHASE_1F_LIVE_TEST.md`). `object_select` and `object_delete` not started. |
| Block list/reference/attributes/replace | Not observed as strong typed set | No | Yes | P2 | AutoCAD | Partial | Reads implemented and live validated in Phase 2J (2026-09-24). `block_set_attribute` and `block_replace` not started. |
| Property Sets | Not observed | No | Yes | P2 | Civil3D | Implemented | Definitions list, list, get, find (sweep of every BlockTableRecord), set with confirmation, atomic batch write, numeric probe. All live validated. Defect D4: the batch write is named `roadway_propertyset_write` but has no roadway logic; should be `propertyset_set_batch` in Civil. Export: measured not to survive the operator export; see `docs/DATA_CARRIERS.md`. |
| Drawing info | hjlrosales and KevinGriffin | Reference only | Yes | P0 | Core | Implemented | `drawing_info` implemented as READ. |
| Save drawing | hjlrosales has `save_drawing`; KevinGriffin queues/commands in places | Reference only | Yes | P2 | Core | Needed | Treat as WRITE with explicit policy. |
| COGO list/get | hjlrosales and KevinGriffin | Reference only | Yes | P1 | Civil3D/Survey | Implemented | Drawing COGO `cogo_list` and `cogo_get` are READ-only and live validated. Separate Survey control/non-control points remain future work. |
| COGO create/update | KevinGriffin has create/edit; barbosaihan templates | Reference only | Yes | P1 | Civil3D/Survey | Needed | Use transaction and schema validation. |
| COGO delete | Not observed as safe typed tool | No | Yes | P2 | Civil3D/Survey | Needed | DESTRUCTIVE with preview. |
| Point styles and label styles | KevinGriffin has broad style create/list; hjlrosales has generic style list/get | Reference only | Yes | P2 | Civil3D/Survey | Needed | Style writes need validation. |
| Bearing/azimuth geometry | KevinGriffin has line by bearing/azimuth | Reference only | Yes | P1 | Survey | Implemented | Phase 4A implements `survey_bearing_to_azimuth`, `survey_azimuth_to_bearing`, `survey_distance`, and `survey_point_by_azimuth` as pure READ/Low tools. |
| FBK dry-run parser | KevinGriffin has parser-driven FBK dry-run/import concepts | Reference only | Yes | P1 | Survey | Implemented | Phase 4B implements `survey_fbk_dry_run` for inline FBK content with structured records, setup previews, observation counts, and warnings. It does not read files, import to Survey DB, or use COM. |
| Survey Database readiness/status | KevinGriffin has Survey COM discovery and working-folder tools | Reference only | Yes | P1 | Survey | Implemented | Phase 4C implements `survey_database_status` as a READ/Low readiness probe. The pure default reports `not_configured`; the Autodesk bridge performs a filesystem-only environment probe and live validated `interop_available` without initializing COM or opening Survey Databases. |
| Traverse calculations | Not fully observed | Reference only | Yes | P2 | Survey | Needed | Build as pure functions first. |
| Surface list/get | hjlrosales has list/get; barbosaihan templates; KevinGriffin surface creation/volume | Reference only | Yes | P1 | Civil3D | Implemented | `surface_list` and `surface_get` are READ-only and live validated. |
| Surface elevation at XY | barbosaihan skill template | Reference only | Yes | P1 | Civil3D | Implemented | `surface_elevation_at_xy` is READ-only and live validated, including structured outside-surface failure. |
| Surface TIN audit and copy-first correction workflow | New hybrid plan from live `TN RTK` analysis | N/A | Yes | P2 | Civil3D | Planned | Registered 2026-09-30 only, not started. See `docs/superpowers/specs/2026-09-30-surface-tin-correction-tools-design.md` and `docs/superpowers/plans/2026-09-30-surface-tin-correction-tools.md`. Future tools must protect the source surface and operate on an explicit copy such as `TN RTK_CORRIGIDA`. |
| Surface create | KevinGriffin and barbosaihan templates | Reference only | Yes | P2 | Civil3D | Needed | WRITE with duplicate-name checks. |
| Surface add breakline | Desired, not strongly observed as typed production tool | Reference only | Yes | P2 | Civil3D/Survey | Needed | May use Survey figures later. |
| Surface rebuild | KevinGriffin calls rebuild in some handlers; no typed standalone | Reference only | Yes | P2 | Civil3D | Needed | Long-running candidate. |
| Alignments list/get | hjlrosales strong; barbosaihan templates | Reference only | Yes | P1 | Civil3D | Implemented | `alignment_list` and `alignment_get` are READ-only and live validated. |
| Alignment station/offset | barbosaihan skill template | Reference only | Yes | P1 | Civil3D/Roadway | Implemented | `alignment_station_offset` is READ-only and live validated. |
| Alignment point location | Not strongly observed as typed tool | Reference only | Yes | P1 | Civil3D/Roadway | Implemented | `alignment_point_location` is READ-only and live validated as station/offset companion. |
| Alignment create from polyline | Desired, not audited as complete typed tool | No | Yes | P2 | Civil3D | Needed | WRITE with preview. |
| Profiles list/get/elevation | hjlrosales list/get; profile elevation desired not observed | Reference only | Yes | P2 | Civil3D | Implemented | Phase 2D implements `profile_list`, `profile_get`, and `profile_elevation_at_station` as READ/Low tools over the Civil 3D .NET API. Profiles resolve through their parent alignment, ambiguous names return `PROFILE_AMBIGUOUS`, and elevation plus grade are read at a station. Live validated on 59 profiles across 32 alignments. Profile writes remain future work. |
| Corridors list/get/rebuild | hjlrosales list/get/report; rebuild desired not observed | Reference only | Yes | P2 | Civil3D | Needed | Rebuild needs progress/cancel. |
| Corridors list/get | hjlrosales | Reference only | Yes | P2 | Civil3D | Implemented | Live validated (Phase 2E). Rebuild tracked on the row above. |
| Pipe networks | hjlrosales strongest | Reference only | Yes | P3 | Civil3D | Later | Useful but below Survey/roadway MVP unless user prioritizes. |
| Survey working folder | KevinGriffin | Reference only | Yes | P1 | Survey | Implemented (read-only) | Phase 4D implements `survey_working_folder_get` through a typed Survey COM gateway. It requires an active document, reads only the current working folder, and does not open Survey Databases or set working folders. Live validation confirmed the 25-tool catalog and structured `DocumentNotFound` behavior when the bridge reported no active document. |
| Survey projects | KevinGriffin | Reference only | Yes | P1 | Survey | Partial | Phase 4E-B implements `survey_project_list` through the same typed Survey COM gateway. It requires an active document, lists project metadata only, and does not open Survey Databases, create projects, or set working folders. Project detail and mutation tools remain future work. |
| Survey networks | KevinGriffin | Reference only | Yes | P1 | Survey | Partial | Phase 4E-C implements `survey_network_list` through the typed Survey COM gateway. It requires an active document, lists network metadata only for one or all projects, and never calls `IAeccSurveyNetworks.Create` or `Remove`. Network detail, create, update, and summary tools remain future work. Live network item enumeration is still unproven because the validation host had no Survey projects. |
| Survey control/non-control points | KevinGriffin | Reference only | Yes | P1 | Survey | Needed | Distinct from drawing COGO. |
| Survey setups/observations | KevinGriffin | Reference only | Yes | P1 | Survey | Needed | Read-only first. |
| Survey figures | KevinGriffin bridge-only functions | Reference only | Yes | P2 | Survey | Needed | Expose only safe typed subset. |
| Figure Prefix Database | KevinGriffin bridge-only functions | Reference only | Yes | P2 | Survey | Needed | WRITE operations need explicit validation. |
| FBK parse dry-run | KevinGriffin | Reference only | Yes | P1 | Survey | Implemented | Covered by `survey_fbk_dry_run`; pure parser with unit and fake-bridge MCP tests. |
| FBK import | KevinGriffin command and direct COM approaches | Reference only | Yes | P2 | Survey | Needed | Direct COM preferred; command fallback must be documented. |
| GNSS raw to RINEX conversion | Not present in any reference | No | Yes | P3 | Survey | Knowledge only | Operator has a proven JPS to RINEX 2.11 pipeline using RTKLIB `convbin`. `.HCN` does not convert and is handled upstream by CHCNAV vendor software. Nothing implemented; see `docs/SURVEY_IBGE_PPP.md`. |
| IBGE-PPP corrected base | Not present in any reference | No | Yes | P3 | Survey | Knowledge only | Post-processes an RTK base observation so a survey is referenced to a corrected coordinate. Would be this platform's first outbound network call and its first unrollbackable external action, so it needs an explicit decision, not just a tool. Antenna height must be vertical to the ARP, never slant. See `docs/SURVEY_IBGE_PPP.md`. |
| Developer Mode dynamic C# | barbosaihan | Reference only | Yes | P3 | DeveloperMode | Later | Disabled by default. |
| Developer Mode skills | barbosaihan | Reference only | Yes | P3 | DeveloperMode | Later | Promote successful scripts into typed tools. |
| Roslyn sandbox | barbosaihan partial | No | Yes | P3 | DeveloperMode/Security | Needed | Must be redesigned with allowlists and analyzers. |
| C# destructive API analyzer | Not present | No | Yes | P3 | DeveloperMode/Security | Needed | Detect `.Erase`, purge, delete, mass writes, `SendStringToExecute`, COM escape hatches. |
| Bundle autoload | hjlrosales and KevinGriffin have bundle metadata | Reference only | Yes | P1 | Packaging | Implemented | Clean-start validation launched Civil 3D 2026 without a `/b` loader script and received a new bridge endpoint, `ping`, `status`, and `document_list`. Continue monitoring during packaging changes. |
| Offline bundle diagnostics | New hybrid project implementation | N/A | Yes | P1 | Packaging | Implemented | `scripts/check-bundle-install.ps1` validates bundle files, manifest autoload settings, Civil 3D 2026 runtime requirements, and absence of packaged Autodesk managed assemblies without touching Civil 3D. |
| Civil 3D 2026 SDK detection | KevinGriffin hardcoded; hjlrosales configurable default 2025 | Reference only | Yes | P0 | Build/Packaging | Implemented | Bridge build supports env/MSBuild overrides and clear missing-reference errors. |
| Roadway high-level tools | Not present as dedicated module | No | Yes | P4 | Roadway | Partial | 6A `roadway_station_offset_batch` and 6B batch property set write, both live validated 2026-09-19. Rescoped 2026-09-24 from 12 to 9 tools: `roadway_create_km_marker` PAUSED by operator decision; the two property set tools move to Civil (D4); `roadway_get_station` redefined for the exported moment. See `docs/ROADMAP_PROGRESS.md`. |
| Block list/reference/attributes (reads) | Not observed as strong typed set | No | Yes | **P1** | AutoCAD | Implemented | Raised from P2 on 2026-09-24. Largest MVP read gap (0 of 3). Blocks are plain AutoCAD entities, so these reads work in both the geometric and the exported moment. |
| Object-layer map read (tracking anchor) | Not observed | No | Yes | P1 | Civil3D | Needed | `CivilDocument.Settings -> DrawingSettings -> ObjectLayerSettings -> GetObjectLayerSetting(SettingsObjectLayerType)`, API-confirmed. The layer convention bridging geometric and exported drawings is read from the template, never typed. See `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`. |
| Project moment detector (geometric/exported) | Not observed | No | Yes | P1 | Civil3D | Needed | Defect D1: on an exported drawing 15 Civil reads return a valid empty result indistinguishable from a genuinely empty drawing. |
| Object Data read/write | Not observed | No | Yes | P2 | Map3D | Needed | `Autodesk.Gis.Map.ObjectData` in `Map\ManagedMapApi.dll`, API-confirmed. Required because MAPEXPORT does not offer Property Set data for SHP attributes (secondary source). KMZ mechanism still to confirm. See `docs/DATA_CARRIERS.md`. |
| XRecord / Extension Dictionary for platform metadata | Not observed | No | Yes | P3 | Core | Needed | Preferred over XData for any per-object platform state: XData is capped at 16 KB per entity shared across all applications. |
| Data carrier export survival test | Not observed | No | Yes | P1 | Civil3D | Needed | Phase 3F. One export round measuring Property Set (measured dead), Object Data, XData and XRecord on the same hatch. |
| Exported-moment station read | Not observed | No | Yes | P2 | Roadway | Needed | `roadway_get_station` redefined: station from the former axis polyline or its label, reading the role-to-layer map, with the source of the number declared in the response. |

## MVP Priority View

### P0 - Phase 1 / 1.5 Core

- project skeleton and module boundaries
- shared protocol contracts
- official MCP STDIO surface
- named pipe IPC
- endpoint registry
- bridge autoload package scaffold
- ping/status
- document identity model
- dynamic tool manifest discovery
- structured logging
- command/transaction foundation
- modal dialog status/list foundation
- permission and confirmation model design
- fake bridge MCP end-to-end validation

### P1 - First Read/Write Surface

- drawing info
- document list/activate
- layer list/get/create/current/update
- layer delete policy design
- alignment list/get/station-offset/point-location
- surface list/get/elevation at XY
- COGO list/get
- Survey bearing/azimuth/distance geometry
- COGO create/update
- Survey project/network read path
- FBK dry-run parser

### P2 - Safe Writes and Survey Expansion

- object delete with confirmation
- blocks and attributes
- property sets
- profiles and corridors
- surface create/breaklines/rebuild
- Survey control/non-control/setup/observation operations
- figure prefix database
- Survey figures and breakline workflows

### P3 - Developer Mode

- Roslyn dynamic C# host
- sandbox allowlists
- syntax/semantic risk analyzer
- code hash/audit logging
- explicit enablement
- promotion workflow from script to typed tool

### P4 - Roadway Layer

- roadway station/offset batches
- KM marker automation
- property-set read/write wrappers
- KML/export/layout workflows
- interference/access/visibility checks
