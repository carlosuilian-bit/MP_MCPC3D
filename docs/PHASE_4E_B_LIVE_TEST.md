# Phase 4E-B Live Test - Survey Project List

Date: 2026-09-13

Scope: validate the installed Phase 4E-B Autodesk bundle against Civil 3D 2026 with the new read-only `survey_project_list` tool.

## Result

Status: Passed.

The installed bundle advertised 26 tools, including `survey_project_list`. The new tool was exercised through the live named pipe against Civil 3D 2026.

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

Important startup note:

- During Civil 3D startup the endpoint can briefly report a template document.
- After startup settles, the same launch can report `documentCount: 0`.
- `survey_project_list` correctly returns `DocumentNotFound` in that no-active-document state.

## Tool Manifest

The live `tools/list` response reported:

```json
{
  "toolCount": 26,
  "hasSurveyProjectList": true
}
```

## No Active Document Case

With no active Civil 3D document, `survey_project_list` returned a structured tool error instead of initializing Survey state blindly:

```json
{
  "success": false,
  "errorCode": "DocumentNotFound",
  "errorMessage": "Survey project listing requires an active Civil 3D document."
}
```

## Active Document Case

A transient unsaved `Drawing1.dwg` was opened through COM for the read-only validation. The direct named-pipe call returned:

```json
{
  "success": true,
  "responseDocument": "Drawing1.dwg",
  "status": "projects_available",
  "workingFolder": null,
  "count": 0,
  "projectNames": [],
  "warnings": 0
}
```

This proves the typed Survey COM gateway can read the Survey project collection for an active Civil 3D document without opening Survey Databases or mutating drawing state.

## Cleanup

The transient drawing was closed without saving.

The launched Civil 3D process accepted `Quit()` but remained alive after a short wait, so the test PID was force-stopped under the user's temporary permission for plugin DLL installation and validation cleanup.

## Safety Boundary

`survey_project_list` is READ/Low:

- no DWG mutation
- no document lock or transaction required
- no Survey Database open/create/import call
- no working-folder change
- no project/network/figure/setup/observation creation
- no FBK import
