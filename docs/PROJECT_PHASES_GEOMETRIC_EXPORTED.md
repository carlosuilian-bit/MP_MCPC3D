# Project Phases: Geometric and Exported

Date: 2026-09-19

Status: **knowledge only, nothing implemented.** No tool in this platform reads a
drawing in exported form yet. This document records a distinction the operator
raised and the measurements that confirm it, so a later phase is designed against
facts instead of guesses.

## The distinction

A road project is worked in two moments, and they are not the same drawing with
fewer features. They are two different data models that answer the same questions
by different means.

**Geometric.** The project under development. Alignments, surfaces, profiles and
corridors are live Civil 3D objects. A station is obtained by asking the
`Alignment` object, and the answer is exact.

**Exported.** The project delivered. Civil 3D intelligence is gone: the alignment
is a polyline, the station is a text label, the surface is contours and hatch.
The same question has to be answered from primitives.

The operator's own words, and the reason this matters to tool design:

> posso ter metodos diferentes para fazer funcoes "Semelhantes"

Two tools that answer one question from different data sources are not
duplicates. This corrects an earlier analysis in this project, which classified
`roadway_get_station` as redundant with `alignment_station_offset` on the grounds
that they answer the same question. They do — in different moments, from
different data, with different reliability.

## The reference pair

Both files are the same project in the two moments. The operator confirmed the
pairing and confirmed which geometric variant is the reference.

```text
geometric : C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg
            40,737,831 bytes   md5 d589e77f548f2fdc3155d1b84002ee8c   AC1032 (AutoCAD 2018)

exported  : DNT-393RJ-172+112-173+235-ACE-PIT-DE-V2-001-R01.dwg
            86,253,930 bytes   AC1015 (AutoCAD 2000)
```

The file names carry no meaning across projects; they change per project. What
identifies the moment is the content, not the name.

Note the format downgrade. The delivered file is AutoCAD 2000 format, a common
delivery requirement and itself a signal that the drawing has left the Civil 3D
world.

The Phase 2F live test measured the geometric file of this pair exactly: its
working copy `artifacts/live-tests/TBD-25-009_GEOMETRICO_R2_propsets_20260914-091254.dwg`
carries the same md5 as the source above. The numbers below are measured, not
inferred.

## Measured

| | Geometric | Exported |
|---|---|---|
| DWG format | AC1032 | AC1015 |
| Property set definitions | **6** | **0** |
| Objects carrying an attached set | **0 of 4146** | 0 |
| Entities | 4146 (2741 model + 1405 paper) | ~11,700 `SOLID` hatches alone |
| Stationing | `Alignment` object | text: `172+112`, `172+340`, `172+700`, `172+980`, `173+235` |

The six definitions present in the geometric moment:

```text
CADINFRA_BACIA                          project
CADINFRA_TALVEGUE                       project
COORDENACAO                             project
Corridor Model Information              Autodesk corridor template
Corridor Property Data - User Defined   Autodesk corridor template
Corridor Shape Information              Autodesk corridor template
```

### How the exported file was measured

Read-only binary inspection, no Civil 3D involved, originals untouched. AC1015
stores class names as readable strings, which is what makes the absence
meaningful rather than merely unobserved: `Aecc*` strings occur 2335 times and
`AecDb*` 29 times in that file, so the property set classes would be visible if
present. Searched under every spelling (`AecDbPropertySet`, `AecDbPropertySetDef`,
`PropertySet`, `PropSet`, `AecPropDataMgd`, `AECPROPERTYSET`): zero occurrences.

The `AecDb*` hits are all architectural display representations (wall, stair,
roof, window, ceiling grid) that ship as boilerplate with any AEC-based product.
Not property sets.

The same probe cannot be run against the geometric file: AC1032 compresses its
sections, so absence of a string there proves nothing. Its numbers come from the
Phase 2F live test instead.

## Two findings, and the second is the larger one

**The export destroys property sets.** Not only the attached values — the
definitions themselves do not survive. Six became zero.

**Property sets carry no data in this project even before the export.** All 4146
entities of the geometric drawing returned `count: 0`. Alignments and corridors
were probed specifically. The six definitions are empty scaffolding: three
authored for the project and never filled, three inherited from the Autodesk
corridor template.

The semantics of this project were therefore never in the property sets. This
separates two agendas that were being treated as one:

- **Property set mastery** (create, read, edit). Correct as a goal and already
  largely built: read, write, find and batch write are live validated. It is a
  new capability, for work that will start using it.
- **Reading the layer convention.** How one works with what already exists, in
  both moments, including deliveries already made. This was not on the roadmap.

## What actually bridges the two moments

The layer naming convention. It is the only structure present in both files.

```text
F-HZ-EIXO            F-VT-GREIDE              F-SC-SECAO
F-HZ-ESTACA          F-VT-TERRENO             F-SC-TERRENO
F-HZ-SECAO           F-VT-COTAS-PROJETO       F-SC-TABELA_ESTACA
F-HZ-INTERSECAO      F-VT-COTAS-TERRENO       F-HATCH-PISTA-EXISTENTE
F-HZ-*-TXT           F-VT-ESTACA
```

`HZ` is plan, `VT` is longitudinal profile, `SC` is cross section.

In the exported moment the axis is not an `Alignment`. It is a polyline on a
layer named `F-HZ-EIXO`. The profile is a polyline on `F-VT-GREIDE`. The station
is text on `F-HZ-ESTACA`.

**In the geometric moment you ask the object. In the exported moment you ask the
layer.**

## Where the convention comes from — the tracking anchor

This is the most important practical finding in this document, and the operator
asked for it to be clearly identified, because standardising it is what makes
tracking a project across both moments possible.

The convention is **not** authored by hand and **not** a drafting habit. It is a
Civil 3D drawing setting, configured in the template, and it is readable through
the .NET API:

```text
CivilDocument.Settings
  -> SettingsRoot
  -> DrawingSettings                    (Autodesk.Civil.Settings.SettingsDrawing)
  -> ObjectLayerSettings                (Autodesk.Civil.Settings.SettingsObjectLayers)
  -> GetObjectLayerSetting(SettingsObjectLayerType)
                                        (Autodesk.Civil.Settings.SettingsObjectLayer)
         .ObjectType      SettingsObjectLayerType
         .LayerName       string
         .Modifier        ObjectLayerModifierType
         .ModifierValue   string
```

Assembly: `C:\Program Files\Autodesk\AutoCAD 2026\C3D\AeccDbMgd.dll`.
Confirmed by metadata read with `artifacts/metadata-probe`, not recalled.

Supporting members on `SettingsObjectLayers`:

```text
GetObjectLayerSetting(SettingsObjectLayerType) -> SettingsObjectLayer
hasLabelingLayer(SettingsObjectLayerType)      -> bool
ObjectControlledByLayer                        { get; set; }
```

`SettingsObjectLayerType` is an enum of roughly 45 members, so the full map is
obtained by walking the enum rather than by enumerating a collection. It includes
the labelling variants, which is what makes it cover annotation as well as
geometry:

```text
Alignment            AlignmentLabeling      AlignmentTable
Profile              ProfileLabeling        ProfileView / ProfileViewLabeling
Section              SectionLabeling        SectionView / SectionViewLabeling
SampleLine           SampleLineLabeling     MatchLine / MatchLineLabeling
Corridor             CorridorSection        Assembly
GridSurface          GridSurfaceLabeling    FeatureLine
Parcel               ParcelLabeling         ParcelSegment / ParcelSegmentLabeling
Pipe                 PipeLabeling           Structure
Grading              GradingLabeling        MassHaulLine / MassHaulView
Interference         MaterialSection        MaterialTable
PointTable           Sheet                  GeneralNoteLabel / GeneralSegmentLabel
```

### Why this is the tracking anchor

The correspondence with the delivered drawing is direct:

| Object layer setting | Layer in the exported file |
|---|---|
| `Alignment` | `F-HZ-EIXO` |
| `AlignmentLabeling` | `F-HZ-ESTACA` |
| `Profile` | `F-VT-GREIDE` |
| `ProfileView` / `ProfileViewLabeling` | `F-VT-COTAS-PROJETO`, `F-VT-COTAS-TERRENO` |
| `SampleLine` | `F-HZ-SECAO` |
| `Section` / `SectionView` | `F-SC-SECAO` |

So the same setting that decides which layer a Civil object is drawn on in the
geometric moment decides which layer its exploded remains land on in the exported
moment. **That is the thread that runs through both, and it is machine-readable
at the source.**

Consequence, and the reason the operator wants it standardised: the role-to-layer
map never has to be typed, transcribed, or guessed. It is read out of the
geometric drawing. A hand-maintained map would drift from the template the first
time someone edited one and not the other; a map read from the template cannot.

### Limits of this anchor

Object layers cover **Civil 3D object types only**. Layers seen in the delivered
file that will not appear in this setting:

```text
F-HATCH-PISTA-EXISTENTE     a hatch is not a Civil object
F-SC-TABELA_ESTACA          table/annotation, from a style or drafting convention
F-HZ-INTERSECAO / -TXT      drafting convention
```

Those come from styles, or from nothing but habit. The setting gives the backbone
of the map, not the whole of it. Standardising should therefore cover both: the
object layers, which are already structured, and the drafting layers, which are
not yet.

### Proposed first tool

A read-only tool returning this map is the cheapest useful step toward the
exported moment — cheaper than the moment detector tracked as defect D1 in
`docs/CONSOLIDATION_REVIEW.md`, and it partly serves the same need, since a
drawing that answers this call is by definition still geometric.

Not implemented. Recorded so the design starts from the setting rather than from
a hardcoded list.

## Measured with block reads, 2026-09-24

The block reads of Phase 2J are the first tools run live on both drawings of the pair. They confirm the prediction that plain AutoCAD entities keep answering after the export:

| | Geometric | Exported |
|---|---:|---:|
| Entities in layouts | 4,217 | 38,179 |
| Block inserts | 104 | 1,961 |
| of which dynamic | 66 | 1,372 |
| Layouts holding inserts | 3 | 13 |

Two observations from that run bear directly on this document, and neither is concluded yet:

- **Civil 3D label components survive as blocks.** The exported drawing carries `AeccTickLine` x202 and `AeccTickCircle` x32 as plain block inserts.
- **A handle may survive the export.** `D7D90` is the same `_MP Marco Estacao` insert in both files, with a different attribute value (`MPE 92` and `MPE 01`). If the export preserves handles, the handle would be a tracking anchor between the moments, stronger than a layer name. Worth a deliberate check before relying on it.

Detail in `docs/PHASE_2J_LIVE_TEST.md`.

## Consequences for tool design

Nothing below is implemented. These are the constraints a later phase has to
respect.

**The layer convention is declared configuration, never code.** Same rule already
established for property set names: `Dados_Rodovia` appears zero times in `src/`,
and `F-HZ-EIXO` must never appear there either. This convention belongs to this
client and this delivery; another project will have another one. A tool for the
exported moment takes a role-to-layer map as input:

```text
eixo_planta     -> F-HZ-EIXO
greide          -> F-VT-GREIDE
estaca_planta   -> F-HZ-ESTACA
terreno_perfil  -> F-VT-TERRENO
```

**The moment is explicit, not auto-detected.** A tool that silently picks its
strategy hides which one it used. Prefer a tool per moment, plus one cheap
detector that reports which moment a drawing is in.

**The response declares where the number came from.** A station computed from an
`Alignment` is exact. A station read from an MText label is worth whatever the
person who plotted it was worth. If both return in the same shape, a caller
treats both as fact. They must be distinguishable in the response.

## Naming correction this makes necessary

`roadway_propertyset_write` has no roadway logic in it. It takes `set_name`,
handles and name/value pairs, and works identically with any property set. It
sits in the Roadway module by accident of where it was built. The operator
confirmed the origin of the confusion: the only sample file available at the time
happened to contain road data.

| Today | Should be |
|---|---|
| `roadway_propertyset_write` | `propertyset_set_batch`, Civil module |
| `roadway_propertyset_read` (planned) | `propertyset_get_batch`, Civil module |

Not yet done. Recorded so it is fixed while only one such tool exists.

## Not proven

- Whether a drawing whose objects **do** carry property set values loses them on
  export. This pair cannot answer it, because its geometric moment had none
  attached to begin with. The controlled test is cheap: copy the geometric file
  to `artifacts/live-tests/`, stamp a known property set, run the operator's own
  export, read back.
- Whether every project of this operator uses the `F-HZ` / `F-VT` / `F-SC`
  convention, or whether it is specific to this client. Either way the map stays
  declared rather than assumed.
- Nothing here was observed by opening either drawing in Civil 3D. The geometric
  numbers come from the Phase 2F live test; the exported numbers from read-only
  binary inspection. Neither original was opened or modified.
