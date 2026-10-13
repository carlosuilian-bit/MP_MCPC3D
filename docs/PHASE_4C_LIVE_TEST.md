# Phase 4C Live Test

Date: 2026-09-13

## Scope

Validate the installed Phase 4C Autodesk bundle against Civil 3D 2026 after adding `survey_database_status` to the 24-tool catalog and after adding the conservative Survey environment probe.

This test did not open or mutate a DWG. Civil 3D was on the Start screen with `documentCount: 0`.

## Preparation

The previous Civil 3D process was still holding `Civil3D.HybridMcp.Bridge.dll`. The user granted temporary same-day permission to terminate `acad.exe` when needed for plugin DLL installation, so the stale process was terminated before installing the bundle.

## Commands

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-bundle.ps1 -Configuration Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-bundle-install.ps1 -AsJson
```

Civil 3D was launched with the known-good quoted `/ld` argument:

```powershell
Start-Process -FilePath "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe" -ArgumentList '/ld "C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx" /p "<<C3D_Metric>>" /product C3D /language en-US'
```

Live smoke:

```powershell
$env:CIVIL3D_HYBRID_LIVE_INSTANCE_ID = "cbe58d0d4fb54825a5fa484cd4b00137"
dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug --filter "FullyQualifiedName~OfficialMcpClientCanCallLiveBridgeWhenConfigured" -v:minimal
```

## Results

- Installed bundle diagnostic: passed with no issues or warnings.
- Civil 3D process: `37756`.
- Bridge instance: `cbe58d0d4fb54825a5fa484cd4b00137`.
- Product: Autodesk Civil 3D `25.1.0.0`.
- Tool catalog: 24 tools.
- Capability flags:
  - `survey_enabled: true`
  - `survey_geometry_enabled: true`
  - `survey_fbk_dry_run_enabled: true`
  - `survey_database_status_enabled: true`
  - `surveyCom: false`
- Official MCP live test: 1 passed, 0 failed.
- `survey_database_status` returned the expected conservative environment probe:
  - `adapter: "environment_probe"`
  - `status: "interop_available"`
  - `survey_com_available: true`
  - `survey_database_available: false`
  - `interop_assemblies: ["Autodesk.AECC.Interop.Survey.dll", "Autodesk.AECC.Interop.UiSurvey.dll"]`
  - `capabilities: ["survey_com_interop_available"]`

## Safety Notes

- No drawing was opened.
- No Civil 3D object was created, edited, imported, or deleted.
- No Survey Database was opened or initialized.
- No Survey COM automation was invoked.
- The environment probe only checked for known Autodesk Survey runtime files under the installed Civil 3D `C3D` directory.
