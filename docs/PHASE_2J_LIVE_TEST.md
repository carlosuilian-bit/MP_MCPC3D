# Phase 2J Live Test - Block Reads, Both Project Moments

Date: 2026-09-24

Scope: validate `block_list`, `block_reference_list` and `block_get_attributes`
against Civil 3D 2026 on copies of the reference pair: the geometric drawing and
the exported drawing of the same project. The claim under test is the one that
justified prioritising these tools: **blocks are plain AutoCAD entities, so
block reads keep answering after a project is exported**, unlike the 15 Civil 3D
reads that go silent there.

## Result

Status: **Passed on both drawings.**

Proven live:

- all three tools answer in both moments, with real data;
- a dynamic insert is found by its real name while its definition is anonymous;
- the result limit and the scan budget both cut the list and say which one did;
- a handle that is not a block insert fails structurally and names the real class;
- an unknown handle fails with `ObjectNotFound`;
- a sweep of 38,179 entities completes in 68 ms, because only block inserts are opened.

Not proven live, and why:

- **Constant attributes.** The rule is unit tested, but neither drawing has a
  block whose attribute definitions include a constant one, so the live path
  never ran. Neither drawing offered a candidate.
- **Inserts nested inside other block definitions** are not listed, by design:
  the sweep reads layouts. This is a scope decision, not a gap found live.

## Runtime Incident Found Before Testing

The first build of this phase failed before reaching any new code. A Civil 3D
2026 update on 2026-09-22 moved the Autodesk host from .NET 8 to .NET 10, and
every project referencing Autodesk assemblies stopped compiling with CS1705.
Retargeting those projects, fixing the packaging script, and correcting the
bundle manifest are recorded in `docs/decisions/ADR-0009-net10-autodesk-host.md`.

This test is therefore also the first live proof that the .NET 10 bridge loads
in the updated host.

## Installed Bundle

Packaged with the corrected `scripts/package.ps1`, which now reads the target
framework from the bridge project, from
`src\Civil3D.HybridMcp.Bridge\bin\Release\net10.0-windows`. Installed to
`%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle`. Offline
diagnostic: `success: true`, no issues, no warnings, including the new
`SeriesMin="R25.1"` check. Live catalog: **49 tools**.

## Test Drawings

Copies only. Originals hashed before and after:

```text
geometric source : C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg
                   md5 d589e77f548f2fdc3155d1b84002ee8c   (before = after)
geometric copy   : artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_blocks_20260924-133933.dwg

exported source  : DNT-393RJ-172+112-173+235-ACE-PIT-DE-V2-001-R01.dwg
                   md5 d07ec447c5a009a6baebafb197b352e1   (before = after)
exported copy    : artifacts\live-tests\DNT-393RJ_EXPORTADO_blocks_20260924-133933.dwg
```

## Results

| | Geometric | Exported |
|---|---:|---:|
| Block definitions, default view | 257 | 271 |
| Block definitions, all | 917 | 2,131 |
| of which anonymous | 657 | 1,847 |
| of which layouts | 3 | 13 |
| `block_list` time | 331 ms | 76 ms |
| Entities scanned | 4,217 | 38,179 |
| Block inserts, default view | 104 | 1,961 |
| of which dynamic | **66** | **1,372** |
| `block_reference_list` time | 71 ms | 68 ms |
| Layouts holding inserts | 3 | 13 |

The exported drawing scans nine times more entities than the geometric one, in
the same time. This is the class filter doing its job: `ObjectId.ObjectClass` is
checked before anything is opened, so the thousands of exported hatches are
never opened.

### Dynamic blocks are the majority, not the exception

66 of 104 inserts in the geometric drawing, and 1,372 of 1,961 in the exported
one, are dynamic. Each points at an anonymous definition. A filter on the
definition name would have returned an empty list and looked like a correct
answer for most of the blocks in both drawings.

Live evidence that the effective name resolves:

```text
geometric: block_name=_MP ID Planta  -> 2 inserts, definitions *U185, *U897
exported : block_name=PLACA KM 173   -> 4 inserts, definitions *U179, *U180, *U181, *U182
```

### Limits

```text
exported: include_anonymous=true -> count 2000, truncated true, truncated_by result_limit
both    : max_scan=100            -> scanned 100, truncated true, truncated_by scan_budget
```

The result limit fired on real data, not only in a unit test.

### Attributes

```text
geometric: D7D90 _MP Marco Estacao   MPE = "MPE 92"
exported : D7D90 _MP Marco Estacao   MPE = "MPE 01"
```

`block_reference_list` with `include_attributes` returned the same attributes
inline for the same block in both drawings.

### Negative paths

```text
geometric: block_get_attributes(BCC31)    -> InvalidRequest: "Handle 'BCC31' is a AcDbBlockTableRecord, not a block reference."
exported : block_get_attributes(249687)   -> InvalidRequest, same wording
both     : block_get_attributes(DEADBEEF) -> ObjectNotFound
```

## Observations Worth Keeping

Recorded as observations, not conclusions.

**Civil 3D label components survive the export as blocks.** The exported
drawing carries `AeccTickLine` x202 and `AeccTickCircle` x32 as ordinary block
inserts. What was a label component in the geometric moment is a readable block
in the exported one.

**The same handle carries the same block in both drawings, with a different
value.** `D7D90` is a `_MP Marco Estacao` insert in both files, with attribute
`MPE 92` in the geometric drawing and `MPE 01` in the exported one. Either the
export preserves handles and the value was changed between these two revisions,
or the match is coincidental. If handles do survive the export, they would be a
tracking anchor between the two moments stronger than layer names. Not
investigated further here; worth a deliberate check.

**Station and kilometre marker blocks already exist in both drawings.** The
geometric drawing has `_MP Km Via` x3 and `_MP Marco Estacao` x3; the exported
one has `_MP Km Via` x36 and `_MP Marco Estacao` among its inserts. Recorded as
fact only: `roadway_create_km_marker` remains paused by operator decision, and
this is not a proposal to resume it.

**A dialog cleared without intervention.** Opening the exported copy raised
`Drawing Recovery`, a consequence of the previous test instance being closed
forcibly. Between two reads about a minute apart it disappeared with no
decision recorded and no button pressed by the platform; the decision store
stayed `[]`. Whether a person closed it or AutoCAD did is not known.

**Console encoding.** Block names with accents print garbled in the test console
(`Esta??o`). The data is intact: the name returned by the server was sent back
as a filter and matched the three inserts, so the string survives the transport.

## Cleanup

Both Civil 3D instances were started by this test and terminated at its end
(PIDs 31304 and 13152). No `acad.exe` was left running. Neither copy was saved.
Originals unchanged by md5. Dialog decision store `[]`.
