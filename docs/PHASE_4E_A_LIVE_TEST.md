# Phase 4E-A Live Test

Date: 2026-09-13

Scope: Survey COM read stability for `survey_working_folder_get`, structured no-document diagnostics, active-document response metadata, installed bundle diagnostics, and live Civil 3D 2026 validation.

## Objective

Close the gap left by Phase 4D:

- prove that `documentCount: 0` is handled as a real no-active-document host state
- return structured `DocumentNotFound` diagnostics instead of `InternalError`
- prove that `survey_working_folder_get` succeeds when Civil 3D has an active document visible to the bridge
- include the active document descriptor on successful host-context Survey responses

The tool remains intentionally narrow:

- READ/Low
- no input arguments
- requires an active Civil 3D document
- reads only the current Survey working folder
- does not open Survey Databases
- does not set working folders
- does not create projects, networks, figures, setups, or observations
- does not import FBK
- does not mutate drawing state

## Root Cause

The Phase 4D live run showed an AutoCAD/Civil 3D host window while the bridge reported:

- `documentCount: 0`
- `activeDocument: null`

Phase 4E-A confirmed this as a real host state visible through the Autodesk API, not a bridge enumeration or threading bug. The correct behavior is to fail with `DocumentNotFound` and include details that tell callers an active document is required.

## Code Changes Validated

Phase 4E-A added reusable structured tool exceptions:

- `ToolExecutionException` in the core command pipeline
- Survey-specific exceptions now carry optional structured details
- bridge execution now preserves typed tool error codes and details
- `drawing_info` maps no-active-document state to `DocumentNotFound`
- `survey_working_folder_get` includes Survey COM diagnostic details when no active document exists
- successful `survey_working_folder_get` responses include the active `DocumentDescriptor` when the bridge can resolve one

## Build, Install, And Offline Diagnostic

The Release bridge build passed:

```powershell
dotnet build .\src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj -c Release --no-restore
```

Result:

- build succeeded
- 9 known Autodesk MSB3277 warnings

The updated bundle was installed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-bundle.ps1 -Configuration Release
```

The installed bundle diagnostic passed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-bundle-install.ps1 -AsJson
```

Result:

- `success: true`
- no issues
- no warnings

## Live Civil 3D Host

Civil 3D 2026 live process:

- PID: `42780`
- instance: `e197cc9ebd714c99b7f9c2f6a4005e4f`
- product version: `25.1.0.0`
- tool catalog count: `25`
- `survey: true`
- `surveyCom: true`
- `survey_working_folder_get_enabled: true`

Initial state:

- window title: `Autodesk Civil 3D 2026 - [Start]`
- `documentCount: 0`
- `activeDocument: null`

## No Active Document Validation

`drawing_info` returned:

```json
{
  "success": false,
  "tool": "drawing_info",
  "document": null,
  "error": {
    "code": "DocumentNotFound",
    "message": "No active drawing is open.",
    "details": {
      "document_count": 0,
      "requires_active_document": true
    }
  }
}
```

`survey_working_folder_get` returned:

```json
{
  "success": false,
  "tool": "survey_working_folder_get",
  "document": null,
  "error": {
    "code": "DocumentNotFound",
    "message": "Survey working folder read requires an active Civil 3D document.",
    "details": {
      "adapter": "survey_com",
      "document_count": 0,
      "requires_active_document": true
    }
  }
}
```

This is the expected safe result when the host has no active DWG.

## Active Document Validation

A transient unsaved `Drawing1.dwg` was opened through AutoCAD COM only for this read-only test.

`drawing_info` returned:

- `success: true`
- `document.name: "Drawing1.dwg"`
- `document.isSaved: false`
- `document.isReadOnly: false`
- `document.units: "Meters"`

`survey_working_folder_get` returned:

```json
{
  "success": true,
  "tool": "survey_working_folder_get",
  "document": {
    "name": "Drawing1.dwg",
    "isSaved": false,
    "isReadOnly": false,
    "units": "Meters"
  },
  "data": {
    "adapter": "survey_com",
    "status": "working_folder_available",
    "working_folder": "C:\\Users\\Public\\Documents\\Autodesk\\Civil 3D Projects",
    "requires_active_document": true,
    "capabilities": [
      "working_folder_read"
    ]
  }
}
```

The transient drawing was closed without saving after validation.

## Safety Result

Passed for Phase 4E-A scope:

- no-active-document failures are structured and actionable
- Survey COM errors preserve typed details instead of collapsing to `InternalError`
- successful working-folder reads include the active document descriptor
- the installed 25-tool bundle remains valid
- no Survey Database was opened
- no working folder was set
- no FBK import was run
- no drawing state was intentionally mutated or saved

Not validated in this run:

- Survey project/network reads
- control or non-control point reads from Survey Database
- setups, observations, and figures
- Figure Prefix Database
- controlled FBK import
