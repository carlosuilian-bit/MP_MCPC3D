# Live Smoke Test

Date: 2026-09-04

Status: `PASSED_PHASE_1_5_LIVE_SMOKE`

Codex validated the MCP server, STDIO adapter, endpoint discovery, named-pipe protocol, fake bridge behavior, structured errors, reconnection, ambiguity handling, packaging, Civil 3D bundle autoload, CLI live calls, and MCP STDIO live calls. A live Civil 3D 2026 process reported `ACADVER=25.1s`; the autoload manifest was updated to allow `R25.1`, then Civil 3D was restarted and validated successfully.

Use only a blank drawing or non-production DWG for this smoke test.

## 1. Build And Package

```powershell
dotnet build .\Civil3D.HybridMcp.sln -c Release
.\scripts\package.ps1 -Configuration Release
```

If local PowerShell policy blocks scripts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Configuration Release
```

Confirm the bundle exists:

```text
artifacts\Civil3D.HybridMcp.bundle
```

The bundle should include `Civil3D.HybridMcp.*.dll`, `.pdb`, `Civil3D.HybridMcp.Bridge.deps.json`, and `Civil3D.HybridMcp.Bridge.runtimeconfig.json`. It must not include Autodesk DLLs.

## 2. Install Bundle

```powershell
.\scripts\install-bundle.ps1 -Configuration Release
```

Target:

```text
%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle
```

## 3. Start Civil 3D 2026

Open a blank drawing or lab DWG.

If Civil 3D was already open before installing or updating the bundle, close and reopen it. Autodesk bundle autoload is validated only on a fresh host startup.

Run either command in Civil 3D:

```text
C3DHMCPSTATUS
C3DHYBRIDSTATUS
```

Expected:

- bridge status is `running`
- `BridgeVersion=0.1.0`
- `Protocol=1.0.0`
- `InstanceId` is shown
- `Pipe` is shown
- `EndpointRegistered=True`
- active document name is shown when a drawing is open

## 4. Check Logs And Endpoint

Expected endpoint directory:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\endpoints
```

Expected bridge log:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\logs\bridge-YYYYMMDD.jsonl
```

The bridge log should include:

- `initialize_start`
- `autodesk_reference_probe`
- `named_pipe_started`
- `endpoint_registered`

## 5. Validate Server CLI

```powershell
.\scripts\run-server.ps1 discover
```

Copy the reported `instance_id`, then run:

```powershell
.\scripts\run-server.ps1 ping --instance <instance_id>
.\scripts\run-server.ps1 status --instance <instance_id>
.\scripts\run-server.ps1 document-list --instance <instance_id>
.\scripts\run-server.ps1 drawing-info --instance <instance_id>
```

Expected:

- all calls return structured JSON
- `success` is `true`
- document identity is returned for the active drawing
- no drawing modification occurs

## 6. Validate MCP STDIO

Configure an MCP client using `docs/MCP_CLIENT_SETUP.md`.

Expected `tools/list`:

- `civil3d_ping`
- `civil3d_status`
- `document_list`
- `drawing_info`

Expected `tools/call` for each tool:

- structured content includes `success=true`
- `tool` matches the requested tool
- `correlationId` is present
- `document` is present when Civil 3D reports an active document

## Result Recording

Completed on 2026-09-04.

| Item | Result |
| --- | --- |
| Civil 3D host | Autodesk Civil 3D 2026 |
| `ACADVER` | `25.1s (LMS Tech)` |
| Product version reported by bridge | `25.1.0.0` |
| Bridge version | `0.1.0` |
| Protocol version | `1.0.0` |
| DWG used | Blank `Drawing1.dwg` created for smoke test |
| Install path | `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle` |
| Endpoint | `%LOCALAPPDATA%\Civil3DHybridMcp\endpoints\civil3d-hybrid-mcp-5052-f431b7adc1b941978511eac8b3f71eae.json` |
| Instance ID | `f431b7adc1b941978511eac8b3f71eae` |
| CLI discover | Passed |
| CLI `civil3d_ping` | Passed |
| CLI `civil3d_status` | Passed |
| CLI `document_list` | Passed |
| CLI `drawing_info` | Passed |
| MCP STDIO live test | Passed |
| Autodesk load errors | None observed after manifest fix |

Important finding:

- Initial autoload did not create an endpoint with `SeriesMax="R25.0"`.
- Civil 3D 2026 reported `ACADVER=25.1s`.
- Updating `PackageContents.xml` to `Platform="AutoCAD*"` and `SeriesMax="R25.1"` fixed autoload.

Bridge log confirmed:

- `initialize_start`
- `autodesk_reference_probe`
- `named_pipe_started`
- `endpoint_registered`
- `initialize_success`

Validation commands:

```powershell
dotnet build .\Civil3D.HybridMcp.sln -c Debug -v:minimal
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-bundle.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-server.ps1 discover
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-server.ps1 ping --instance f431b7adc1b941978511eac8b3f71eae
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-server.ps1 status --instance f431b7adc1b941978511eac8b3f71eae
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-server.ps1 document-list --instance f431b7adc1b941978511eac8b3f71eae
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-server.ps1 drawing-info --instance f431b7adc1b941978511eac8b3f71eae
$env:CIVIL3D_HYBRID_LIVE_INSTANCE_ID='f431b7adc1b941978511eac8b3f71eae'; dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~OfficialMcpClientCanCallLiveBridgeWhenConfigured" -v:minimal
dotnet test .\Civil3D.HybridMcp.sln -c Debug --no-build -v:minimal
```

Phase 2 may now start for read-only Civil 3D tools, keeping the same safety rules: no production DWGs, no writes until transaction/rollback paths are implemented and tested.
