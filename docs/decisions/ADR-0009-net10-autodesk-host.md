# ADR-0009: Autodesk-Facing Projects Target .NET 10

Date: 2026-09-24

Status: Accepted

## Context

On 2026-09-22 a Civil 3D 2026 update replaced every Autodesk managed assembly on
the validation machine. The product version stayed 25.1 (`acad.exe` reports
R25.1.179), but the host runtime changed:

```text
C:\Program Files\Autodesk\AutoCAD 2026\acdbmgd.runtimeconfig.json
  "tfm": "net10.0"
  Microsoft.NETCore.App 10.0.0, Microsoft.WindowsDesktop.App 10.0.0
```

`acdbmgd`, `accoremgd`, `acmgd` and `AeccDbMgd` now reference
`System.Runtime 10.0.0.0`. A project targeting `net8.0-windows` can no longer
compile against them; the compiler stops with CS1705.

The break was found on 2026-09-24, the first build after the update. It was
confirmed to be independent of any code change: with the new block-read tests
removed from the tree, the solution still failed with 12 CS1705 errors, 4 in
`Tools.AutoCAD` and 8 in `Tools.Civil`. The last green build before that was
2026-09-19.

Nothing in `AGENTS.md` section 17 was violated: the platform was told not to
assume the .NET version, and the Autodesk path was already detected through
`$(AutodeskAcadDir)` rather than hardcoded. The target framework itself was
the one assumption left in place.

## Decision

Retarget only the projects that reference Autodesk assemblies, and the test
projects that reference them, to `net10.0-windows`:

```text
src/Civil3D.HybridMcp.Bridge
src/Civil3D.HybridMcp.Tools.AutoCAD
src/Civil3D.HybridMcp.Tools.Civil
tests/Civil3D.HybridMcp.Tools.AutoCAD.Tests
tests/Civil3D.HybridMcp.Tools.Civil.Tests
```

Everything else stays where it was: Protocol, Core, Sdk, Diagnostics, Server,
Tools.Survey, Tools.Roadway and their tests remain on `net8.0` /
`net8.0-windows`. A .NET 10 project references .NET 8 libraries without
difficulty, the MCP Server runs as its own process outside AutoCAD, and the pure
modules gain nothing from moving.

Consequential changes made in the same step:

- `scripts/package.ps1` read the bridge output from a hardcoded
  `bin\<Configuration>\net8.0-windows` path. It now reads `TargetFramework`
  from the bridge project and fails loudly if the build fails or the bridge DLL
  is missing. Before, a retarget would have packaged an empty or stale folder
  without any error.
- `PackageContents.xml` declared `SeriesMin="R25.0"`, which covers Civil 3D
  2025. A .NET 10 assembly cannot load into a .NET 8 host, so that declaration
  became false. It is now `SeriesMin="R25.1"`, and
  `scripts/check-bundle-install.ps1` checks for the new value.

## Alternatives

**Retarget the whole solution to .NET 10.** Uniform, but moves the MCP Server
and every pure module for no benefit, and widens the blast radius of a change
forced by one dependency.

**Keep `net8.0-windows` and suppress CS1705.** Not possible: CS1705 is an error
with no supported suppression, and even if it compiled, the bridge would be
built against reference assemblies older than the host it loads into.

**Multi-target `net8.0-windows` and `net10.0-windows` to keep Civil 3D 2025.**
Would need the 2025 Autodesk assemblies on the build machine, which are not
installed, and a second live validation host. `AGENTS.md` section 17 asks for
2025 compatibility only when it is simple and does not compromise the project;
it is neither here. Revisit if a 2025 host becomes a real requirement.

## Consequences

- The bundle now requires a Civil 3D 2026 install that includes the 2026-09-22
  update. `SeriesMin`/`SeriesMax` cannot express an update level inside a
  series, so an un-updated 2026 install still matches the manifest and would
  fail to load the bridge. Recorded here because the manifest cannot say it.
- Civil 3D 2025 is no longer supported by the packaged bundle.
- Verified: solution builds with 0 errors (the 9 known MSB3277 warnings
  remain), 264 tests green before the block reads were added and 293 after,
  AutoCAD and Civil tests running on `net10.0`, bundle packaged from the
  `net10.0-windows` output, offline diagnostic `success: true`, and the bridge
  **loaded and served tools live** in the updated Civil 3D 2026 on 2026-09-24.
- A future Autodesk update may move the runtime again. The symptom will be the
  same CS1705 with a newer `System.Runtime` version; the fix is the same
  retarget. See `docs/TROUBLESHOOTING.md`.
