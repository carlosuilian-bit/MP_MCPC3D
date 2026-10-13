# Building & reloading the Civil 3D MCP bridge

A short, do-this-each-time guide. The whole loop is: **build → load into Civil 3D → run the regression check.**

## Prerequisites (one time)
- The .NET SDK installed. Check by opening a terminal and running `dotnet --version` — it should print a number. If not, install from https://dotnet.microsoft.com/download.
- Civil 3D open with any drawing.

## 1. Build the next version
Double-click **`plugin\build-next.bat`**.

That's it. It auto-picks the next version number (highest existing `hotreload\vN` + 1), compiles the current `plugin\Civil3dMcpBridge.cs`, and prints what to do next.

- If it says **BUILD FAILED**, read the compiler errors in the window and fix them in `Civil3dMcpBridge.cs`. Nothing in Civil 3D changed — the old bridge is still running.
- If it succeeds, it prints the three commands for step 2, including the exact version number (e.g. `MCPSTART29`).

*(Equivalent manual command, if you prefer a terminal in the `plugin` folder: `powershell -ExecutionPolicy Bypass -File hotreload.ps1 -Version <N>`.)*

## 2. Load it into Civil 3D
In the **Civil 3D command line**, type these one at a time and press Enter after each:

```
MCPSTOP                 (stops the running bridge, frees port 8270)
NETLOAD                 (a file dialog opens — pick plugin\hotreload\vN\Civil3dMcpBridge_vN.dll)
MCPSTART<N>             (e.g. MCPSTART29 — starts the new bridge on port 8270)
```

Use the same `N` the build reported. The `MCPSTART<N>` command name is version-specific on purpose, so a new build can load alongside an old one without colliding.

## 3. Verify
```
cd mcp-server
python regression_check.py
```
Results print to the screen **and** to `mcp-server\regression_results.txt` (share that file if you want a second pair of eyes on a failure).

## Notes
- **You do not need to restart the Python MCP server** for the regression check — it talks straight to port 8270, so only the plugin reload (step 2) matters. Restart the MCP server only when you want newly added *tools* exposed to an MCP client.
- `build-next.bat` runs PowerShell with `-ExecutionPolicy Bypass`, so you don't have to change any system policy.
- Each build lands in its own `plugin\hotreload\vN\` folder; old versions are kept, which is why version numbers only ever go up.
