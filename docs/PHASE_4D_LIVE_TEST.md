# Phase 4D Live Test

Date: 2026-09-13

Scope: installed Autodesk bundle, Survey COM working-folder read adapter, live bridge manifest, direct named-pipe call, and official MCP client smoke test against Civil 3D 2026.

## Objective

Validate `survey_working_folder_get` as the first typed Survey COM read path.

The tool is intentionally narrow:

- READ/Low
- no input arguments
- requires an active Civil 3D document
- reads only the current Survey working folder
- does not open Survey Databases
- does not set working folders
- does not create projects/networks
- does not import FBK
- does not mutate drawing state

## Build And Install

The Release bundle was packaged:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Configuration Release
```

Result:

- package succeeded
- 9 known Autodesk MSB3277 warnings
- output bundle: `artifacts\Civil3D.HybridMcp.bundle`

The artifact bundle diagnostic passed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-bundle-install.ps1 -BundlePath .\artifacts\Civil3D.HybridMcp.bundle -AsJson
```

Result:

- `success: true`
- no issues
- no warnings
- no packaged Autodesk/Aecc/acmgd DLLs in `Contents\Win64`

The first install attempt failed because the previous Civil 3D process was still holding:

```text
Civil3D.HybridMcp.Bridge.dll
```

The user granted same-day permission to terminate `acad.exe` only when needed for plugin DLL installation. The stale Civil 3D process was terminated, then installation passed:

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
- installed bridge DLL size: `50176`

## Civil 3D Launch

Civil 3D was launched with the corrected `/ld` quoting form:

```powershell
Start-Process -FilePath 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe' -ArgumentList '/ld "C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx" /p "<<C3D_Metric>>" /product C3D /language en-US'
```

This avoids the prior AutoCAD error:

```text
Cannot find the specified drawing file. Please verify that the file exists.
```

Live process:

- PID: `12784`
- title: `Autodesk Civil 3D 2026 - [Drawing1.dwg]`

## Live Bridge Status

Command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-server.ps1 status
```

Result:

- instance: `80157282508b47f7b8b379aabd9c7805`
- process ID: `12784`
- product version: `25.1.0.0`
- bridge version: `0.1.0`
- pipe: `Civil3DHybridMcp-12784-80157282508b47f7b8b379aabd9c7805`
- advertised tools: `25`
- `survey: true`
- `surveyCom: true`
- `survey_working_folder_get_enabled: true`
- `pipeListening: true`
- `endpointRegistered: true`
- bridge-reported `documentCount: 0`
- bridge-reported `activeDocument: null`

The Windows title showed `Drawing1.dwg`, but the bridge's AutoCAD API view reported no active document. The working-folder tool therefore had to return a structured no-document error.

## Direct Named-Pipe Tool Call

Tool:

```text
survey_working_folder_get
```

Result:

```json
{
  "success": false,
  "tool": "survey_working_folder_get",
  "error": {
    "code": "DocumentNotFound",
    "message": "Survey working folder read requires an active Civil 3D document."
  }
}
```

This is the expected safe result when `MdiActiveDocument` is unavailable. No Survey Database was opened and no drawing state was changed.

## Official MCP Client Smoke

Command:

```powershell
$env:CIVIL3D_HYBRID_LIVE_INSTANCE_ID = '80157282508b47f7b8b379aabd9c7805'
dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug --filter "FullyQualifiedName~OfficialMcpClientCanCallLiveBridgeWhenConfigured" -v:minimal
```

Result:

- passed: `1`
- failed: `0`
- skipped: `0`
- total: `1`
- duration: `10 s`

The live MCP test listed the installed 25-tool catalog and called the Phase 4D Survey working-folder tool. The test accepts either a successful working-folder response or a structured document-state error because live host startup can legitimately have no active document.

## Safety Result

Passed for Phase 4D scope:

- bundle packaged and installed
- installed bundle diagnostic passed
- Civil 3D loaded the updated bridge
- live bridge advertised the 25-tool catalog
- Survey COM capability flag was enabled
- `survey_working_folder_get` returned a structured error instead of guessing or mutating state when no active document was visible
- official MCP live smoke passed

Not validated in this run:

- successful non-null working-folder value with an active document visible to the bridge
- Survey project/network reads
- Survey Database open/import/write operations
- FBK import

