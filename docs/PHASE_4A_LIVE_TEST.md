# Phase 4A Live Test

Date: 2026-09-12

Scope: installed Autodesk bundle autoload and one READ/Low Survey geometry tool against Civil 3D 2026.

## Environment

- Product: Autodesk Civil 3D 2026
- Reported product version: `25.1.0.0`
- Reported `ACADVER`: `25.1s (LMS Tech)`
- Bundle: `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle`
- Bridge process: clean Civil 3D launch with the installed bundle autoloaded
- Active document: default metric template `_Autodesk Civil 3D (Metric) NCS.dwt`

## Bundle Validation

The installed bundle passed offline diagnostics:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-bundle-install.ps1 -AsJson
```

The diagnostic required and found:

- `Civil3D.HybridMcp.Bridge.dll`
- `Civil3D.HybridMcp.Bridge.deps.json`
- `Civil3D.HybridMcp.Bridge.runtimeconfig.json`
- `Civil3D.HybridMcp.Core.dll`
- `Civil3D.HybridMcp.Diagnostics.dll`
- `Civil3D.HybridMcp.Protocol.dll`
- `Civil3D.HybridMcp.Sdk.dll`
- `Civil3D.HybridMcp.Tools.AutoCAD.dll`
- `Civil3D.HybridMcp.Tools.Civil.dll`
- `Civil3D.HybridMcp.Tools.Survey.dll`

No Autodesk managed assemblies were packaged.

## Live Status

`civil3d_status` succeeded through the installed bridge and reported:

- `survey: true`
- `surveyCom: false`
- `survey_enabled: true`
- `survey_geometry_enabled: true`
- `pipe_listening: true`
- `endpoint_registered: true`
- 22 advertised tools

The manifest included:

- `survey_bearing_to_azimuth`
- `survey_azimuth_to_bearing`
- `survey_distance`
- `survey_point_by_azimuth`

## Live Tool Execution

The bridge executed `survey_bearing_to_azimuth` through JSON-RPC over the live named pipe.

Input:

```json
{
  "bearing": "N 45 0 0 E"
}
```

Result summary:

```json
{
  "success": true,
  "tool": "survey_bearing_to_azimuth",
  "azimuth_degrees": 45,
  "bearing": {
    "north_south": "N",
    "angle_degrees": 45,
    "east_west": "E",
    "text": "N 45 E"
  },
  "warnings": []
}
```

## Notes

- No write, destructive, COM, Survey Database, or FBK operation was executed.
- The first launch attempt produced an AutoCAD modal error because the `/ld` path was not quoted correctly in the local test command. That temporary process was closed, then Civil 3D was relaunched with quoted arguments.
- The clean test process was closed after validation to avoid leaving the installed bridge DLL locked.
