# Phase 1D / 1E / 1F / 2G Live Test

Date: 2026-09-17

Host: Autodesk Civil 3D 2026, installed bundle, 42-tool catalog.

Drawing: a copy, never the original —
`artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_dismiss_20260917-002244.dwg`.

This run validated four phases that had accumulated without live coverage, and
each one unblocked the next.

## Result

| Phase | Tool | Result |
| --- | --- | --- |
| 1D | `list_dialogs` content read | Pass |
| 1E | `dialog_decision_list`, `dialog_decision_set` | Pass |
| 1F | `dismiss_dialog` | Pass, after two defects were found and fixed |
| 2G | `object_get`, `object_properties` | Pass |

## Phase 1D: reading the blocking dialog

`list_dialogs` answered while the host was blocked and, for the first time, read
the buttons:

```json
{
  "blocked_by_modal": true,
  "blocking_dialog_title": "References - Not Found Files",
  "buttons": [
    { "text": "Open the External References palette", "control_id": 0, "is_enabled": true },
    { "text": "Ignore unresolved reference files", "control_id": 0, "is_enabled": true }
  ],
  "content_read": true
}
```

The docked `Properties` palette was correctly classified as not a modal
candidate, and its own buttons were read with real control ids (32788-32790).

`text` came back null for both modals. These dialogs carry no `Static` control
with a caption, so there is no message to read. Reported as null rather than
invented.

## Defects found by this run, both in Phase 1F

**1. Control id 0 made the click unaddressable.** Both buttons of
`References - Not Found Files` report `control_id: 0`, while the palette's
buttons report real ids. `Win32DialogActuator` posted `WM_COMMAND` addressed by
control id, which cannot tell two id-0 buttons apart. Fixed by posting `BM_CLICK`
to the button's own handle, which is unambiguous regardless of how a dialog
numbers its controls.

**2. A posted click was reported as success.** `PostMessage` returns true when
the message is queued, not when it is acted on. The tool would have reported
`pressed: true` for a click the dialog ignored, telling the operator the host was
unblocked while it was still stuck. Fixed by adding `IDialogActuator.WaitUntilClosed`,
polling `IsWindow` until the dialog is gone, and failing with
`DialogDismissFailed` when it is still open. Covered by
`AClickThatLeavesTheDialogOpenIsReportedAsFailure`.

Neither defect was reachable offline. Both are exactly what live validation is
for.

## Phase 1E and 1F: decide, then press

`dialog_decision_set` with scope `Once`:

```json
{ "button_text": "Ignore unresolved reference files", "scope": "Once", "persisted": false, "count": 1 }
```

`persisted: false` is the scope contract holding: a one-time answer never reaches
disk.

`dismiss_dialog` then returned `pressed: true`, `outcome: "Resolved"`, and the
following `list_dialogs` reported `blocked_by_modal: false` with
`main_window_enabled: true`. The host unblocked itself without anyone touching
the machine.

**A second modal appeared behind the first:** `Drawing Recovery`, previously seen
in Phase 3B, now with its real button caption read: `&Fechar`. It was decided and
dismissed the same way, and the host unblocked again.

Note the ampersand. `&Fechar` is the caption as Windows reports it, with the
accelerator marker. Decisions match exactly, so a decision for this dialog must
use `&Fechar`, not `Fechar`. Stripping the marker silently would break the
exactness the whole design rests on.

After the run, the decision store was `[]`. Both decisions were `Once` and
neither persisted.

## Phase 2G: reading an object with no typed tool

`object_get` on alignment `BD - 100`, handle `1124B6`:

```json
{
  "object_class": "AeccDbAlignment",
  "dxf_name": "AECC_ALIGNMENT",
  "class_hierarchy": ["AeccDbFeature","AeccDbEntity","AecDbEntity","AcDbCurve","AcDbEntity","AcDbObject","AcGiDrawable","AcRxObject"],
  "is_entity": true,
  "owner_class": "AcDbBlockTableRecord",
  "entity": { "layer": "_MP E. Projeto", "color_index": 2, "color": "ByAci" }
}
```

The eight-level hierarchy is the point of the phase: a Civil 3D class reports
through the same shape as any AutoCAD entity, without the AutoCAD module
referencing the Civil 3D API. Extents came back in real UTM coordinates.

`transparency_alpha` was null. The defensive read caught something rather than
producing a number, which is the intended behaviour.

`object_properties`: **105 properties, `truncated: false`**, read by reflection
over `AeccDbAlignment`. `declaring_type` correctly separated `Alignment`
properties from inherited `DBObject` ones. `name_contains: "sta"` narrowed it to
10, including `StartingStation`, `EndingStation` and `ReferencePointStation`. No
property returned an `error`, so on this object the host evaluated all of them.

`object_get` with handle `DEADBEEF` returned structured `ObjectNotFound`.

## Other findings

**Pending decisions listed a docked palette.** `dialog_decision_list` surfaced
the `Properties` palette as needing a decision. A palette is not a dialog being
presented, and it would bury real decisions in noise. Fixed by restricting
pending decisions to modal candidates, covered by
`PendingDecisionsCoverBlockingDialogsOnlyNotDockedPalettes`.

**Response casing is inconsistent across the platform.** `document_list` returns
camelCase (`documentId`, `isActive`) because the core document contracts carry no
`[JsonPropertyName]`, while the layer, Civil, object and dialog contracts return
snake_case. An MCP client consuming both hits this. Not changed here: it is a
breaking change to the published surface and deserves its own decision.

## Closing state

- No `acad.exe` left running.
- The source drawing kept its 2026-09-11 timestamp; only copies were opened.
- Full solution: 217 tests, 0 failures.
- Bridge Release build: 0 errors, 9 known MSB3277 warnings.
