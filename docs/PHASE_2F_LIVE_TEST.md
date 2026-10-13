# Phase 2F Live Test - AEC Property Set Reads

Date: 2026-09-14

Scope: validate the installed Phase 2F Autodesk bundle against Civil 3D 2026 with the new read-only `propertyset_definition_list`, `propertyset_list`, and `propertyset_get` tools.

## Result

Status: Passed.

The phase was validated in two runs against two drawings. The first drawing proved definition reads and object resolution but had no attached property sets. The user then supplied a second drawing that does, and it closed the value-reading gap.

Proven live:

- reading property set definitions, in both drawings
- reading attached property sets with real values
- property name resolution against real definitions
- `propertyset_get` by exact name and case-insensitively
- the empty-but-valid case, exercised against 4146 objects in the first drawing
- the `ObjectNotFound` and `PropertySetNotFound` error paths
- the name filter returning `count: 0` on no match

Remaining gap at the end of Phase 2F:

- `numeric_value` was never populated from live data, because every attached property in both drawings is of type `Text`

Later closure:

- Phase 3C closed this gap with `propertyset_numeric_test_create`, which created a controlled `Real` property set, wrote `987.654321`, and read the same `numeric_value` back through `propertyset_get`. See `docs/PHASE_3C_LIVE_TEST.md`.

## Installed Bundle

Installed to `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle`. Offline diagnostic: success. The live catalog advertised 36 tools.

## Test Drawing

A copy was used, never the original:

```text
source : C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg
copy   : artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_propsets_20260914-091254.dwg
```

## The Dialog Monitor Earned Its Keep

Opening the drawing raised the familiar `References - Not Found Files` modal again. This time the test did not guess or hang:

```json
{ "blocked_by_modal": true, "blocking_dialog_title": "References - Not Found Files" }
```

The test dismissed the dialog and confirmed recovery:

```json
{ "blocked_by_modal": false }
```

This is the first time the Phase 1C monitor was used as a working tool rather than being the thing under test. It turned a previously mysterious hang into a named, handled condition.

## propertyset_definition_list

The drawing carries 6 property set definitions:

```json
{
  "success": true,
  "count": 6,
  "names": [
    "CADINFRA_BACIA",
    "CADINFRA_TALVEGUE",
    "COORDENAÇÃO",
    "Corridor Model Information",
    "Corridor Property Data – User Defined",
    "Corridor Shape Information"
  ]
}
```

Three are project-specific and three ship with the Autodesk corridor template. Note that `COORDENAÇÃO` carries an accent and `Corridor Property Data – User Defined` contains an en dash. Both survived the JSON round trip intact, which matters because property set names are user-authored and frequently non-ASCII in this project.

## propertyset_list

Every one of the drawing's 4146 entities was probed by handle, covering both spaces:

```text
ModelSpace entities:  2741
PaperSpace entities:  1405
probed 4146 of 4146
result: no object in this drawing has attached property sets
```

Each call succeeded and returned `count: 0`. That is the correct answer, and it exercised object resolution by handle 4146 times without a single failure. It also confirms the design decision that an object with no property data returns an empty list rather than an error: had that path thrown, this sweep would have produced 4146 errors instead of 4146 valid answers.

Before the full sweep, alignments and corridors were probed specifically, since those are the objects a roadway workflow would most likely carry data on. None had attached sets either.

## Object Not Found

```json
{
  "success": false,
  "errorCode": "ObjectNotFound",
  "errorMessage": "Object with handle 'ZZZZZZ' was not found in the target drawing."
}
```

This is the distinction that makes the empty case trustworthy: a handle that does not resolve fails loudly, so `count: 0` can be read as "this object genuinely has no property sets" rather than "something went wrong".

## Second Run: Drawing With Attached Property Sets

The user supplied a production drawing that carries property data:

```text
source : C:\Users\CGomes\Desktop\Painel_Gestao\Correcao de Km\Way-153_25-179.dwg
copy   : artifacts\live-tests\Way-153_25-179_propsets_20260914-104255.dwg
```

Again only the copy was opened, and the source kept its original timestamp.

### Definitions

This drawing carries 7 definitions, one more than the first, and they expose real property definitions rather than empty stubs:

```json
[
  { "name": "CADINFRA_BACIA", "property_count": 9,
    "properties": ["Nome:Text", "Grupo:Text", "Area:Real", "Coeficiente Runoff:Real", "VAdicional:Real"] },
  { "name": "CADINFRA_TALVEGUE", "property_count": 2,
    "properties": ["Declividade Ponderada:Real", "Declividade Media:Real"] },
  { "name": "COORDENACAO", "property_count": 4,
    "properties": ["Handle:Text", "AREA_3D_SUPERFICIE:Text", "COMPRIMENTO_SOLIDOS_CORREDOR:Text"] },
  { "name": "Dados_Rodovia", "property_count": 4,
    "properties": ["Secao:Text", "Ponto_A:Text", "Ponto_B:Text", "BR:Text"] }
]
```

Accented property names round-tripped intact, as did the en dash in the stock `Corridor Property Data` definition. The names above are written without accents in this document only to keep the file ASCII; the live responses carried the accented forms.

### Attached Sets With Real Values

Sweeping all 2473 objects across both spaces found the property data:

```text
ModelSpace entities:  1056
PaperSpace entities:  1417
Dados_Rodovia: 526 object(s)
```

A representative object:

```json
{
  "object_handle": "10781C",
  "count": 1,
  "property_sets": [
    {
      "name": "Dados_Rodovia",
      "handle": "10A379",
      "definition_handle": "10553F",
      "property_count": 4,
      "properties": [
        { "name": "Secao",   "data_type": "Text", "value": "529",     "numeric_value": null, "is_read_only": false },
        { "name": "Ponto_A", "data_type": "Text", "value": "km-529",  "numeric_value": null, "is_read_only": false },
        { "name": "Ponto_B", "data_type": "Text", "value": "km-530",  "numeric_value": null, "is_read_only": false },
        { "name": "BR",      "data_type": "Text", "value": "153",     "numeric_value": null, "is_read_only": false }
      ]
    }
  ]
}
```

This is real BR-153 kilometre-marker data. Consecutive objects carried `529`, `530`, and `531`, confirming that values are read per object rather than cached from the first one.

Property names resolved correctly through the definition, which exercises the `PropertyIdToName` path that unit tests could only simulate.

### propertyset_get

```json
{ "success": true, "object_handle": "10781C", "name": "Dados_Rodovia", "property_count": 4 }
```

Requesting the same set as `dados_rodovia` also succeeded, confirming case-insensitive matching.

### Error And Filter Paths

```json
{ "success": false, "errorCode": "PropertySetNotFound",
  "errorMessage": "Property set 'NO_SUCH_SET_2F' is not attached to object '10781C'." }
```

```json
{ "success": true, "count": 0 }
```

The second result is `propertyset_list` with a `name_contains` filter that matches nothing. It returns an empty success rather than an error, which is correct: the object resolved and simply has no set matching the filter.

## Remaining Gap At The End Of Phase 2F

`numeric_value` has still never been populated from live data. Both drawings attach only `Text` properties, even though definitions such as `CADINFRA_BACIA` declare `Real` properties like area and runoff coefficient. The numeric branch is covered by unit tests only.

This was later closed in Phase 3C by creating a controlled `Real` property set in a temporary test DWG copy and reading the populated numeric value back through the read path.

## Cleanup

Both drawing copies were closed without saving, and both originals kept their pre-test timestamps.

The first run needed a force stop, matching every prior phase. The second run quit cleanly on `Quit()` with no force stop required, the first time that has happened in this project.

Before the second run, two Civil 3D processes were already running that this session did not start. They were not touched until the user explicitly authorised closing them, following the rule against terminating an instance that might hold production work.

## Safety Boundary

All three tools are READ/Low:

- read inside a read-only AutoCAD transaction
- never call `PropertyDataServices.AddPropertySet`, `RemovePropertySet`, or `PropertySet.SetAt`
- general property set writes are deliberately unimplemented; the later Phase 3C numeric tool is a bounded write probe, not a general setter
- a single unreadable definition or property is skipped so one bad record cannot fail a request
