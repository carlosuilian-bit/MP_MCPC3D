# Roadmap Progress

Date: 2026-09-24, updated 2026-09-30

Scope: compatibilization review. Reconciles the roadmap with what ships (49 tools, 293 passing tests, measured 2026-09-24 after Phase 2J), with the `AGENTS.md` MVP list (section 15) and success criteria (section 38), and with the scope decisions of 2026-09-19/20: the geometric/exported project moments, the layer-convention tracking anchor, the data-carrier analysis, Phase 3F, the D1-D5 defects, and the pause of `roadway_create_km_marker`.

Update 2026-09-30: registered, but did not start, the Surface/TIN correction tool plan prompted by the live `TN RTK` analysis. This is planning/backlog only; no implementation, bundle change, or drawing mutation is implied by this roadmap update.

The pre-2026-09-24 phase notes remain below under "History".

## Three numbers, three questions

The previous headline, **87% "platform progress"**, did not follow from the phase table printed under it. That table, weighted with the explicit weights below, gives **63%**; with equal weights, **58%**. No reasonable weighting reaches 87%.

The 87% was nonetheless a real measurement of something else. It matches **exactly** the core success criteria of `AGENTS.md` section 38: 13 met, 2 partial, 1 missing = 14/16 = 87.5%. It was the right number under the wrong label.

So the project now reports three numbers, each answering a different question:

| Question | Basis | Result |
| --- | --- | ---: |
| Is the **core** successful as `AGENTS.md` defines it? | Section 38, 16 criteria; partial = half | **87%** |
| How much of the **MVP tool list** exists? | Sections 14 + 15, 47 named tools | **72%** (34/47) |
| How far along is the **whole platform**? | Phases 0-7, weighted, after compatibilization | **55%** |

None of these is wrong. Reporting the first as if it were the third is what was wrong.

Section 38 detail: criteria 1-11, 13 and 16 are met. **12** (rollback on error) is partial: the abort path ran live in Phase 6B, but that batch failed before writing anything, so undoing a real write has not been proven live. **15** (Survey flow) is partial: FBK dry-run and Survey reads exist, import does not. **14** (dynamic C#) is missing.

MVP detail, tools named in sections 14-15 that do not exist: `document_activate`, `cancel_active_command`, `object_select`, `object_delete`, `block_set_attribute`, `block_replace`, `cogo_create`, `cogo_update`, `surface_create`, `surface_add_breakline`, `surface_rebuild`, `alignment_create_from_polyline`, `corridor_rebuild`.

## How phase percentages are computed

Stated so the next revision can reproduce them instead of re-estimating.

- **Where a phase is a list of tools**, the percentage is a count: delivered over scoped.
- **Where a phase is infrastructure**, it is a judgment, and the row says so.
- **Phase 3 is both**: half infrastructure, half MVP write tools.
- **Weights** reflect remaining effort and risk, not calendar: Phase 0 = 5, Phase 1 = 15, Phase 2 = 20, Phase 3 = 20, Phases 4-7 = 10 each. Total 100.
- Treat every figure as plus or minus 5. Integers are shown so the arithmetic can be checked, not to imply precision.

## Phase Progress

| Phase | Weight | Declared | **Revised** | Basis | Main remaining work |
| --- | ---: | ---: | ---: | --- | --- |
| 0 - Audit | 5 | 100% | **95%** | Judgment | Three decisions of 2026-09-19/20 have no ADR, which section 35 requires: the two project moments with an explicit moment and declared provenance; the layer map read from `ObjectLayerSettings`; the data-carrier roles and governance rule. `FEATURE_MATRIX.md` was nine phases stale and is corrected in this pass. |
| 1 - Core | 15 | 99% | **92%** | Judgment | `document_activate`; `cancel_active_command`; **defect D2**, response naming split, which lives in Core contracts: `DocumentDescriptor`, embedded in every response, emits camelCase beside snake_case module fields. The declared row also listed `dismiss_dialog` as missing; it shipped and was live validated in Phase 1F. |
| 2 - Read-only | 20 | 97% | **88%** | Count, 23/26 | MVP Civil reads: 23 of 24 exist. The three block reads shipped in Phase 2J (2026-09-24), live validated in both project moments. Missing: `object_select`, plus the two reads added by compatibilization: the object-layer map (tracking anchor) and the moment detector (defect D1). |
| 3 - Safe Write | 20 | 70% | **52%** | Half infrastructure (6/8), half tools (4/14) | Infrastructure done: lock, transaction, classification, confirmation, rollback coverage, preview. Open: **defect D3**, extract the confirmed-write pipeline duplicated in two places; **Phase 3F**, carrier export survival test. MVP writes: 4 of 14 exist (three layer writes, `propertyset_set`). |
| 4 - Survey | 10 | 50% | **50%** | Unchanged, parked | No new evidence. Parked since 2026-09-13: no Survey project exists on the validation machine. IBGE-PPP knowledge is recorded, not implemented. |
| 5 - Developer Mode | 10 | 5% | **5%** | Unchanged | Roslyn host, sandbox, risk analyzer, promotion workflow. `docs/DEVELOPER_MODE.md`, mandatory under section 32, does not exist. |
| 6 - Roadway | 10 | 28% | **11%** | Count, 1/9 | See "Phase 6 rescoped" below. Delivered: `roadway_station_offset_batch`. |
| 7 - Hardening | 10 | 17% | **20%** | Judgment | Stress, cancellation and progress, multi-instance live tests, installer, recovery, `cancel_active_command`. Safe modal dismissal, listed as remaining, shipped in Phase 1F and is removed from this row. |
| **Weighted** | **100** | **63%** | **55%** | | |

The declared column is weighted with the same weights, not the old 87% headline, so the two columns compare like with like.

### Phase 6 rescoped

The `AGENTS.md` section 16 list had 12 tools. After the decisions of 2026-09-19/20 it has 9:

- **Removed, paused**: `roadway_create_km_marker`, by operator decision. Not estimated until the operator raises it again.
- **Removed, moved to Civil**: `roadway_propertyset_read` and `roadway_propertyset_write`. They contain no roadway logic (defect D4). The shipped batch write moves with its credit; it is not lost, it is reclassified.
- **Changed**: `roadway_get_station` now means the **exported** moment (station from the former axis polyline or its label). In the geometric moment it would duplicate `alignment_station_offset`.
- **Remaining**: `roadway_station_offset_batch` (done), `roadway_extract_alignment_data`, `roadway_get_station` (exported), `roadway_visibility_check`, `roadway_access_geometry`, `roadway_interference_check`, `roadway_export_kml` (now KMZ, via Object Data), `roadway_generate_layouts`, `roadway_generate_viewports`.

The drop from 28% to 11% is mostly reclassification, not lost work.

## Cross-cutting track: geometric and exported moments

Not a new phase number. Its items sit inside Phases 2, 3 and 6 and are counted there; this view exists so the track can be followed as one thing. **Knowledge and design: done. Implementation: 0 of 8.**

| Item | Counted in | State |
| --- | --- | --- |
| Read the role-to-layer map from `ObjectLayerSettings` | Phase 2 | Designed, API-confirmed |
| Moment detector (defect D1) | Phase 2 | Designed |
| Phase 3F, survival test for Property Set, Object Data, XData, XRecord | Phase 3 | Planned |
| Verify whether hatch area is an automatic property | Phase 3F | Open question, first in `docs/DATA_CARRIERS.md` |
| Object Data read/write adapter | Phase 6 prerequisite | API-confirmed, no code |
| Confirm the KMZ mechanism by watching the operator export | Phase 6 prerequisite | MAPEXPORT established for SHP/SDF; KMZ open |
| `roadway_get_station` for the exported moment | Phase 6 | Designed |
| Property Set to Object Data one-way sync under a declared source | Phase 6 | Direction only |

## Registered implementation plan: Surface/TIN correction tools

Status: **registered only, not started**.

On 2026-09-30 the operator requested that the controlled surface-correction workflow be placed in the implementation plans, after the live read-only analysis of `TN RTK` showed long unsupported TIN edges and skinny triangles while the current bridge exposed only surface read tools.

The plan is intentionally **typed-tool first** and does not use Developer Mode as a shortcut. Developer Mode remains unimplemented and disabled.

Documents:

- Design spec: `docs/superpowers/specs/2026-09-30-surface-tin-correction-tools-design.md`
- Implementation plan: `docs/superpowers/plans/2026-09-30-surface-tin-correction-tools.md`

Planned tools include `surface_audit_tin`, `surface_copy`, `surface_rebuild`, `surface_set_build_options`, `surface_boundary_preview`, `surface_add_outer_boundary`, `surface_breakline_candidates`, `surface_add_standard_breakline`, `surface_tin_edit_preview`, `surface_swap_edge`, and `surface_delete_tin_line`.

Safety rule for that future work: the original production surface, specifically `TN RTK` / handle `20E98B` in the Pratao drawing, must not be edited directly. Corrections target an explicit copied surface such as `TN RTK_CORRIGIDA`, with protected-source rejection, preview, confirmation where applicable, and live validation only on copied test DWGs.

## Adjustments made to the plan in this pass

1. Headline replaced by three labelled numbers; weighting method published.
2. Phase 2 rescored against its own MVP scope: the declared 97% ignored the three block reads and `object_select`, although its own "remaining work" named them.
3. Phase 3 rescored to count MVP write tools, not only infrastructure.
4. Phase 6 rescoped from 12 to 9 items.
5. Stale statements removed: Phase 2G "not live validated", `dismiss_dialog` "unimplemented", Phase 6 crediting only 6A, modal dismissal as hardening work.
6. Geometric/exported track mapped onto existing phases instead of spawning a new one.
7. Defects D1-D5 assigned to phases: D1 to 2, D2 to 1, D3 to 3, D4 to 6, D5 fixed.
8. Blocks raised in priority. They are the largest MVP read gap, and **blocks are plain AutoCAD entities, so block tools work in both project moments**, unlike the 15 Civil reads that go silent on an exported drawing.

## Immediate Next Steps

Ordered by value per effort. Nothing here is started.

1. ~~**Block reads**~~ **Done 2026-09-24, Phase 2J**, live validated on both drawings of the reference pair. See `docs/PHASE_2J_LIVE_TEST.md`.
2. **Object-layer map read.** READ/Low, API-confirmed, and it is the tracking anchor the operator asked to standardise.
3. **Moment detector (D1).** Cheap; removes the ambiguity from 15 tools at once.
4. **D4 rename, then D3 extraction.** Rename while only one tool carries the wrong name; extract the confirmed-write pipeline before a third write needs it.
5. **Phase 3F.** One export round, four carriers. Can run in parallel with any of the above.
6. **ADRs for the three decisions of 2026-09-19/20.** Section 35 compliance.
7. **D2 naming pass.** Largest and least urgent, and it grows with every response shape added before it.

Paused: `roadway_create_km_marker`. Parked: Survey COM beyond listing.

**Runtime note, 2026-09-24.** A Civil 3D 2026 update moved the Autodesk host to .NET 10. The three Autodesk-facing projects and their tests now target `net10.0-windows`; everything else stays on .NET 8. The bundle requires the updated 2026 install and no longer supports 2025. See `docs/decisions/ADR-0009-net10-autodesk-host.md`.

---

# History (pre-2026-09-24)

## Offline Work Started

The current offline step is the packaging/autoload diagnostic track:

- `scripts/check-bundle-install.ps1` validates the installed or supplied `.bundle` directory from the filesystem only.
- `PackagingDiagnosticsTests` covers successful bundle validation and rejection of packaged Autodesk managed assemblies.
- The installed bundle at `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle` passed this diagnostic on 2026-09-11.

## Phase 3 Safe Write Work

- Clean-start Civil 3D autoload validation passed without a `/b` loader script.
- `layer_set_current` is implemented as a WRITE/Low tool.
- The new tool uses document target validation, layer locator validation, `Document.LockDocument()`, an AutoCAD transaction, and rollback-before-commit semantics.
- `layer_update` is implemented as a WRITE/Low tool for bounded layer property changes.
- `layer_update` validates locator, at least one update field, layer-name syntax, color range, duplicate rename targets, and current-layer on/frozen safety.
- `propertyset_numeric_test_create` is implemented as a WRITE/Low AEC property-set numeric probe. It creates a prefixed test definition, a temporary `DBPoint`, attaches the set, writes one `Real` value, and returns the same structured property-set shape used by read tools.
- Phase 3B unit and fake bridge MCP tests expected an 18-tool catalog; Phase 4A expected a 22-tool catalog; Phase 4B expected a 23-tool catalog; Phase 4C starts at 24 tools; Phase 4D starts at 25 tools; Phase 4E-B starts at 26 tools; Phase 4E-C starts at 27 tools; Phase 3C takes the current development catalog to 37 tools.
- The installed bundle was live validated against Civil 3D 2026 on a temporary copy of the user-confirmed test DWG.
- Live `layer_set_current` switched from `_Deletar linha` to `0`, verified the current layer through `layer_list`, then restored `_Deletar linha`.
- The Phase 3B 18-tool bundle was installed and live validated against Civil 3D 2026 on a temporary copy of the user-confirmed test DWG.
- Live `layer_update` renamed `MCP_LU_OK_114201` to `MCP_LU_OK_114201_UPDATED`, changed color to ACI 2, locked the layer, updated the description, and verified the result with `layer_get`.
- Phase 3B live validation exposed two modal blockers, `References - Not Found Files` and `Drawing Recovery`, reinforcing modal monitor priority.
- The Phase 3C 37-tool Release bundle was installed and live validated against Civil 3D 2026 on a temporary copy of the user-confirmed test DWG.
- Live `propertyset_numeric_test_create` created `MCP_NUMERIC_TEST_LIVE_111759`, attached it to object handle `16FD0D`, wrote `987.654321`, and verified the same `numeric_value` through `propertyset_get`.
- Phase 3C live validation initially hit the `References - Not Found Files` modal. The dialog was read first, then dismissed with `Ignore unresolved reference files` for the temporary test DWG copy before retrying the tool.

## Phase 4 Survey Work

- `Civil3D.HybridMcp.Tools.Survey` is implemented as a separate pure-logic module without Autodesk or COM references.
- The bridge and fake bridge advertise a 27-tool catalog including four Survey geometry tools, `survey_fbk_dry_run`, `survey_database_status`, `survey_working_folder_get`, `survey_project_list`, and `survey_network_list`.
- Implemented READ/Low tools: `survey_bearing_to_azimuth`, `survey_azimuth_to_bearing`, `survey_distance`, and `survey_point_by_azimuth`.
- Installed-bundle live validation reported the 22-tool catalog and executed `survey_bearing_to_azimuth` successfully in Civil 3D 2026.
- Phase 4B was packaged and installed as a 23-tool bundle, passed the offline bundle diagnostic, and passed live smoke validation through the official MCP client against Civil 3D 2026.
- Survey capability is enabled for geometry, FBK dry-run, Survey Database readiness, and working-folder read tools; the Autodesk bridge reports `SurveyCom` as true once the Phase 4D read-only working-folder adapter is present.
- Phase 4B implements `survey_fbk_dry_run` as a pure inline-content parser. It recognizes the initial FBK subset `JOB`, `UNIT`, `EDM`, `SCALE`, `HORIZ`, `VERT`, `NEZ`, `STN`, `AZ`, `BS`, `PRISM`, `F1`, and `F2`, and skips comment lines starting with `!`, `;`, or `#`.
- The parser returns control point previews, setup summaries, observation counts, supported keywords, and warnings without reading files, touching Civil 3D, or using COM.
- Phase 4C adds `survey_database_status` as a READ/Low readiness probe. The pure default adapter is intentionally `none` and reports `not_configured`; the Autodesk bridge uses `environment_probe` to check known Survey runtime files without initializing COM or opening Survey Databases.
- The Phase 4C 24-tool Release bundle was packaged to `artifacts\Civil3D.HybridMcp.bundle`, installed to `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle`, passed the offline bundle diagnostic, and passed a live official MCP client smoke test against Civil 3D 2026. The live `survey_database_status` response returned `status: "interop_available"` and `survey_database_available: false`.
- Phase 4D adds `survey_working_folder_get` as a READ/Low Survey COM working-folder read. The Autodesk bridge dispatches the COM call through the host context, requires an active document, and does not open Survey Databases, set working folders, import FBK, or mutate drawing state.
- The Phase 4D 25-tool Release bundle was packaged, installed to `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle` after terminating the prior `acad.exe` that held the bridge DLL, passed the offline bundle diagnostic, and passed a live official MCP client smoke test against Civil 3D 2026. The direct live named-pipe call to `survey_working_folder_get` returned structured `DocumentNotFound` because that launch reported `documentCount: 0`.
- Phase 4E-A confirmed that `documentCount: 0` is a real no-active-document host state and now returns structured `DocumentNotFound` details from both `drawing_info` and `survey_working_folder_get`.
- Phase 4E-A also opened a transient unsaved `Drawing1.dwg`, read the Survey working folder successfully through the installed bridge, included the active document descriptor in the response, and closed the transient drawing without saving.
- Phase 4E-B adds `survey_project_list` as a READ/Low Survey COM project collection read. The Autodesk bridge dispatches it through the host context, requires an active document, and does not open Survey Databases, set working folders, create projects, or mutate drawing state.
- The Phase 4E-B 26-tool Release bundle was installed to `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle`, passed the offline bundle diagnostic with no issues or warnings, and was live validated against Civil 3D 2026.
- Phase 4E-B live validation confirmed the Start/no-document state returns structured `DocumentNotFound`, then opened a transient unsaved `Drawing1.dwg` and read the Survey project collection successfully with `status: "projects_available"` and `count: 0`.
- Phase 4E-C adds `survey_network_list` as a READ/Low Survey COM network collection read with an optional `project_name` filter. The Autodesk bridge dispatches it through the host context, requires an active document, and does not open Survey Databases or Survey projects, set working folders, create networks, or mutate drawing state.
- Phase 4E-C verified the Survey COM network surface by reflection over the installed `Autodesk.AECC.Interop.Survey.dll` before writing adapter code, confirming `IAeccSurveyProject.Networks`, `IAeccSurveyNetworks.Count`, the integer indexer, and readable `ID`, `Name`, `Description`, and `Path` on `IAeccSurveyNetwork`.
- Phase 4E-C introduces the `SurveyProjectNotFound` error code so an unmatched `project_name` fails structurally instead of returning an empty success.
- The Phase 4E-C 27-tool Release bundle was installed, passed the offline bundle diagnostic with no issues or warnings, and was live validated against Civil 3D 2026.
- Phase 4E-C live validation confirmed structured `DocumentNotFound` with no active document, `status: "no_projects"` with a transient unsaved `Drawing1.dwg`, and structured `SurveyProjectNotFound` for an unmatched project filter.
- Phase 4E-C live validation could not exercise network item enumeration because the host had zero Survey projects. That branch is covered by unit and fake-bridge tests only and must be re-validated on a host with Survey data before Survey write tooling starts.

## Phase 2D Civil 3D Profile Reads

- Phase 2D adds `profile_list`, `profile_get`, and `profile_elevation_at_station` as READ/Low Civil 3D .NET API tools, taking the catalog from 27 to 30 tools.
- The Civil 3D API surface was verified from assembly metadata before implementation: `Alignment.GetProfileIds()`, `Profile.ElevationAt`, `Profile.GradeAt`, and the `Name`/`Description`/`StyleName` members inherited from `Autodesk.Civil.DatabaseServices.Entity`.
- Profiles are enumerated through their parent alignments, so every profile record carries `alignment_name` and `alignment_handle`.
- Because profile names are unique only within an alignment, an ambiguous name returns `PROFILE_AMBIGUOUS` listing the competing alignments instead of silently picking one.
- Live validation opened a copy of the user-confirmed test DWG, never the original, and read 59 profiles across 32 alignments, resolved one profile by handle, and returned elevation 280.856 and grade -0.216 at station 2178.913.
- The original test DWG kept its pre-test timestamp, and no `acad.exe` was left running.

## Survey Module Status

- On 2026-09-13 the user confirmed there is no Survey project on this machine, and a filesystem check found only a Data Shortcuts project and no Survey database anywhere under the user document trees.
- Phase 4E-C `survey_network_list` therefore remains live validated only for its no-document, no-projects, and project-not-found paths.
- Phase 4E-D was started and then stopped by user decision; no Survey network detail code was left in the repository.

## Phase 2E Civil 3D Corridor Reads

- Phase 2E adds `corridor_list` and `corridor_get` as READ/Low Civil 3D .NET API tools, taking the catalog from 30 to 32 tools.
- The API surface was verified from assembly metadata first: `CivilDocument.CorridorCollection`, `Corridor.Baselines`, `Baseline.BaselineRegions`, `Baseline.AlignmentId`, `Baseline.ProfileId`, and `BaselineRegion.AssemblyId`.
- `corridor_list` returns summaries only; `corridor_get` walks baselines and regions and resolves alignment, profile, and assembly names.
- `Corridor.Rebuild()` is never called. Corridor rebuild stays unimplemented because it is a long-running write that needs the progress/cancellation model from Phase 7.
- Live validation read corridor `PAV` with 19 baselines and 2 corridor surfaces from a copy of the user-confirmed test DWG.
- The same run reproduced the modal blocker: host-dispatched reads hung until the `References - Not Found Files` dialog was dismissed. This is now the strongest argument for the dialog monitor.

## Phase 1C Modal Dialog Monitor

- Phase 1C replaces the Phase 1 placeholder monitor with a real Win32 implementation, taking the catalog from 32 to 33 tools.
- `list_dialogs` answers without the Autodesk host context. This is the structural point of the phase: every other tool dispatches through the Autodesk idle loop and therefore hangs when a modal blocks it.
- Detection uses the Windows modality rule directly: a modal disables its owner, so a disabled main window next to an enabled owned window means the host is blocked.
- Classification logic is separated from Win32 behind `IWindowEnumerator`, so it is covered by nine unit tests in the new `Civil3D.HybridMcp.Diagnostics.Tests` project.
- Window titles are read with `SendMessageTimeout` and `SMTO_ABORTIFHUNG`; using `GetWindowText` would have reintroduced the very hang the tool exists to diagnose.
- Live validation: with the `References - Not Found Files` modal up, `list_dialogs` answered in 28 ms, reported `blocked_by_modal: true`, named the dialog, and did not flag the disabled `Properties` palette. After dismissal it reported a clean state and `document_list` recovered.
- `civil3d_status` now reports `supported: true` with real counts, and the `DialogMonitor` capability flag moved from `partial` to `active`.
- Still open: `dismiss_dialog` and `cancel_active_command`, both write-class and both gated on an allowlist design.

## Phase 2F AEC Property Set Reads

- Phase 2F adds `propertyset_definition_list`, `propertyset_list`, and `propertyset_get`, taking the catalog from 33 to 36 tools.
- These are the first tools that reach outside the Civil 3D API. Property sets are an AEC feature, so the Civil tools project now references `AecPropDataMgd`.
- The API was verified from assembly metadata before implementation: the definition dictionary exposes `Records` through `Autodesk.Aec.DatabaseServices.Dictionary`, and attached sets come from `PropertyDataServices.GetPropertySets`.
- Values are returned as an invariant string plus a separate `numeric_value`, so a caller computing widths or stations does not have to reparse localized text.
- Two new error codes keep partial reads honest: `ObjectNotFound` for a handle that does not resolve, and `PropertySetNotFound` for a set that is not attached.
- Live validation ran twice. The first drawing proved definition reads and object resolution across 4146 objects. A second, user-supplied drawing closed the value gap: 526 objects carried the `Dados_Rodovia` set with real BR-153 kilometre-marker values, and `propertyset_get` plus `PropertySetNotFound` were exercised against real data.
- Phase 3C closed the remaining numeric-value evidence gap by creating a controlled `Real` property set in a temporary test DWG copy and reading the populated `numeric_value` back through `propertyset_get`.
- Phase 2F also exercised the Phase 1C dialog monitor in anger: the test drawing raised the familiar modal, `list_dialogs` named it, and the test cleared it and continued.
