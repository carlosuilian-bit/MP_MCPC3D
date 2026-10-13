# Phase 3B Live Test - `layer_update`

Date: 2026-09-12

Host:

- Autodesk Civil 3D 2026
- Product version reported by bridge: `25.1.0.0`
- Bridge version: `0.1.0`
- Protocol version: `1.0.0`

## Scope

Validate the Phase 3B safe write path for `layer_update` through the installed Autodesk bundle, live bridge, named pipe JSON-RPC, and AutoCAD transaction pipeline.

This was a non-destructive bounded write test. It created one temporary layer in a temporary copy of the user's confirmed test DWG, updated that layer, and read it back.

## Test Drawing

Temporary DWG copy:

```text
C:\Users\CGomes\Desktop\MCPMP_Civil3D\artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_layer_update_20260912-113401.dwg
```

The user's original DWG was not modified.

## Endpoint Evidence

The temporary-copy session registered:

```json
{
  "instanceId": "671f1cc3e3bc4e20b637288c568fce1a",
  "processId": 40752,
  "documentId": "c59e4d558283ccdde716ccd5a58fedfe0de60834c20f25cbb553ba3fbf732e5f",
  "product": "Autodesk Civil 3D",
  "productVersion": "25.1.0.0",
  "toolCount": 18,
  "hasLayerUpdate": true
}
```

## Modal Recovery

Opening the copied DWG showed two blocking modal dialogs before `tools/execute` could run:

- `References - Not Found Files`
- `Drawing Recovery`

The dialogs were identified by Win32 window enumeration before dismissal. `References - Not Found Files` was dismissed with `Ignore unresolved reference files`; `Drawing Recovery` was closed with `Fechar`. After that, `civil3d_ping` through `tools/execute` succeeded.

This confirms the need to continue Phase 7 modal-dialog hardening.

## Execution Evidence

Steps executed through `tools/execute` over the named pipe:

1. `tools/list`
2. `layer_create` for `MCP_LU_OK_114201`
3. `layer_update` from `MCP_LU_OK_114201` to `MCP_LU_OK_114201_UPDATED`
4. `layer_get` for `MCP_LU_OK_114201_UPDATED`

Observed result:

```json
{
  "success": true,
  "toolCount": 18,
  "hasLayerUpdate": true,
  "createdLayer": "MCP_LU_OK_114201",
  "createTransaction": {
    "document_locked": true,
    "transaction_started": true,
    "committed": true,
    "rolled_back": false
  },
  "previousLayer": "MCP_LU_OK_114201",
  "updatedLayer": "MCP_LU_OK_114201_UPDATED",
  "changed": true,
  "updatedColor": 2,
  "updatedLocked": true,
  "updatedDescription": "Phase 3B live layer_update updated layer",
  "updateTransaction": {
    "document_locked": true,
    "transaction_started": true,
    "committed": true,
    "rolled_back": false
  },
  "readBackLayer": "MCP_LU_OK_114201_UPDATED",
  "readBackLocked": true,
  "readBackColor": 2,
  "readBackDescription": "Phase 3B live layer_update updated layer"
}
```

## Notes

- The installed bundle advertised the expected 18-tool catalog.
- No destructive operation was executed.
- A first timed-out attempt created `MCP_LU_113522` after modal dialogs were dismissed; no update was observed for that attempt.
- The temporary Civil 3D session was left open after validation.

