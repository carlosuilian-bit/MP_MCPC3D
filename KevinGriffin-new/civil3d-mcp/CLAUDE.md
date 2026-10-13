# CLAUDE.md — working notes for agents on `civil3d-mcp`

Read this first when improving this repo.

## Use Windows-MCP (PowerShell) for build / reload / test
This plugin only builds and runs on the **Windows host** with Civil 3D + the AutoCAD .NET SDK.
The default Linux bash sandbox **cannot** build it and **cannot** reach the bridge on
`localhost:8270`. Whenever you need to build, hot-reload, or run the regression check, use the
**Windows-MCP `PowerShell` tool** — it runs on the host. This is the right tool for any change here.

## The loop: build → hot-reload → test (all driveable from an agent)
1. **Build** the next version (auto-numbered):
   `powershell -ExecutionPolicy Bypass -File plugin\build-next.ps1` (or double-click `plugin\build-next.bat`).
   Output: `plugin\hotreload\vN\Civil3dMcpBridge_vN.dll`. Full guide in `BUILD.md`.
2. **Hot-reload** into the already-running Civil 3D — no UI clicks needed:
   - Confirm the new DLL is loaded into the process:
     `Get-Process | ? {$_.Name -match 'acad'} | % {$_.Modules | ? {$_.ModuleName -like 'Civil3dMcpBridge*'}}`
   - Headless NETLOAD (passing the path skips the file dialog; `SECURELOAD 0` skips the trust prompt),
     sent via the *currently running* bridge's `send_command`:
     `(progn (setvar "SECURELOAD" 0)(command "_.NETLOAD" "C:/.../plugin/hotreload/vN/Civil3dMcpBridge_vN.dll")(princ))`
   - Switch the listener on port 8270. Commands are **version-suffixed** (`MCPSTART<N>`/`MCPSTOP<N>`).
     Send stop+start as ONE queued command through the OLD bridge so it finishes after the old listener drops:
     `(progn (command "MCPSTOP<old>")(command "MCPSTART<new>")(princ))`
   - Verify: `ping` the bridge. If it answers *after* the old listener was stopped, the new version is serving.
3. **Test:** `cd mcp-server; python regression_check.py` — also writes `regression_results.txt`; read that for results.

## Gotchas (learned the hard way)
- **Host file tools are authoritative.** The Linux bash mount can lag and serve stale/truncated copies of
  files you just wrote. Verify edits with the Read tool or Windows-MCP, not bash `cat`/`py_compile`.
- **`ping` returns a hardcoded app version (`"2026"`)** — it cannot tell you which plugin version owns 8270.
  "Bridge still alive after stopping the old listener" is the reliable proof a switch worked.
  (Improvement idea: have `ping` report the loaded assembly version.)
- Multiple versioned assemblies coexist in the AutoCAD AppDomain; only one binds 8270 at a time.
- Don't restart the Python MCP server just for the regression check (it hits 8270 directly). Restart it
  only to expose newly added *tools* to an MCP client.
- `add_labels_to_layer` writes entity handles to a hardcoded Desktop path with a fixed sleep — fragile;
  still pending a self-label filter (`AeccDbGeneralLabel`). See `mcp-server\MCP_v22_add_labels_bug.md` history.

## Adding a bridge action (the pattern)
- **C#** (`plugin\Civil3dMcpBridge.cs`): add `case "x": return Handlers.X(p);` in `RouteAction`, then
  `public static object X(JObject p)` using `Db.TransactionManager.StartTransaction()`, current space
  `Db.CurrentSpaceId`, returning `new { ..., entityId = id.Handle.Value.ToString() }` (decimal handle).
- **server.py**: add `@mcp.tool() async def x(...)` that calls `call_bridge("x", {...})`.
- **regression_check.py**: add a Section A test (drive the action, assert on the JSON).
- `attach_raster_image` (added v29/v30) is a worked example of all three layers.
