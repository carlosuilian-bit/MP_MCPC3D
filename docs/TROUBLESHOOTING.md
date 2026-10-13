# Troubleshooting

Date: 2026-09-14

## Build Cannot Find Autodesk Assemblies

Set:

```powershell
$env:CIVIL3D_ACAD_DIR = "C:\Program Files\Autodesk\AutoCAD 2026"
$env:CIVIL3D_C3D_DIR = "C:\Program Files\Autodesk\AutoCAD 2026\C3D"
```

Then run:

```powershell
.\scripts\build.ps1
```

## No Endpoints Found

Run:

```powershell
.\scripts\run-server.ps1 discover
```

If the result is empty:

- Civil 3D may not be running
- the bundle may not be installed
- the bridge may not have loaded
- the endpoint descriptor may be stale and cleaned up

Inside Civil 3D, run:

```text
C3DHMCPSTATUS
C3DHYBRIDSTATUS
```

For MCP STDIO clients, `tools/list` returns an empty list when no bridge endpoint is available.

## Bundle Installed But Bridge Did Not Autoload

Check:

- `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle\PackageContents.xml`
- `%LOCALAPPDATA%\Civil3DHybridMcp\logs\bridge-YYYYMMDD.jsonl`
- Civil 3D command line output after startup

Run the offline bundle diagnostic before launching or touching Civil 3D:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-bundle-install.ps1
```

For automated output:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-bundle-install.ps1 -AsJson
```

This validates the installed `.bundle` directory, `PackageContents.xml`, required project DLLs, `LoadOnAutoCADStartup`, Civil 3D 2026 runtime requirements, and absence of packaged Autodesk managed assemblies.

During one Phase 2C validation run, Civil 3D opened to `[Start]` without autoloading the bridge. Manual development recovery used `NETLOAD` against:

```text
%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle\Contents\Win64\Civil3D.HybridMcp.Bridge.dll
```

After `NETLOAD`, the bridge registered the endpoint and all READ tests passed. Treat this as an autoload/startup diagnostic issue, not as proof that the tool contracts failed.

## AutoCAD Message: Cannot Find The Specified Drawing File

Observed during Phase 4A smoke testing:

```text
AutoCAD Message
Cannot find the specified drawing file.
Please verify that the file exists.
```

Cause:

- The Civil 3D launch command passed the `/ld` loader path without preserving quotes around a path containing spaces.
- AutoCAD then interpreted part of `C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx` as if it were a drawing path.

Use a single quoted argument string when launching Civil 3D from PowerShell:

```powershell
$acad = "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe"
$argsText = '/ld "C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx" /p "<<C3D_Metric>>" /product C3D /language en-US'
Start-Process -FilePath $acad -ArgumentList $argsText
```

Do not pass `/ld` and the path as loosely split arguments unless the final command line is verified to preserve quotes around the DBX path.

If the dialog appears, close the temporary AutoCAD/Civil 3D process opened by the bad command and relaunch with the quoted argument string. Do not treat this as a missing DWG or bridge failure.

## Multiple Instances Found

If more than one Civil 3D bridge endpoint exists, calls without `--instance` fail by design.

Use:

```powershell
.\scripts\run-server.ps1 discover
.\scripts\run-server.ps1 ping --instance <instance_id>
```

## Protocol Incompatible

The server rejects protocol major mismatches.

Check:

- server version
- bridge version
- endpoint descriptor protocol version
- installed bundle version

## Bridge Unavailable

The descriptor may exist but the pipe may not answer.

Try:

```powershell
.\scripts\run-server.ps1 discover
```

Then restart Civil 3D or run `C3DHMCPSTOP` and reload the bridge.

MCP tool calls report this as structured error code `BRIDGE_UNAVAILABLE`.

## survey_working_folder_get Returns DocumentNotFound

`survey_working_folder_get` intentionally requires an active Civil 3D document before it initializes the Survey COM document wrapper.

If Civil 3D is on the Start screen, `civil3d_status` may legitimately report:

```json
{
  "documentCount": 0,
  "activeDocument": null
}
```

In that state, the expected response is a structured error:

```json
{
  "code": "DocumentNotFound",
  "details": {
    "adapter": "survey_com",
    "document_count": 0,
    "requires_active_document": true
  }
}
```

Open a test DWG or a transient blank drawing before calling the tool when a successful working-folder value is needed. Do not treat this as a bridge failure.

## Host Tool Times Out But tools/list Works

When `tools/list` or another server-only operation responds, but `document_list`, `drawing_info`, or a drawing tool hangs, assume Civil 3D may be blocked by a modal dialog before retrying the tool.

First call:

```text
list_dialogs
```

`list_dialogs` bypasses the Autodesk host dispatcher, so it can answer while the host thread is blocked. If it reports `blocked_by_modal: true`, read the dialog title and content before deciding what to do. Do not dismiss an unknown or potentially destructive dialog blind.

Observed safe test-DWG case:

- title: `References - Not Found Files`
- symptom: live handshake/tool execution timed out while the main Civil 3D window was disabled
- safe test workflow after reading the dialog: choose `Ignore unresolved reference files` for the temporary DWG copy, then retry the tool

`dismiss_dialog` and `cancel_active_command` are intentionally not implemented yet. Until an allowlist-backed WRITE tool exists, dialog dismissal remains an operator action or a one-off live-test action that must be documented.

## Build Fails With CS1705 After A Civil 3D Update

Symptom: `Tools.AutoCAD`, `Tools.Civil` or `Bridge` stop compiling with

```text
error CS1705: Assembly "Acdbmgd" ... uses "System.Runtime, Version=10.0.0.0" which has a
higher version than referenced assembly "System.Runtime, Version=8.0.0.0"
```

Cause: an Autodesk update moved the host to a newer .NET. Seen on 2026-09-22, when a Civil 3D 2026 update moved the host from .NET 8 to .NET 10 while the product version stayed 25.1.

Confirm before changing anything:

- check the dates of `acdbmgd.dll`, `accoremgd.dll`, `acmgd.dll` and `C3D\AeccDbMgd.dll` in the AutoCAD folder
- read `tfm` in `C:\Program Files\Autodesk\AutoCAD 2026\acdbmgd.runtimeconfig.json`
- build the solution with any new work set aside; if it still fails, the break is environmental

Fix: retarget the Autodesk-facing projects and their tests to the host framework, rebuild, rerun the tests, repackage. Do not suppress CS1705; it has no supported suppression. Full record in `docs/decisions/ADR-0009-net10-autodesk-host.md`.

## Autodesk Assembly Warnings During Build

MSBuild may report assembly-version conflict warnings from Autodesk DLL dependencies, such as `Microsoft.VisualBasic`, `System.Drawing`, or `WindowsBase`.

The Phase 2C solution compiled in this environment despite those warnings. With `Civil3D.HybridMcp.Tools.AutoCAD` and `Civil3D.HybridMcp.Tools.Civil` enabled, the observed count is 9 warnings: the same three conflict categories reported once for each Autodesk-referencing project.

Treat new errors or new warning categories differently from these known Autodesk reference conflict warnings.

See `docs/AUTODESK_ASSEMBLY_RESOLUTION.md` for the exact observed versions and decision.

## MCP STDIO Does Not Start

Build the server first:

```powershell
dotnet build .\Civil3D.HybridMcp.sln -c Release
```

Then verify the command path used by the MCP client:

```powershell
src\Civil3D.HybridMcp.Server\bin\Release\net8.0\Civil3D.HybridMcp.Server.exe mcp
```

STDIO diagnostics are written to stderr. Request logs are JSONL files under `%LOCALAPPDATA%\Civil3DHybridMcp\logs` unless `CIVIL3D_HYBRID_LOG_DIR` is set.

## PowerShell Script Execution Is Blocked

Run scripts with a process-local bypass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Configuration Release
```

## Logs

Default request logs:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\logs
```

Endpoint descriptors:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\endpoints
```

## layer_create Fails With Layer Already Exists

`layer_create` intentionally rejects duplicate names.

Use `layer_get` or `layer_list` first if the caller may be retrying a previous request. The duplicate failure is a structured error and should not be treated as a successful write.

## Civil 3D Prompts On Close After Layer Testing

Phase 2 live validation creates `MCP_TEST_LAYER` in a user-confirmed test DWG. Codex does not issue a save command during this validation. If Civil 3D prompts on close, decide manually whether that test-layer change should be saved.
