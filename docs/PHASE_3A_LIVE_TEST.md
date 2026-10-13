# Phase 3A Live Test - `layer_set_current`

Date: 2026-09-12

Host:

- Autodesk Civil 3D 2026
- Product version reported by bridge: `25.1.0.0`
- Bridge version: `0.1.0`
- Protocol version: `1.0.0`

## Scope

Validate the Phase 3A safe write path for `layer_set_current` through the installed Autodesk bundle, live bridge, named pipe JSON-RPC, and AutoCAD transaction pipeline.

This was a non-destructive write test. It changed only the current layer setting in a temporary copy of the user's confirmed test DWG, then restored the original current layer.

## Test Drawing

Original test DWG:

```text
C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg
```

The original opened read-only in one launch because another AutoCAD/Civil 3D process was present, so the write validation used a temporary copy:

```text
C:\Users\CGomes\Desktop\MCPMP_Civil3D\artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_layer_set_current_20260912-104849.dwg
```

## Endpoint Evidence

The temporary-copy session registered:

```json
{
  "instanceId": "bbfff491fe4a4c6c8d50ee1b6f0f6eed",
  "processId": 10780,
  "documentId": "95d9a020ee89185f7a1b39e6cf3c9a17fc5fca8ca1207a3acc82f59def959d91",
  "isReadOnly": false,
  "toolCount": 17,
  "hasLayerSetCurrent": true
}
```

## Execution Evidence

Steps executed through `tools/execute` over the named pipe:

1. `layer_list`
2. `layer_set_current` to target layer `0`
3. `layer_list` confirmation
4. `layer_set_current` back to original layer `_Deletar linha`
5. `layer_list` confirmation

Observed result:

```json
{
  "success": true,
  "layerCount": 482,
  "originalLayer": "_Deletar linha",
  "targetLayer": "0",
  "setChanged": true,
  "setTransaction": {
    "document_locked": true,
    "transaction_started": true,
    "committed": true,
    "rolled_back": false
  },
  "restoredLayer": "_Deletar linha",
  "restoreChanged": true,
  "restoreTransaction": {
    "document_locked": true,
    "transaction_started": true,
    "committed": true,
    "rolled_back": false
  }
}
```

## Notes

- The installed bundle advertised the expected 17-tool catalog.
- No destructive operation was executed.
- The temporary Civil 3D process used for this test was stopped after validation.
- The user's original DWG was not modified by this validation.
