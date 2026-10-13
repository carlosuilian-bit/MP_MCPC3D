# Phase 2E Live Test - Civil 3D Corridor Reads

Date: 2026-09-13

Scope: validate the installed Phase 2E Autodesk bundle against Civil 3D 2026 with the new read-only `corridor_list` and `corridor_get` tools.

## Result

Status: Passed, after clearing a modal dialog that blocked the Autodesk host thread.

The installed bundle advertised 32 tools, including both corridor tools. Both were exercised through the live named pipe against a real interchange drawing containing a 19-baseline corridor.

## Installed Bundle

Installed path:

```text
%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle
```

Offline diagnostic: success with no issues and no warnings.

## Test Drawing

The user-confirmed test drawing was copied before the test and never opened directly:

```text
source : C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg
copy   : artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_corridor_read_20260913-203002.dwg
```

After the test the source file retained its original timestamp, confirming it was untouched.

## Modal Dialog Blocker

This run reproduced the modal blocker recorded in Phase 3B, and it is the clearest evidence so far that the dialog monitor is a real platform requirement.

Symptoms:

- `tools/list` answered instantly, because the bridge serves the manifest without the Autodesk host context.
- `document_list` and every Civil 3D read hung indefinitely, because those dispatch through the Autodesk idle dispatcher.

Cause, found by enumerating the process windows:

```text
References - Not Found Files    (visible, enabled)
Autodesk Civil 3D 2026 - [...] (main window, DISABLED)
```

A disabled main window alongside a visible child window is the signature of a modal dialog. The dialog is the standard AutoCAD notification that external references could not be resolved. It is informational and does not alter the drawing.

The dialog was identified before being dismissed, then closed with `WM_CLOSE`. Every pending named-pipe call completed immediately afterwards.

Lesson for the platform: a blocked Autodesk host thread is indistinguishable from a slow tool from the client's point of view. `civil3d_status` should be able to report "blocked by modal" and name the dialog, which is exactly what `list_dialogs` is meant to provide.

## Tool Manifest

The live `tools/list` response reported 32 tools, including `corridor_list` and `corridor_get`.

## corridor_list

```json
{
  "count": 1,
  "corridors": [
    {
      "name": "PAV",
      "handle": "109A14",
      "is_out_of_date": false,
      "rebuild_automatic": true,
      "baseline_count": 19,
      "corridor_surface_count": 2,
      "code_set_style_name": "_MP SEÇÃO TIPO",
      "region_lock_mode": "GeometryLock"
    }
  ]
}
```

Rebuild state, counts, style name, and region lock mode were all readable. Accented style names round-tripped correctly through the JSON pipeline.

## corridor_get

Resolved by handle, the corridor returned all 19 baselines. First four:

```json
[
  {
    "name": "RAMO 100 - ",
    "baseline_type": "MainBaseline",
    "alignment_name": "RAMO 100",
    "profile_name": "V-RAMO 100",
    "start_station": 2040.5423846978083,
    "end_station": 2349.332068170084,
    "region_count": 6,
    "first_region": {
      "name": "TRECHO - RAMO - STC - 4",
      "assembly_name": "RAMO - STC",
      "start_station": 2040.5423846978083,
      "end_station": 2163.5271611355997
    }
  },
  {
    "name": "RAMO 200 - PAV",
    "alignment_name": "RAMO 200",
    "profile_name": "V-RAMO 200",
    "start_station": 4000,
    "end_station": 4532.723465152177,
    "region_count": 6
  },
  {
    "name": "N3 - PAV",
    "alignment_name": "N3",
    "profile_name": "V - N3",
    "start_station": 0,
    "end_station": 22.197036383926985,
    "region_count": 1
  },
  {
    "name": "N4 - PAV",
    "alignment_name": "N4",
    "profile_name": "V - N4",
    "start_station": 0.3131068686110622,
    "end_station": 22.434295758564023,
    "region_count": 1
  }
]
```

This confirms the full read path: baselines resolve their parent alignment and profile by name, region ranges are readable, applied assemblies resolve by name, and baseline station ranges derived from regions match the region extents.

## Not Found Case

```json
{
  "success": false,
  "errorCode": "CorridorNotFound",
  "errorMessage": "Corridor was not found in the target drawing."
}
```

## Cleanup

The drawing copy was closed without saving. The Civil 3D process accepted `Quit()` but stayed alive, matching earlier phases, and the test PID was force-stopped under the user's temporary permission. No `acad.exe` remained running.

## Safety Boundary

Both corridor tools are READ/Low:

- no DWG mutation
- reads run inside a read-only AutoCAD transaction
- `Corridor.Rebuild()` is never called, so no corridor is reprocessed by these tools
- `SetTargets` and `ExportSolids` are never called
- the drawing copy, not the original, was opened
