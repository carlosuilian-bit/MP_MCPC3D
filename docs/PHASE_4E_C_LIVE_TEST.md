# Phase 4E-C Live Test - Survey Network List

Date: 2026-09-13

Scope: validate the installed Phase 4E-C Autodesk bundle against Civil 3D 2026 with the new read-only `survey_network_list` tool.

## Result

Status: Passed with a documented coverage gap.

The installed bundle advertised 27 tools, including `survey_network_list`. The new tool was exercised through the live named pipe against Civil 3D 2026 in three states: no active document, active document with zero Survey projects, and an unmatched `project_name` filter.

The live host had no Survey projects, so the COM path that enumerates `IAeccSurveyProject.Networks` items was not exercised live. See the coverage gap section below.

## Installed Bundle

Installed path:

```text
%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle
```

Offline diagnostic:

```json
{
  "success": true,
  "issueCount": 0,
  "warningCount": 0,
  "requiredFileCount": 10
}
```

## Civil 3D Session

Launch command shape:

```powershell
Start-Process -FilePath "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe" -ArgumentList '/ld "C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx" /p "<<C3D_Metric>>" /product C3D /language en-US'
```

The launched test instance was PID 38564. No other `acad.exe` was running before the test.

## Tool Manifest

The live `tools/list` response reported:

```json
{
  "toolCount": 27,
  "hasSurveyNetworkList": true
}
```

The live Survey tool set was:

```text
survey_bearing_to_azimuth
survey_azimuth_to_bearing
survey_distance
survey_point_by_azimuth
survey_fbk_dry_run
survey_database_status
survey_working_folder_get
survey_project_list
survey_network_list
```

## No Active Document Case

With `documentCount: 0`, `survey_network_list` returned a structured tool error instead of initializing Survey state blindly:

```json
{
  "success": false,
  "errorCode": "DocumentNotFound",
  "errorMessage": "Survey network listing requires an active Civil 3D document.",
  "details": {
    "adapter": "survey_com",
    "document_count": 0,
    "requires_active_document": true
  }
}
```

## Active Document Case

A transient unsaved `Drawing1.dwg` was created through COM for the read-only validation. The direct named-pipe call returned:

```json
{
  "success": true,
  "responseDocument": "Drawing1.dwg",
  "adapter": "survey_com",
  "status": "no_projects",
  "workingFolder": "C:\\Users\\Public\\Documents\\Autodesk\\Civil 3D Projects",
  "count": 0,
  "networkNames": [],
  "warnings": [],
  "capabilities": ["working_folder_read", "project_list_read", "network_list_read"]
}
```

`survey_project_list` was called in the same session for cross-checking and returned `status: "projects_available"` with `count: 0`, confirming the host genuinely has no Survey projects.

## Unmatched Project Filter Case

Calling the tool with a `project_name` that does not exist returned the new structured error code instead of an empty success:

```json
{
  "success": false,
  "errorCode": "SurveyProjectNotFound",
  "errorMessage": "Survey project 'NO_SUCH_PROJECT_4E_C' was not found in the current Survey working folder.",
  "details": {
    "adapter": "survey_com",
    "project_count": 0,
    "working_folder": "C:\\Users\\Public\\Documents\\Autodesk\\Civil 3D Projects"
  }
}
```

## Coverage Gap

This validation could not exercise the branch that reads networks from a real Survey project.

Not covered live:

- reading `IAeccSurveyProject.Networks`
- reading `IAeccSurveyNetworks.Count` and its indexer
- reading `Name`, `Description`, `Path`, and `ID` from `IAeccSurveyNetwork`
- the `networks_available` and `networks_partial` statuses
- the per-project warning path when a project's networks cannot be read without opening the project

Reason: the live Civil 3D host reported zero Survey projects, and creating a Survey project is a write operation that is explicitly out of scope for this read-only phase.

Current coverage for those branches comes from unit tests and fake-bridge integration tests only. The API surface itself was verified by reflection against the installed `Autodesk.AECC.Interop.Survey.dll` for Civil 3D 2026, which confirms that `IAeccSurveyProject.Networks` returns `AeccSurveyNetworks` and that `IAeccSurveyNetwork` exposes readable `ID`, `Name`, `Description`, and `Path` members.

Recommended follow-up: re-run this live test on a host that already has at least one Survey project with at least one network, or add a user-confirmed Survey project fixture before enabling any Survey write tooling.

## Cleanup

The transient drawing was closed without saving.

The launched Civil 3D process accepted `Quit()` but remained alive afterwards, matching the Phase 4E-B observation. The test PID was then force-stopped under the user's temporary permission for plugin installation and validation cleanup, and no `acad.exe` remained running at the end of the test.

## Safety Boundary

`survey_network_list` is READ/Low:

- no DWG mutation
- no document lock or transaction required
- no Survey Database open/create/import call
- no Survey project open call
- no working-folder change
- no network, figure, setup, or observation creation
- the COM members `IAeccSurveyNetworks.Create` and `IAeccSurveyNetworks.Remove` are never called
