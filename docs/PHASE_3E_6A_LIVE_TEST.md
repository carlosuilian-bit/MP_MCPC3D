# Phase 3E / 6A Live Test

Date: 2026-09-19

Host: Autodesk Civil 3D 2026, installed bundle, 44-tool catalog.

Drawings: copies, never originals.

- `Way-153_25-179_phase3E6A_20260919-135120.dwg` for the property-set write
- `TBD-25-009_GEOMETRICO_R2_roadway_20260919-*.dwg` for the roadway batch

Two drawings were needed because neither carries both: the Way-153 copy has the
real `Dados_Rodovia` sets and no alignments; the TBD copy has 32 alignments and
no property sets.

## Result

| Phase | Tool | Result |
| --- | --- | --- |
| 3E | `propertyset_set` | Pass, after one defect was found and fixed |
| 6A | `roadway_station_offset_batch` | Pass |

## Defect found before the run: the token never reached the caller

`propertyset_set` refused without a confirmation, as designed, but the token was
created inside the bridge and never returned. The caller was told to confirm and
given nothing to confirm with, which made the flow unusable end to end.

Fixed by returning the token with the refusal: `IConfirmationGate.Evaluate` now
returns a `ConfirmationEvaluation` carrying the issued token, `CivilToolException`
carries structured `Details`, and the `CONFIRMATION_REQUIRED` error ships the
preview, the affected count and the token. Covered by
`TheRefusalCarriesBackTheTokenTheCallerMustConfirmWith`.

Without this fix the live test could not have completed, which is the clearest
possible argument for running it.

## Phase 3E: the confirmation flow on real BR-153 data

Object `10781C`, property set `Dados_Rodovia`, on a copy.

Before:

```json
{ "name": "Secao", "data_type": "Text", "value": "529", "is_read_only": false }
```

Call without a token:

```text
success=False code=ConfirmationRequired
Update 1 value(s) in property set 'Dados_Rodovia' on object 10781C
in 'Way-153_25-179_phase3E6A_20260919-135120.dwg': Secao: '529' -> '529-TESTE-MCP'
```

Nothing was written. The preview names the drawing, the object, the set and the
before/after pair, and the token travelled back in `error.details`.

Call with that token:

```json
{
  "applied": true,
  "changed_count": 1,
  "properties": [
    { "name": "Secao", "data_type": "Text",
      "previous_value": "529", "new_value": "529-TESTE-MCP", "changed": true }
  ],
  "transaction": { "document_locked": true, "transaction_started": true,
                   "committed": true, "rolled_back": false }
}
```

A read-back confirmed the drawing now holds `529-TESTE-MCP`.

**The no-op guard fired live.** Running the same write a second time, with the
value already applied, returned:

```text
code=InvalidRequest
No value would change on property set 'Dados_Rodovia'; nothing to confirm or write.
```

No confirmation was requested, because there was nothing to approve. That is the
rule that keeps an operator from learning to approve without reading.

This is the first time the platform has changed a value an engineer put in a
drawing, and the first use of the confirmation model that had sat in Core since
Phase 1 with no consumer.

## Phase 6A: the batch survives a bad point

Alignment `BD - 100`, stations 2000 to 2357.826.

A point was obtained from `alignment_point_location` at the mid station
2178.913067160348, then fed back through the batch together with a point far off
the alignment:

```json
{
  "count": 2, "resolved_count": 1, "failed_count": 1,
  "points": [
    { "index": 0, "label": "sobre o eixo", "resolved": true,
      "station": 2178.913067160337, "offset": 0, "error": null },
    { "index": 1, "label": "longe do eixo", "resolved": false,
      "station": null, "offset": null,
      "error": "Could not compute station/offset: PointNotOnEntity," }
  ]
}
```

Three things this proves:

- the batch is not all-or-nothing: one point failed and the other still returned;
- the geometry round-trips. Station in, coordinates out, coordinates back in, and
  the station returns agreeing to the eleventh decimal. That is the consequence
  of reusing the live-validated `alignment_station_offset` through a narrow port
  instead of writing a second implementation;
- labels are echoed back untouched, so a caller can match rows.

A request naming a missing alignment failed once with `ALIGNMENT_NOT_FOUND`,
before any point was touched.

## Modal handling, now routine

Both sessions raised `References - Not Found Files`, and the TBD session also
raised `Drawing Recovery`. Each was read, decided with scope `Once`, and
dismissed through `dismiss_dialog`. No human touched the machine, and no decision
persisted: the store was `[]` afterwards.

This is the first run where the modals were a step in the script rather than a
blocker, which is what Phases 1D through 1F were for.

## Findings worth carrying forward

**Field names in responses are not guessable.** The client assumed
`start_station` and `end_station`; the contract emits `starting_station` and
`ending_station`. Combined with the camelCase/snake_case split already recorded
in `docs/PHASE_1F_LIVE_TEST.md`, this is the second time response naming has cost
a debugging cycle. Worth one deliberate pass over the published surface.

**No tool finds objects that carry a property set.** The handle used here came
from the Phase 2F report, not from the platform. `propertyset_list` takes a
handle, so locating carriers means sweeping every object. A filter tool would
make `propertyset_set` usable at scale, which is what a roadway batch write will
need.

## Closing state

- No `acad.exe` left running.
- Both source drawings kept their original timestamps; only copies were opened.
- The dialog decision store was `[]`; all decisions were one-time.
- Full solution: 238 tests, 0 failures.
- Bridge Release build: 0 errors.
