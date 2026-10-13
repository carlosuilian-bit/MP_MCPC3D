# Data Carriers: Property Set, Object Data, XData, XRecord

Date: 2026-09-20

Status: **analysis only, nothing implemented.** The platform supports AEC Property
Sets today and nothing else. This document compares the options so a carrier is
chosen on evidence rather than on which one was built first.

## Evidence classes used here

Three different standards of proof appear below and they are not interchangeable.
Each claim says which one it rests on.

- **Measured** — observed in this project's own drawings or live runs.
- **API-confirmed** — read from assembly metadata with `artifacts/metadata-probe`.
  Proves a member exists; proves nothing about behaviour.
- **Secondary** — from the research report of 2026-09-20 (provenance at the end),
  which cites Autodesk Help, Autodesk University and Autodesk Community. Useful
  and detailed, but not verified by this project. Treat as a strong lead, not as
  fact, and verify before building on any single point.

## The question that prompted this

The operator asked whether property sets are worth investing in, and answered it
with a concrete case:

> se eu conseguir carregar informacoes de um pavimento como espessura de
> Pavimento CBUQ, Espessura de base, sub base, etc, para uma hachura, ou seja,
> carregar informacoes que sao importantes para fazer calculos ou analises,
> seria sim interessante. Importante a analise da melhor proposta de trabalho,
> property set ou Object data.

Then added the fact that reframed it:

> Object Data pode ser exportado, inclusive para arquivos KMZ que sao
> necessarios em nossos projetos.

## Why there are three mechanisms, not three versions of one

*Secondary, and it is the most useful framing in the whole report.*

These did not evolve from one another. They come from three different products
that were merged into Civil 3D, and each was designed for a different consumer:

```text
AutoCAD core     -> XData        speaks to programs
Map 3D / GIS     -> Object Data  speaks to GIS
AEC / BIM        -> Property Set speaks to documentation and assets
```

This explains the apparent redundancy, and it explains why an operation that is
obvious in one is simply absent in the other. The deciding question is therefore
not "which is best" but **"who needs to consume this data?"**

## The carriers

### AEC Property Set

`Autodesk.Aec.PropertyData.DatabaseServices`, in `AecPropDataMgd.dll`.

A `PropertySetDefinition` lives in a drawing dictionary and declares typed
properties. Instances attach per object through `PropertyDataServices`.

Types: Text, Integer, Real, TrueFalse, List, AutoIncrement, plus a `UnitType` per
property, so a thickness can be declared as a length rather than a bare number.
*API-confirmed.*

```text
PropertyDefinition.Automatic            { get; set; }
PropertyDefinition.SetAutomaticData(string, string)
PropertyDefinition.GetAutomaticData()   -> AutomaticPropertyData[]
PropertyDefinition.ClearAutomaticData()
```

**Automatic properties** derive their value from the object itself. **Formula
properties** compute from other properties, using VBScript. *Secondary.*

**Important constraint, and it corrects an earlier claim in this project.** The
available automatic properties are **hard-coded per object type** and cannot be
extended through the interface; for anything not exposed, Autodesk points to
formula, API, or Dynamo. The report's worked example is that the 3D area of a TIN
Surface is *not* available as an automatic property. *Secondary.*

Earlier drafts of this document asserted that a hatch's area would be available
automatically. That was inferred from the API existing, never verified, and the
constraint above makes it a real question rather than a given. **It must be
checked against `AcDbHatch` before any design leans on it.** If it is not exposed,
a formula property or a platform-written value takes its place — the case is not
lost, but it is not free either.

Other capabilities, all *secondary*:

- **Civil 3D labels.** The Text Component Editor has a Property Sets tab;
  `<[PS:DadosRodovia:BR]>` composes into a label, so changing the property changes
  the label. **Not universal**: a 2024 case had a COGO Point property set working
  in a plan label but absent in a Profile View label, because not every label type
  exposes the tab.
- **Schedule Tables.** An AEC schedule reads properties straight from the objects;
  the table stores nothing itself, so it refreshes with the model. Exports to XLS,
  CSV, TXT.
- **Standardized Data Tool.** Aggregates property set data from many DWGs into
  Excel and synchronises back.
- **Database Fields.** A documented workflow connects property sets to MDB, XLS,
  TXT, CSV sources.
- **ArcGIS Connector** imports ArcGIS attributes *as property sets* — though a
  June 2026 note says those attributes are not automatically available to labels,
  description keys, or symbol assignment.
- **Risk:** replacing a definition with an incompatible one can destroy existing
  manual values, and existing sets cannot be merged — a new definition has to be
  created carrying the wanted fields.

Platform support: **complete and live validated.** Seven tools.

Survival through export: **none. Measured.** Six definitions in the geometric
drawing, zero in the delivered file, definitions included. See
`docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`.

### Object Data

`Autodesk.Gis.Map.ObjectData`, in `Map\ManagedMapApi.dll`. A Map 3D feature, and
Map 3D ships inside Civil 3D.

*API-confirmed:*

```text
Tables.GetTableNames() / IsTableDefined(string) / Add(string, FieldDefinitions, string, bool)
Tables.UpdateTable / RenameTable / RemoveTable
Tables.GetObjectRecords(uint, DBObject | ObjectId | MapObjectId, OpenMode, bool) -> Records
FieldDefinition.Create(name, description, string | double | int | Point3d | DataType)
FieldDefinition.Name / .Description / .Type / .DefaultMapValue
Record.TableName / .Count / .MapObjectId / this[int] -> MapValue
```

Types: Integer (32-bit), Real, Character, Point. **A Character field is limited to
132 characters.** *Secondary.*

**Multiple records of the same table on one object** — a property set cannot do
this. Autodesk presents it as a way to hold historical information; the report's
example is an inspection log on a sign, one record per inspection. *Secondary,
but consistent with the API's `Records` return type, which is API-confirmed.*

Where it is strong, all *secondary*:

- **Map queries** combining Location, Property and Data conditions, so "every
  polyline inside this area whose `BR` = 040 and `TIPO` = ACESSO" is a query, not
  a script.
- **Thematic mapping** driven by attribute values.
- **MAPIMPORT** turns GIS attributes into Object Data tables automatically.
- **MAPEXPORT** writes geometry plus Object Data fields out to SHP/SDF.

Limitations, *secondary*:

- Little automatic intelligence: no equivalent of automatic or formula properties,
  so a length or area has to be computed and rewritten by someone or something.
- Poor integration with native Civil 3D labels — Map 3D annotation is the
  documented route instead.
- Table name collisions: if attached drawings define same-named tables with
  different definitions, the first activated drawing's definition wins. Schema
  standardisation matters.
- Changing a table definition requires the drawings using it to be active, and
  some operations need Superuser privileges.
- Object Data attaches to DWG objects and **cannot attach directly to FDO
  geospatial features**.

Platform support: **none.** A new adapter, new contracts, new tools.

Survival through export: **unknown.** Phase 3F measures it.

### XData

Extended Entity Data, plain AutoCAD, keyed by a registered application name
(RegApp). Values are stored under DXF group codes 1000-1071. *Secondary.*

```text
1000 string   1004 binary   1005 handle
1010 point    1040 double   1070 int16   1071 int32
```

In .NET this surfaces as `ResultBuffer` + `TypedValue`.

Two properties that matter for a plugin, *secondary*:

- **Code 1005 stores another entity's handle**, so relationships can be recorded,
  and those references are translated during INSERT/WBLOCK.
- XData is preserved when the object is copied, including between drawings.

**The limit is the problem.** 16 KB per entity, *shared across every application*
that writes to that object. Autodesk's own developer guidance is to stay under
roughly 2 KB and never exceed 4 KB per application, and to move to XRecord for
anything larger. *Secondary.*

Platform support: none. Survival through export: unknown; Phase 3F measures it.

### XRecord — the fourth mechanism, and the one this project probably wants

*Secondary, and it was not on our list before reading the research.*

An Extension Dictionary on an entity can hold multiple XRecords and further
dictionaries:

```text
Entity
 └─ Extension Dictionary
      ├─ MyApp
      │    ├─ Config
      │    ├─ DatabaseInfo
      │    └─ History
      └─ AnotherApp
```

No 16 KB ceiling, and real structure. Autodesk's documented recommendation is to
prefer this over XData once an application stores anything substantial.

**This matters directly to this MCP.** If the platform ever needs to record its
own state on an object — what generated it, which version, whether it needs
reprocessing, which external record it corresponds to — XRecord is the documented
answer, not XData. Worth knowing before that need arrives and gets solved badly.

## Side by side

| | Property Set | Object Data | XData | XRecord |
|---|---|---|---|---|
| Consumer it serves | BIM, documentation, assets | GIS | programs | programs |
| Typed schema | richest | narrower | none | none |
| Unit-aware | yes | no | no | no |
| Derives own values | yes, but hard-coded per type | no | no | no |
| Formulas | yes, VBScript | no | no | no |
| Records per entity | one per definition | **many** | one per app | many |
| Civil 3D labels | yes, with gaps | poor | no | no |
| Schedules / Excel | yes | no | no | no |
| SHP / SDF export | **no** | **yes** | no | no |
| Size limit | none of note | 132 chars per text field | **16 KB shared** | none of note |
| Survives export | **no, measured** | unknown | unknown | unknown |
| Platform support | **complete** | none | none | none |

## Recommendation

**They are not competing for one slot.** Each serves a consumer the others cannot.

- **Property Set** — data the engineer sees, edits, labels, schedules, or sends to
  BIM. The geometric moment.
- **Object Data** — data that must leave for GIS. Confirmed necessity, below.
- **XRecord** — the platform's own metadata about an object. Not XData, because of
  the 16 KB shared budget.
- **XData** — only for a few small identifiers, if XRecord is overkill.

### The finding that settles Object Data's necessity

*Secondary, and it confirms what the operator said.*

**MAPEXPORT exports Object Data, but does not offer Property Set data as
attributes for the SHP's DBF.** So an asset database built entirely in property
sets cannot be delivered as geometry plus attributes without a conversion step:

```text
Property Set -> Dynamo / API -> Object Data -> MAPEXPORT -> SHP
```

That is the concrete reason Object Data remains necessary, and it matches the
operator's requirement exactly. It is not a preference; it is a gap in the export
path.

### The governance rule that must come with a hybrid design

*Secondary, and worth adopting as a project rule.*

If the same business value lives in more than one carrier with nobody declared
authoritative, they drift:

```text
Property Set = 630.350
Object Data  = 630.351
Database     = 630.348
```

and no one can say which is right. So a hybrid design needs a declared source of
truth and a one-way generation path — for example Property Set as the authored
value, Object Data generated from it for GIS delivery, and XRecord holding only
identity (GUID, source id, sync version), never repeating business data.

**This is a natural job for this MCP.** Rather than choosing one carrier, the
platform can be the layer that keeps them in sync from a declared source. That is
a design direction, not a commitment.

## Still to verify

Ranked by how much they would change a decision.

1. **Is hatch area exposed as an automatic property?** The whole "Property Set for
   calculation" argument leans on it. Automatic properties are hard-coded per
   object type, so this needs checking against `AcDbHatch`, not assuming.
2. **What survives the operator's export?** Property Set is measured dead. Object
   Data, XData and XRecord are unmeasured. **Phase 3F**.
3. **Which command produces the KMZ today?** The research establishes MAPEXPORT as
   the Object Data path to SHP/SDF, which resolves the *principle*. It does not
   establish KMZ specifically, and a filesystem search of this Civil 3D 2026
   install found no KML/KMZ component and zero occurrences of `kml` in
   `ManagedMapApi.dll`. Watching one real run answers it faster than inference.
4. **Which label types expose the Property Sets tab?** Known to be incomplete.
   Matters if labels become part of a workflow.
5. **Do the secondary claims hold?** Nothing here has been verified against a
   running Civil 3D by this project.

## Provenance

The bulk of the comparative detail above comes from a research report the operator
supplied on 2026-09-20, titled "Análise Autodesk Detalhada" / "Relatório técnico —
Object Data, Property Set e XData no AutoCAD/Civil 3D", produced by ChatGPT and
citing Autodesk Help, Autodesk University sessions and Autodesk Community threads.

It is detailed, internally consistent, and consistent with everything this project
has measured independently. It is also unverified secondary material with
citations this project has not followed. The report's own star-rating tables are
opinion and are deliberately not reproduced here.

Where it conflicted with an earlier claim of mine — the hatch-area automatic
property — the claim has been demoted to a question, not silently kept.
