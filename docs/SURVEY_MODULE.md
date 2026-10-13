# Survey Module

Date: 2026-09-13

Status: Phase 4E-C Survey COM network-list read implemented and live validated.

## Scope

The Survey module is a separate project:

```text
src/Civil3D.HybridMcp.Tools.Survey
```

Phase 4A started with pure topographic geometry. Phase 4B adds an FBK dry-run parser that reads inline FBK content and returns a structured preview. Phase 4C starts the Survey Database path with a conservative readiness/status probe. Phase 4D adds the first typed Survey COM adapter for working-folder reads only. Phase 4E-A stabilizes the live no-document and active-document behavior for that adapter. Phase 4E-B adds the first Survey project collection read. Phase 4E-C adds the first Survey network collection read. These tools do not write DWGs, do not import data, do not open Survey Databases, and do not require document locks or transactions.

## Implemented Tools

All implemented Survey tools are READ/Low:

- `survey_bearing_to_azimuth`
- `survey_azimuth_to_bearing`
- `survey_distance`
- `survey_point_by_azimuth`
- `survey_fbk_dry_run`
- `survey_database_status`
- `survey_working_folder_get`
- `survey_project_list`
- `survey_network_list`

The bridge advertises these tools in the runtime manifest as module `Survey`. The capability flag `Survey` is true when these tools are present. The Autodesk bridge reports `SurveyCom` as true because a typed read-only Survey COM gateway is now present.

## FBK Dry Run

`survey_fbk_dry_run` parses FBK text supplied as `content`. It does not read arbitrary file paths from inside the bridge.

Supported Phase 4B subset:

- header/noise records: `JOB`, `UNIT`, `EDM`, `SCALE`, `HORIZ`, `VERT`
- comments starting with `!`, `;`, or `#`
- control point records: `NEZ pt northing easting elevation "description"`
- setup records: `STN pt instrument_height "description"`
- backsight records: `BS pt circle`
- azimuth records: `AZ from to dms`
- prism height records: `PRISM height`
- observation records: `F1` / `F2` with optional `VA`, `ZA`, or `ZE`

The result includes total lines, parsed records, control points, setup previews, observation counts, warning counts, supported keywords, and human-readable warnings for records such as unknown keywords or observations before a setup.

## Survey Database Status

`survey_database_status` reports whether a Survey Database adapter is ready. In the pure service default, with no gateway injected, it returns:

- `adapter: "none"`
- `status: "not_configured"`
- `survey_com_available: false`
- `survey_database_available: false`
- `requires_active_document: true`

In the Autodesk bridge, Phase 4C injects a filesystem-only environment probe. On Civil 3D 2026 this live probe returned:

- `adapter: "environment_probe"`
- `status: "interop_available"`
- `survey_com_available: true`
- `survey_database_available: false`
- `requires_active_document: true`
- `interop_assemblies: ["Autodesk.AECC.Interop.Survey.dll", "Autodesk.AECC.Interop.UiSurvey.dll"]`
- `capabilities: ["survey_com_interop_available"]`

This is intentional. It gives MCP clients a stable readiness contract before broader Survey Database reads are introduced. It checks known Autodesk Survey runtime files but does not initialize Survey COM, open Survey Databases, set the Survey working folder, create projects, list networks, or import FBK.

Live validation:

- The Phase 4C installed bundle advertised the 24-tool catalog in Civil 3D 2026.
- The live `civil3d_status` capability flags reported `survey_database_status_enabled: true`, `survey: true`, and `surveyCom: false`.
- The official MCP client called `survey_database_status` successfully and the live bridge returned `adapter: "environment_probe"` and `status: "interop_available"`.

## Survey Working Folder

`survey_working_folder_get` reads the current Civil 3D Survey working folder through a typed Survey COM adapter. It is a READ/Low tool with no input arguments.

Expected successful response fields:

- `adapter: "survey_com"`
- `status: "working_folder_available"`
- optional `working_folder`
- `requires_active_document: true`
- `capabilities: ["working_folder_read"]`

The tool intentionally requires an active Civil 3D document because the COM Survey document is initialized from the active AutoCAD document. If the bridge cannot see an active document, it returns a structured `DocumentNotFound` error instead of guessing or initializing Survey state blindly.

Live validation:

- The Phase 4D installed bundle advertised the 25-tool catalog in Civil 3D 2026.
- The live `civil3d_status` capability flags reported `survey_working_folder_get_enabled: true`, `survey: true`, and `surveyCom: true`.
- A direct named-pipe call to `survey_working_folder_get` returned `DocumentNotFound` with message `Survey working folder read requires an active Civil 3D document.` because that live launch reported `documentCount: 0`.
- The official MCP client live smoke test passed against the installed 25-tool catalog and accepted the structured no-active-document result.
- Phase 4E-A confirmed that the no-active-document case returns `DocumentNotFound` with structured details: `adapter: "survey_com"`, `document_count: 0`, and `requires_active_document: true`.
- Phase 4E-A opened a transient unsaved `Drawing1.dwg` and confirmed a successful working-folder read with `status: "working_folder_available"`, `working_folder: "C:\\Users\\Public\\Documents\\Autodesk\\Civil 3D Projects"`, and a non-null active document descriptor.
- The transient drawing was closed without saving after validation.

## Survey Project List

`survey_project_list` reads the Civil 3D Survey project collection exposed by Survey COM. It is a READ/Low tool with no input arguments.

Expected successful response fields:

- `instance_id`
- `adapter: "survey_com"`
- `status: "projects_available"`
- optional `working_folder`
- `count`
- `projects`
- `requires_active_document: true`
- `capabilities: ["working_folder_read", "project_list_read"]`

Each project record includes:

- `name`
- optional `path`
- optional `project_guid`

The tool intentionally requires an active Civil 3D document for the same reason as `survey_working_folder_get`: the Survey COM document is initialized from the active AutoCAD document. It does not call Survey Database open/create/import operations, does not set the Survey working folder, and does not mutate drawing state.

Live validation:

- The Phase 4E-B installed bundle advertised the 26-tool catalog in Civil 3D 2026.
- A Start/no-document launch returned structured `DocumentNotFound` for `survey_project_list`, matching `documentCount: 0`.
- A transient unsaved `Drawing1.dwg` was opened and `survey_project_list` returned success with `status: "projects_available"`, `count: 0`, `working_folder: null`, and a non-null active document descriptor.
- The transient drawing was closed without saving after validation.

## Survey Network List

`survey_network_list` reads the Survey network collection exposed by Survey COM. It is a READ/Low tool with one optional argument.

Input:

- `project_name` optional; when omitted, networks from every Survey project are listed

Expected successful response fields:

- `instance_id`
- `adapter: "survey_com"`
- `status`
- optional `project_name`
- optional `working_folder`
- `count`
- `networks`
- `requires_active_document: true`
- `capabilities: ["working_folder_read", "project_list_read", "network_list_read"]`
- `limitations`
- `warnings`

Each network record includes:

- `name`
- optional `project_name`
- optional `description`
- optional `path`
- optional `id`

Status values:

- `networks_available` when every matched project's networks were read
- `networks_partial` when at least one project's networks could not be read; the reason appears in `warnings`
- `no_projects` when the Survey working folder exposes no projects

Failure policy follows the no-hidden-failure rule:

- no active document returns `DocumentNotFound` with `adapter`, `document_count`, and `requires_active_document` details
- an unmatched `project_name` returns `SurveyProjectNotFound` with `adapter`, `project_count`, and `working_folder` details instead of an empty success
- when `project_name` targets one project whose networks cannot be read, the tool returns `NotSupported` rather than pretending the project has no networks
- when no filter is supplied, an unreadable project becomes a warning and status `networks_partial`, so the readable projects are still returned

The Survey COM surface used here was verified by reflection against the installed `Autodesk.AECC.Interop.Survey.dll` for Civil 3D 2026: `IAeccSurveyProject.Networks` returns `AeccSurveyNetworks`, `IAeccSurveyNetworks` exposes `Count` and an integer indexer, and `IAeccSurveyNetwork` exposes readable `ID`, `Name`, `Description`, and `Path`. The write members `IAeccSurveyNetworks.Create` and `IAeccSurveyNetworks.Remove` exist but are never called.

Live validation:

- The Phase 4E-C installed bundle advertised the 27-tool catalog in Civil 3D 2026.
- A no-document launch returned structured `DocumentNotFound` with `document_count: 0`.
- A transient unsaved `Drawing1.dwg` was opened and `survey_network_list` returned success with `status: "no_projects"`, `count: 0`, and a non-null active document descriptor.
- An unmatched `project_name` returned `SurveyProjectNotFound` with `project_count: 0`.
- The transient drawing was closed without saving.

Known coverage gap: the validation host had no Survey projects, so the live COM path that enumerates network items was not exercised. That branch is currently covered by unit and fake-bridge tests only and should be re-validated on a host with at least one Survey project. See `docs/PHASE_4E_C_LIVE_TEST.md`.

## Geometry Convention

Survey azimuths use the common Civil/topographic convention:

- `0` degrees points north
- `90` degrees points east
- `180` degrees points south
- `270` degrees points west

Coordinate projection uses:

```text
delta_x = distance * sin(azimuth)
delta_y = distance * cos(azimuth)
```

This matches easting/northing-style XY calculations.

## Bearing Format

`survey_bearing_to_azimuth` accepts quadrant bearing text with:

- first direction: `N` or `S`
- angle: degrees and optional minutes/seconds
- final direction: `E` or `W`

Examples:

```text
N 30 0 0 E
S 45 0 0 E
S 12 30 0 W
N 45 30 0 W
```

Angles must be between 0 and 90 degrees. Minutes and seconds must be in the normal 0 through less than 60 range.

## Safety

Phase 4E-C Survey tools:

- do not mutate drawing state
- access Survey COM only in `survey_working_folder_get`, `survey_project_list`, and `survey_network_list`
- do not call shell commands
- do not open Survey Databases
- do not set Survey working folders
- do not create or open Survey projects
- never call Survey COM network create/remove members
- do not perform arbitrary file IO
- limit the Survey Database environment probe to known Autodesk runtime files under the installed Civil 3D directory
- report Survey Database readiness without opening or mutating Survey Databases
- reject invalid bearings
- reject non-finite numeric inputs
- reject negative projection distance
- parse FBK content only as dry-run preview

Because they are pure READ tools, rollback is not required.

## IBGE-PPP Corrected Base

Recorded knowledge, nothing implemented. See `docs/SURVEY_IBGE_PPP.md`.

The operator has a working, externally validated pipeline that post-processes an
RTK base observation through the IBGE-PPP service, so a survey can be referenced
to a corrected base coordinate rather than a raw RTK one. That document records
the proven pipeline, the IBGE submission fields, the antenna-height rule that
decides whether the result is right or confidently wrong, and the raw formats
that do and do not convert.

Two facts matter most for anyone planning work here:

- the height sent to IBGE is the **vertical** height to the ARP, never the slant
  height; both are present in the raw file and both look plausible;
- `.HCN` (CHC/CHCNAV) does not convert with the available toolchain and is
  converted upstream by the vendor's own software, so it reaches this platform
  already as RINEX.

Designing the procedure is the operator's next step, and the document lists the
open questions it has to answer, including whether this platform performs the
outbound submission at all.

## Future Work

Next Survey work should remain behind typed adapters:

- re-validate `survey_network_list` on a host that has at least one Survey project with networks
- Survey project get
- Survey network get
- Survey control and non-control points
- setups and observations
- figures
- Figure Prefix Database
- controlled FBK import
- IBGE-PPP corrected base workflow, once the operator defines the procedure; see `docs/SURVEY_IBGE_PPP.md`

Survey Database and import operations may require Civil 3D COM or command fallbacks. When that happens, the tool must explicitly document the fallback and return `NOT_SUPPORTED`, `API_LIMITATION`, `REQUIRES_COM`, or `REQUIRES_UI_AUTOMATION` instead of simulating success.
