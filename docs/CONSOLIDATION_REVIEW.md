# Consolidation Review

Date: 2026-09-20

Status: **audit only, no code changed.** This reconciles the geometric/exported
finding recorded in `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md` with what the
platform actually ships today, and lists what must change and what needs an
operator decision.

Everything below was measured against the current tree, not recalled.

## The 46 tools, classified by moment

The platform ships 46 tools. Until now they were classified by module. Against
the two moments they fall into four groups, and the split is not even.

**Dead in the exported moment (15).** These read Civil 3D objects that no longer
exist once the project is exported.

```text
alignment_list                surface_list             corridor_list
alignment_get                 surface_get              corridor_get
alignment_station_offset      surface_elevation_at_xy  profile_list
alignment_point_location      cogo_list                profile_get
roadway_station_offset_batch  cogo_get                 profile_elevation_at_station
```

**Empty in both moments, in this project (7).** The property set family. It works
correctly; there is simply nothing attached to read. See the measured evidence in
the phases document: 0 of 4146 objects carried a set in the geometric drawing.

```text
propertyset_definition_list  propertyset_find  roadway_propertyset_write
propertyset_list             propertyset_set
propertyset_get              propertyset_numeric_test_create
```

**Work identically in both moments (15).**

```text
civil3d_ping    layer_list         object_get         list_dialogs
civil3d_status  layer_get          object_properties  dialog_decision_list
document_list   layer_create                          dialog_decision_set
drawing_info    layer_update                          dismiss_dialog
                layer_set_current
```

**Independent of the drawing moment (9).** Survey: pure geometry maths, file
parsing, or the Survey database.

```text
survey_bearing_to_azimuth  survey_fbk_dry_run        survey_project_list
survey_azimuth_to_bearing  survey_database_status    survey_network_list
survey_distance            survey_working_folder_get
survey_point_by_azimuth
```

Two observations follow, and the second is the useful one.

**22 of 46 tools return nothing useful on an exported drawing** — the 15 dead
ones plus the 7 property set ones.

**The tool that reads the exported moment already exists.** It is `layer_list`.
The semantics of an exported drawing live in the layer names, and the platform
has been able to read them since Phase 1 without anyone noticing that is what
they were for. The exported moment does not need a new subsystem; it needs tools
that consume a declared role-to-layer map on top of reads that already work.

## Defects, ranked

### D1. A drawing in the exported moment is indistinguishable from an empty one

Severity: **high.** This is the sharpest consequence of the finding.

`alignment_list` on an exported drawing returns `count: 0`. So does
`alignment_list` on a geometric drawing that genuinely has no alignments. The
responses are identical. An agent cannot tell "this project has no axis" from
"this project's axis is a polyline now", and both readings lead somewhere
different.

The 15 dead tools all fail this way: silently, by returning a valid empty answer.
Nothing errors, so nothing prompts a second look.

This is the argument for the moment detector being cheap and early, not a
refinement for later. Until it exists, every empty Civil read is ambiguous.

### D2. Response naming is split roughly down the middle

Severity: **high.** Flagged twice before from live testing; now root-caused and
quantified.

`JsonContractOptions` builds on `JsonSerializerDefaults.Web`, whose naming policy
is camelCase. So every contract property **without** an explicit
`[JsonPropertyName]` serializes as camelCase, while the ones that carry an
attribute are snake_case.

Approximate counts of public contract properties against explicit attributes:

| File | properties | with attribute | without (camelCase) |
|---|---|---|---|
| `Contracts.cs` | 172 | 14 | **158** |
| `CivilContracts.cs` | 298 | 181 | 117 |
| `SurveyContracts.cs` | 133 | 81 | 52 |
| `LayerContracts.cs` | 84 | 54 | 30 |
| `RoadwayContracts.cs` | 40 | 15 | 25 |
| `DialogDecisionContracts.cs` | 39 | 16 | 23 |
| `ObjectContracts.cs` | 57 | 40 | 17 |
| `Confirmation.cs` | 2 | 0 | 2 |

Counts are approximate: the property grep may include members that are never
serialized. The ratio is the point, not the digits.

`Contracts.cs` is the worst case and the most visible, because it holds
`DocumentDescriptor` — which every tool response embeds. It emits `instanceId`,
`documentId`, `isActive` while the module contracts beside it emit
`starting_station`, `property_set`, `changed_count`.

Both live client bugs in this project came from guessing the wrong side of this
split. A caller has no rule to follow, because there is no rule.

This blocks nothing today but taxes every future tool, and every response shape
added before it is fixed is another one to migrate.

### D3. The confirmed-write pipeline is duplicated, and has already cost two bugs

Severity: **medium.**

Two call sites build the gate:

- `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs:750`
- `src/Civil3D.HybridMcp.Bridge/RoadwayToolHandler.cs:163`

Two services handle the decision switch:

- `src/Civil3D.HybridMcp.Tools.Civil/PropertySetWriteService.cs`
- `src/Civil3D.HybridMcp.Tools.Roadway/RoadwayPropertySetWriteService.cs`

Two copies have already produced two live-only defects, both in the second copy:
the confirmation token was not returned to the caller, and the HMAC service was
constructed per call so the issued token never validated. Both were fixed in
Phase 6B, both were avoidable.

A third confirmed-write tool will repeat this unless the pattern is extracted
first. It has earned extraction: the evidence is two for two.

### D4. `roadway_propertyset_write` is in the wrong module under the wrong name

Severity: **low**, but the cost grows with every day it ships.

The service takes `set_name`, handles and name/value pairs. It contains no
roadway concept. It is in the Roadway module because that is where it was built,
during a phase whose only sample data happened to be road data.

| Today | Should be |
|---|---|
| `roadway_propertyset_write` | `propertyset_set_batch`, Civil module |
| `roadway_propertyset_read` (planned) | `propertyset_get_batch`, Civil module |

Renaming one shipped tool is cheap. Renaming a family later is not.

### D5. `docs/TOOL_CATALOG.md` was one tool behind — **fixed 2026-09-19**

Severity: low. Fixed during this review, since it was documentation only.

The catalog documented 45 tools; the platform ships 46. `roadway_propertyset_write`
was never added after Phase 6B. It is now documented, with its name-under-review
note pointing at D4.

Three further staleness defects were found and fixed in the same pass:

- `propertyset_find` and `propertyset_set` still read "not yet live validated".
  Both were live validated on 2026-09-19.
- `roadway_station_offset_batch` read the same. It was live validated on
  2026-09-19 and round-tripped to 11 decimals against `alignment_station_offset`.
- The catalog header claimed Phase 2G was not live validated. It was — inside
  `docs/PHASE_1F_LIVE_TEST.md`, which is why no `PHASE_2G_LIVE_TEST.md` exists.
  The handoff carried the same wrong claim in two places.

The lesson worth keeping: **a per-tool validation line and a document-level
summary paragraph will drift.** The per-tool lines are now declared authoritative
and the header says so.

## Operator decisions — answered 2026-09-19

**R1. Is `F-HZ` / `F-VT` / `F-SC` your standard, or this client's?**

**Answered: it is the template standard, pre-configured somewhere in the geometric
drawing.** The operator asked where it lives so it can be standardised later.

**Found.** It is a Civil 3D drawing setting, not a hand-authored list:

```text
CivilDocument.Settings -> DrawingSettings -> ObjectLayerSettings
  -> GetObjectLayerSetting(SettingsObjectLayerType) -> SettingsObjectLayer
       .ObjectType  .LayerName  .Modifier  .ModifierValue
```

`SettingsObjectLayerType` enumerates roughly 45 object types including the
labelling variants, and they correspond closely to the observed export:
`Alignment` to `F-HZ-EIXO`, `AlignmentLabeling` to `F-HZ-ESTACA`, `Profile` to
`F-VT-GREIDE`, `SampleLine` to `F-HZ-SECAO`.

Confirmed from `AeccDbMgd.dll` metadata, not recalled. Detail in
`docs/DATA_CARRIERS.md`.

**Consequence: the role-to-layer map never has to be typed.** It is read out of
the geometric drawing. That satisfies the never-hardcode rule and removes the
transcription step at the same time. A read-only tool returning this map is now
the cheapest useful step toward the exported moment — cheaper than D1's detector,
and it partly answers the same need.

Caveat: object layers cover Civil 3D object types only. `F-HATCH-PISTA-EXISTENTE`
is a hatch, not a Civil object, and will not appear there. The setting gives the
backbone of the map, not all of it.

**R2. Do property sets survive *your* export when they actually carry values?**

**Answered: unknown, must be tested.** Agreed, and the test has been widened.

It should measure three carriers in one export round, not one: Property Set,
Object Data and XData, attached to the same hatch, read back after the export.
Running it three times would cost three exports for the same information.

**Opened as Phase 3F** at the operator's request. Full procedure in
`docs/NEXT_AGENT_HANDOFF.md` under `## Phase 3F`, analysis in
`docs/DATA_CARRIERS.md`.

Note the platform gap the phase exposes: property sets have seven validated
tools, while Object Data and XData have **no read and no write support at all**.
So stamping the three carriers needs either a throwaway probe or minimal
adapters. A throwaway probe is the cheap path — the phase exists to measure, not
to ship tools.

**R3. Should property sets start carrying data at all?**

**Answered: not necessarily — it depends on whether they enable real calculation.**
The operator gave the case: pavement data on a hatch, CBUQ thickness, base and
sub-base thickness, for calculation and analysis. And asked for a comparison of
Property Set against Object Data.

**Analysis done, in `docs/DATA_CARRIERS.md`. Recommendation: Property Set for the
geometric moment, and it is not close.**

The deciding factor is a member confirmed in `AecPropDataMgd.dll`:
`PropertyDefinition.Automatic` with `SetAutomaticData` / `GetAutomaticData`. **A
property can derive its own value from the object it is attached to.** The
pavement case is calculation, and calculation needs area times thickness.

**Corrected 2026-09-20.** The first version of this said that for a hatch the
derived value "includes its area". That was inferred from the API existing and
was never verified. Research the operator supplied establishes that available
automatic properties are **hard-coded per object type** and cannot be extended
through the interface — with a worked counterexample, the 3D area of a TIN
Surface, which is *not* available. So whether hatch area is exposed is now an
open question, listed first in `docs/DATA_CARRIERS.md` under "Still to verify".
If it is not exposed, a formula property or a platform-written value takes its
place; the recommendation survives, but it costs more than stated.

Secondary: property sets are unit-aware, so a thickness is a length rather than a
bare number; and the platform already ships seven live-validated property set
tools while Object Data support is zero.

For the **exported** moment the carrier stays undecided on purpose — Property Set
is measured dead there and the other two are unmeasured. Phase 3F settles it.

**Operator follow-up, and it changes the framing:**

> Object Data pode ser exportado, inclusive para arquivos KMZ que sao
> necessarios em nossos projetos.

KMZ is an existing deliverable here, not a hypothetical. That gives Object Data a
consumer Property Set cannot serve, so the two are **not competing for one slot**:
Property Set for calculation inside the geometric moment, Object Data for what
leaves the drawing. They can coexist on the same entity.

Recorded as a verification and study point at the operator's request, and flagged
**unverified**: a filesystem search of the Civil 3D 2026 install found no KML or
KMZ component, and `ManagedMapApi.dll` contains zero occurrences of the string
`kml`. The mechanism is therefore not confirmed — it may be `MAPEXPORT` with a
provider registered elsewhere, a separate Autodesk utility, or a third-party step.

**Partly resolved 2026-09-20** by the research the operator supplied: the
mechanism is **MAPEXPORT**, which exports Object Data fields as SHP/SDF
attributes and **does not offer Property Set data for the SHP's DBF**. That is
the concrete reason Object Data is necessary rather than merely preferred, and it
confirms the operator's requirement.

Still open: KMZ specifically, as distinct from SHP/SDF. Asking which command
produces the KMZ today and watching one run remains the fastest answer, and it is
the only way to learn which fields survive the crossing.

**R4. `roadway_create_km_marker` — PAUSED by operator decision, 2026-09-19.**

The operator reports the tool's purpose is still not clear to them, and asked for
it to be paused temporarily.

**Do not implement, do not propose, do not include in estimates** until the
operator raises it again. If it must be mentioned, explain what the tool would do
first — place kilometre markers along an alignment automatically — rather than
asking the block / polyline / COGO point question again. Asking the same question
a second way is what produced this pause.

Original framing kept below for whoever picks it up later.

`roadway_create_km_marker` is a tool named in `AGENTS.md` section 16, never built.
The intent is that the platform places kilometre markers along an alignment
automatically instead of someone drawing each one.

The open question is what the tool should actually draw. A block reference with
the kilometre number as an attribute, which is the usual way symbols are handled
and which stays countable and schedulable. A polyline plus text, which is dumb but
works anywhere. Or a COGO point, which is Civil 3D native and knows its own
station, but disappears on export like every other Civil object.

Nobody can pick this from the code: it depends on how these markers are drawn and
delivered in your practice today. If they already exist in the drawings you have,
the answer is whatever is there, and that can be read rather than decided.

The question now has a second half the earlier version missed: whatever the tool
creates in the geometric moment, what should it become after export?

## What does not change

Worth stating plainly, because the list above is long.

The layering rule held. Roadway and Survey still carry no Autodesk reference, and
the batch tools still delegate to the same adapters the single-object tools use —
which is why the batch station offset round-tripped to 11 decimals against
`alignment_station_offset` instead of drifting from it.

The write pipeline held. Document lock, transaction, commit/abort, rollback
outranking the original failure, `Dispose` that never throws — all of it is
unaffected by anything in this document.

The confirmation model held. Atomicity was proven live: a batch with a valid
handle followed by an invalid one wrote nothing.

The genericity requirement held. `Dados_Rodovia` has zero occurrences in `src/`.
The problem found here is naming and module placement, not a hardcoded value.

None of the 264 tests are affected by this review. No source file was modified.

## Suggested order

1. **D5** — catalog entry, minutes.
2. **D4** — rename while only one tool carries the wrong name.
3. **D3** — extract the confirmed-write pattern *before* a third tool uses it.
4. **D1** — moment detector; cheap, and it disambiguates 15 tools at once.
5. **D2** — the naming pass. Largest, least urgent, and the one that gets worse
   with every response shape added meanwhile.

R2 can run in parallel with any of them; it is one live round and it unblocks R3.
