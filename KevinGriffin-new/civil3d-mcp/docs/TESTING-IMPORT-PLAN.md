# import_plan_json — live Civil 3D test script (Windows laptop)

First live exercise of the M3 consumer. Logic is dry-run-verified against a
stubbed bridge; this session tests the real seam: bridge entity creation,
layers, and — the payoff — `create_parcels_from_objects` turning a
closure-validated ring into an actual parcel.

Setup: build/NETLOAD the bridge (BUILD.md), run `mcp-server/server.py`,
attach your MCP client. Fresh drawing, imperial template.

Fixtures in `testdata/` (produced by plat2json's `cogo_assemble.py`):

| fixture | what it is | exercises |
|---|---|---|
| `square.plan.json` | synthetic 100×100 ft parcel, closure exact | ref lines + closed COGO polyline |
| `square_flagged.plan.json` | same plan, `needs_human: true` | the refusal gate |
| `ncdot_alamance_reference.plan.json` | real NCDOT sheet, 410 edges, no rings | reference-only volume (410 bridge calls!) |
| `county_test_flagged.plan.json` | real subdivision, 568 edges, 2 rings BOTH beyond tolerance | gate + ring-skip logic |

Use ABSOLUTE Windows paths in tool calls.

## T0 — environment
`ping`, `get_drawing_info`. Expect the bridge to answer from inside Civil 3D.

## T1 — happy path (square)
`import_plan_json(path=<testdata>/square.plan.json, seed_easting=5000, seed_northing=5000)`

Expect: `status: ok`, `reference_lines: 4`, `cogo_rings: 1`, layers
PLAN_REF + PLAN_COGO, and ONE closed polyline on PLAN_COGO — vertices
(5000,5000) (5000,5100) (5100,5100) (5100,5000), exact.

## T2 — the refusal gate
1. Plain call on `square_flagged.plan.json` → `status: refused`, nothing drawn.
2. Retry with `allow_flagged=True` → `status: ok`, draws as T1.

## T3 — THE MILESTONE: ring → parcel
After T1 (or T2b):
1. `create_parcels_from_objects` targeting the PLAN_COGO polyline
   (`get_entity_handles` on PLAN_COGO to find it).
2. `list_parcels` → expect ONE new parcel, area 10,000 sq ft (0.2296 ac).
That's the full plat→validated-parcel loop: published numbers in, Civil 3D
parcel out, closure-proven in between. Screenshot it.

## T4 — real sheet, reference-only (alamance)
`import_plan_json(path=<testdata>/ncdot_alamance_reference.plan.json)`

Expect `status: ok`, `reference_lines: 410`, `cogo_rings: 0`, rotation
−44.826°, scale 0.694575. This makes 410 sequential `draw_line` bridge calls
— time it. If it's painfully slow the finding is "bridge needs a draw_lines
batch action" (a good, small Civil3dMcpBridge.cs addition).

## T5 — real sheet, flagged with bad rings (county)
1. Plain call → `status: refused`.
2. `allow_flagged=True` → `status: ok`, `reference_lines: 568`,
   `cogo_rings: 0`, result `rings` shows 2 entries with `cogo_drawn: false`
   (misclosures ~150/~380 ft — the closure gate keeping bad geometry off
   PLAN_COGO even on a force-draw).

## Record per test
Returned JSON verbatim, wall-clock, `_dialogs` payloads if any surface, and
parcel screenshots for T3. T1–T3 failures are code bugs (server.py
import_plan_json); T4 slowness is a bridge-batching feature request.
