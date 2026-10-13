# Phase 2D Live Test - Civil 3D Profile Reads

Date: 2026-09-13

Scope: validate the installed Phase 2D Autodesk bundle against Civil 3D 2026 with the new read-only `profile_list`, `profile_get`, and `profile_elevation_at_station` tools.

## Result

Status: Passed.

The installed bundle advertised 30 tools, including all three profile tools. Every tool was exercised through the live named pipe against a real production-grade drawing containing 32 alignments and 59 profiles.

Unlike the Phase 4E-C Survey network read, this phase had real data available, so the full read path was exercised end to end.

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
copy   : artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_profile_read_20260913-195627.dwg
```

Only the copy was opened in Civil 3D. After the test the source file retained its original timestamp, confirming it was untouched.

## Civil 3D Session

Launch command shape:

```powershell
Start-Process -FilePath "C:\Program Files\Autodesk\AutoCAD 2026\acad.exe" -ArgumentList '/ld "C:\Program Files\Autodesk\AutoCAD 2026\AecBase.dbx" /p "<<C3D_Metric>>" /product C3D /language en-US "<drawing copy>"'
```

The launched test instance was PID 25508. No other `acad.exe` was running before the test.

## Tool Manifest

The live `tools/list` response reported:

```json
{
  "toolCount": 30,
  "profileToolNames": ["profile_list", "profile_get", "profile_elevation_at_station"]
}
```

## profile_list

The drawing reported 32 alignments and 59 profiles. Representative records:

```json
[
  {
    "name": "TN_DRONE (27)",
    "alignment_name": "BD - 100",
    "profile_type": "EG",
    "starting_station": 2000,
    "ending_station": 2357.826134321125,
    "elevation_min": 268.09701927648587,
    "elevation_max": 289.2248595500354,
    "pvi_count": 201,
    "style_name": "_MP L Terreno",
    "handle": "1124DD"
  },
  {
    "name": "V - BD - 100",
    "alignment_name": "BD - 100",
    "profile_type": "OffsetProfile",
    "starting_station": 2000,
    "ending_station": 2357.8261343206955,
    "elevation_min": 268.2474089048512,
    "elevation_max": 289.73826184928635,
    "pvi_count": 17,
    "style_name": "_MP V OFFSET",
    "handle": "1124BE"
  }
]
```

This confirms that profiles are discovered through their parent alignments, that the parent alignment name is carried on every record, and that `ProfileType`, station range, elevation range, PVI count, and style name are all readable.

## profile_get

A handle-based lookup returned the expected profile:

```json
{
  "success": true,
  "name": "TN_DRONE (27)",
  "alignment_name": "BD - 100"
}
```

## profile_elevation_at_station

Queried at the midpoint of the resolved profile's station range:

```json
{
  "success": true,
  "profile_name": "TN_DRONE (27)",
  "station": 2178.9130671605626,
  "elevation": 280.85562581193705,
  "grade": -0.2159187945150072
}
```

Both `ElevationAt` and `GradeAt` returned values, so grade is reported alongside elevation.

## Not Found Case

Requesting a profile name that does not exist returned a structured error instead of an empty success:

```json
{
  "success": false,
  "errorCode": "ProfileNotFound",
  "errorMessage": "Profile was not found in the target drawing."
}
```

## Ambiguity Policy

Profile names are only unique within a parent alignment, so the same name can appear under several alignments. When a name matches more than one profile and no `alignment_name` or `handle` is supplied, the tools return `PROFILE_AMBIGUOUS` and list the competing alignments rather than silently picking one. This mirrors the platform rule against acting on an ambiguous target.

That branch is covered by unit tests; the live drawing happened to resolve the tested lookups uniquely by handle.

## Cleanup

The drawing copy was closed without saving.

The launched Civil 3D process accepted `Quit()` but remained alive afterwards, matching earlier phases. The test PID was force-stopped under the user's temporary permission for plugin installation and validation cleanup, and no `acad.exe` remained running at the end of the test.

## Safety Boundary

All three profile tools are READ/Low:

- no DWG mutation
- no document lock or transaction commit of changes
- reads run inside a read-only AutoCAD transaction
- no profile creation, edit, or deletion
- the drawing copy, not the original, was opened
