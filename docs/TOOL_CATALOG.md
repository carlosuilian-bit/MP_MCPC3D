# Tool Catalog

Date: 2026-09-19

Status: **49 tools, all live validated against Civil 3D 2026.**

The last phases to close were 2H `propertyset_find`, 3E `propertyset_set`, 6A `roadway_station_offset_batch` and 6B `roadway_propertyset_write`, all validated on 2026-09-19. Phase 2G `object_get` / `object_properties` and Phase 1D dialog content reads were validated inside `docs/PHASE_1F_LIVE_TEST.md`, which is why they have no report of their own.

Each entry below carries its own `Validation status` line; that line is authoritative, not this paragraph.

Two notes that cut across the catalog, both from `docs/CONSOLIDATION_REVIEW.md`:

- **Response field naming is not consistent.** `JsonContractOptions` builds on `JsonSerializerDefaults.Web`, so contract properties without an explicit `[JsonPropertyName]` serialize as camelCase while the ones carrying an attribute are snake_case. `DocumentDescriptor`, embedded in every response, emits `instanceId` / `documentId` / `isActive`. Read the per-tool field lists literally; do not infer a convention. Tracked as defect D2.
- **15 of these tools read Civil 3D objects and return empty on an exported drawing** — one where the project was delivered as plain AutoCAD entities. An empty result there is indistinguishable from a drawing that genuinely has none. See `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`. Tracked as defect D1.

The server receives this catalog from the bridge during handshake. The server does not hardcode future Civil 3D tool availability.

The MCP STDIO adapter maps these same bridge descriptors to MCP tools. Input/output schemas originate from the bridge `ToolDescriptor` manifest and are preserved by the adapter.

## civil3d_ping

| Field | Value |
| --- | --- |
| Module | Core |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- prove endpoint discovery
- prove named pipe reachability
- return bridge/protocol/product version

Returns:

- success
- instance ID
- bridge version
- protocol version
- product version
- timestamp

## civil3d_status

| Field | Value |
| --- | --- |
| Module | Diagnostics |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- summarize bridge and Civil 3D state
- expose active document
- expose document count
- expose active command when available
- expose capabilities
- report dialog monitor status as partial

Dialog monitor note:

- Phase 1C implements real Win32 modal enumeration.
- `civil3d_status` includes the current dialog-monitor state; call `list_dialogs` for full modal details when a host-dispatched tool stalls.

## document_list

| Field | Value |
| --- | --- |
| Module | Core |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list open documents in one Civil 3D instance
- return unambiguous document IDs
- prevent wrong-DWG execution in future phases

Returns:

- instance ID
- documents with document ID, name, full path, active state, saved/read-only state, units, database fingerprint, and session identifier

## drawing_info

| Field | Value |
| --- | --- |
| Module | Core |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- return read-only metadata for the active drawing

Returns:

- active document descriptor

## layer_list

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list layers in the specified drawing
- return stable layer references for later tools
- support optional filters for name substring and layer state flags

Required input:

- `instance_id`
- `document_id`

Optional filters:

- `name_contains`
- `is_on`
- `is_frozen`
- `is_locked`
- `is_plottable`

Returns:

- `count`
- layer records with name, handle, object reference, current/on/frozen/locked/plottable state, color, linetype, lineweight, and description

## layer_get

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- get one layer from the specified drawing by name, handle, or object reference

Required input:

- `instance_id`
- `document_id`
- one of `name`, `handle`, or `object_reference` containing a name or handle

Returns:

- one layer record with the same shape as `layer_list`

Structured errors:

- `LAYER_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`

## layer_create

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | WRITE |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | Transaction rollback before commit |
| Timeout | 15 seconds |

Purpose:

- create one layer in the specified drawing

Required input:

- `instance_id`
- `document_id`
- `name`

Optional input:

- `color_index` from 1 through 255
- `is_plottable`
- `description`

Safety behavior:

- validates target instance and document
- validates AutoCAD layer-name syntax
- rejects duplicate names
- locks the target document before writing
- opens an AutoCAD transaction
- commits on success
- aborts before commit on failure

Returns:

- `created`
- created layer record
- transaction state:
  - `document_locked`
  - `transaction_started`
  - `committed`
  - `rolled_back`

Structured errors:

- `LAYER_ALREADY_EXISTS`
- `INVALID_LAYER_NAME`
- `DOCUMENT_LOCK_FAILED`
- `TRANSACTION_FAILED`
- `ROLLBACK_FAILED`

## layer_update

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | WRITE |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | Transaction rollback before commit |
| Timeout | 15 seconds |

Purpose:

- update one existing layer in the specified drawing
- support bounded layer definition changes without deletes, purge, or bulk edits

Required input:

- `instance_id`
- `document_id`
- one of `name`, `handle`, or `object_reference` containing a name or handle
- at least one update field

Optional update fields:

- `new_name`
- `color_index` from 1 through 255
- `is_on`
- `is_frozen`
- `is_locked`
- `is_plottable`
- `description`

Safety behavior:

- validates target instance and document
- validates AutoCAD layer-name syntax for locators and `new_name`
- rejects missing layer locators
- rejects no-op requests with no update fields
- rejects duplicate target names
- rejects turning off or freezing the current layer
- locks the target document before writing
- opens an AutoCAD transaction
- commits on success
- aborts before commit on failure

Returns:

- `changed`
- `previous_layer`
- `updated_layer`
- transaction state:
  - `document_locked`
  - `transaction_started`
  - `committed`
  - `rolled_back`

Structured errors:

- `LAYER_NOT_FOUND`
- `LAYER_ALREADY_EXISTS`
- `INVALID_LAYER_NAME`
- `INVALID_REQUEST`
- `DOCUMENT_LOCK_FAILED`
- `TRANSACTION_FAILED`
- `ROLLBACK_FAILED`

Validation status:

- Unit and fake-bridge MCP tests cover `layer_update` request/response shape and the current bridge catalog.
- The Phase 3B 18-tool bundle was packaged successfully.
- Civil 3D 2026 installed-bundle validation on 2026-09-12 reported an 18-tool catalog with `layer_update`.
- On a temporary DWG copy, `layer_update` renamed `MCP_LU_OK_114201` to `MCP_LU_OK_114201_UPDATED`, changed color to ACI 2, locked the layer, updated the description, and `layer_get` read the updated layer back.
- The live write reported `document_locked: true`, `transaction_started: true`, `committed: true`, and `rolled_back: false`.

## layer_set_current

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | WRITE |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | Transaction rollback before commit |
| Timeout | 15 seconds |

Purpose:

- make one existing layer the current layer in the specified drawing
- support low-risk current-layer changes without creating, deleting, or modifying layer definitions

Required input:

- `instance_id`
- `document_id`
- one of `name`, `handle`, or `object_reference` containing a name or handle

Safety behavior:

- validates target instance and document
- validates AutoCAD layer-name syntax when a name is provided
- rejects missing layer locators
- rejects missing target layers
- rejects frozen target layers
- locks the target document before writing
- opens an AutoCAD transaction
- commits on success
- aborts before commit on failure

Returns:

- `changed`
- `previous_layer`
- `current_layer`
- transaction state:
  - `document_locked`
  - `transaction_started`
  - `committed`
  - `rolled_back`

Structured errors:

- `LAYER_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_LOCK_FAILED`
- `TRANSACTION_FAILED`
- `ROLLBACK_FAILED`

Live validation:

- Civil 3D 2026 installed-bundle validation on 2026-09-12 reported a 17-tool Phase 3A catalog with `layer_set_current`.
- On a temporary DWG copy, `layer_set_current` changed current layer `_Deletar linha` to `0`, then restored `_Deletar linha`.
- Both live write calls reported `document_locked: true`, `transaction_started: true`, `committed: true`, and `rolled_back: false`.

## alignment_list

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list Civil 3D alignments in the specified drawing
- return stable object references for later read operations

Required input:

- `instance_id`
- `document_id`

Optional filters:

- `name_contains`

Returns:

- `count`
- alignment records with name, handle, object reference, description, type, length, starting station, ending station, site handle, and style handle

## alignment_get

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- get one alignment by name, handle, or object reference

Structured errors:

- `ALIGNMENT_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`

## alignment_station_offset

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- calculate station/offset for a drawing XY coordinate against one alignment

Required input:

- `instance_id`
- `document_id`
- one alignment locator: `name`, `handle`, or `object_reference`
- `x`
- `y`

Structured errors:

- `ALIGNMENT_NOT_FOUND`
- `CIVIL_QUERY_FAILED`

## alignment_point_location

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- calculate drawing XY from station/offset against one alignment

Required input:

- `instance_id`
- `document_id`
- one alignment locator: `name`, `handle`, or `object_reference`
- `station`
- `offset`

## surface_list

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list Civil 3D surfaces in the specified drawing

Returns:

- `count`
- surface records with name, handle, object reference, description, type, point count, minimum elevation, maximum elevation, and mean elevation when available

## surface_get

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- get one surface by name, handle, or object reference

Structured errors:

- `SURFACE_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`

## surface_elevation_at_xy

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- read surface elevation at a drawing XY coordinate

Structured errors:

- `SURFACE_NOT_FOUND`
- `CIVIL_QUERY_FAILED`

`CIVIL_QUERY_FAILED` is returned when the Civil 3D API rejects the coordinate, for example `Point Outside Surface`.

## cogo_list

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list drawing COGO points

Optional filters:

- `raw_description_contains`
- `full_description_contains`
- `limit`

Returns:

- `count`
- COGO point records with point number, handle, object reference, easting, northing, elevation, raw description, full description, and lock state

## cogo_get

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- get one drawing COGO point by `point_number`, handle, or object reference

Structured errors:

- `COGO_POINT_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`

## survey_bearing_to_azimuth

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 5 seconds |

Purpose:

- convert a quadrant bearing to a normalized survey azimuth
- provide a pure topographic geometry primitive without touching Civil 3D or a DWG

Required input:

- `bearing`

Accepted bearing shape:

- starts with `N` or `S`
- ends with `E` or `W`
- contains degrees and optional minutes/seconds, for example `N 45 30 0 E`

Returns:

- `instance_id`
- `input_bearing`
- parsed `bearing`
- `azimuth_degrees`

Structured errors:

- `INVALID_REQUEST`

Live validation:

- Civil 3D 2026 installed-bundle validation on 2026-09-12 reported a 22-tool catalog including the Survey module.
- `survey_bearing_to_azimuth` executed through the live named pipe with `N 45 0 0 E` and returned `azimuth_degrees: 45`, bearing text `N 45 E`, and no warnings.
- Phase 4B was packaged and installed as a 23-tool bundle, passed the offline bundle diagnostic, and was live smoke validated through the official MCP client against Civil 3D 2026. The live run exercised `survey_fbk_dry_run` without mutating a drawing.

## survey_azimuth_to_bearing

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 5 seconds |

Purpose:

- convert a survey azimuth to a quadrant bearing
- normalize azimuths into the `0 <= azimuth < 360` range

Required input:

- `azimuth_degrees`

Returns:

- `instance_id`
- normalized `azimuth_degrees`
- quadrant `bearing`

Structured errors:

- `INVALID_REQUEST`

## survey_distance

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 5 seconds |

Purpose:

- compute coordinate deltas, distance, and survey azimuth between two XY points

Required input:

- `start_x`
- `start_y`
- `end_x`
- `end_y`

Returns:

- `delta_x`
- `delta_y`
- `distance`
- `azimuth_degrees`

Structured errors:

- `INVALID_REQUEST`

## survey_point_by_azimuth

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 5 seconds |

Purpose:

- project an XY coordinate by survey azimuth and distance
- use survey convention where azimuth 0 is north, 90 is east, 180 is south, and 270 is west

Required input:

- `start_x`
- `start_y`
- `azimuth_degrees`
- `distance`

Safety behavior:

- rejects negative distance
- rejects non-finite numeric inputs
- does not access Civil 3D, COM, files, or drawing objects

Returns:

- normalized `azimuth_degrees`
- input `distance`
- projected `x`
- projected `y`

Structured errors:

- `INVALID_REQUEST`

## survey_fbk_dry_run

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 10 seconds |

Purpose:

- parse FBK text content and return a dry-run Survey import preview
- support parser iteration before any Survey Database, COM, or Civil 3D import path exists
- avoid arbitrary file reads inside the bridge by accepting inline `content`

Required input:

- `content`

Optional input:

- `source_name`

Supported Phase 4B records:

- `JOB`, `UNIT`, `EDM`, `SCALE`, `HORIZ`, `VERT`
- comments starting with `!`, `;`, or `#` are skipped
- `NEZ`
- `STN`
- `BS`
- `AZ`
- `PRISM`
- `F1`, `F2`

Returns:

- `instance_id`
- `source_name`
- `dry_run`
- `total_lines`
- `record_count`
- `control_point_count`
- `setup_count`
- `observation_count`
- `warning_count`
- parsed `records`
- `control_points`
- `setups`
- `warnings`
- `supported_keywords`

Safety behavior:

- does not read files
- does not access Civil 3D APIs
- does not use COM
- does not import Survey data
- does not mutate drawing state

Structured errors:

- `INVALID_REQUEST`

## survey_database_status

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 5 seconds |

Purpose:

- report whether a Survey Database adapter is available
- expose a safe Phase 4C entry point before broader Survey Database COM read/write implementation
- distinguish Survey geometry/FBK support from Survey Database automation support

Input:

- no required arguments

Returns:

- `instance_id`
- `survey_com_available`
- `survey_database_available`
- `requires_active_document`
- `adapter`
- `status`
- optional `working_folder`
- `interop_assemblies`
- `capabilities`
- `limitations`

Current behavior:

- the pure service default returns `adapter: "none"` and `status: "not_configured"` when no Survey Database gateway is injected
- the installed Autodesk bridge uses `adapter: "environment_probe"`
- the live Civil 3D 2026 bridge returned `status: "interop_available"`
- the live Civil 3D 2026 bridge returned `survey_com_available: true`
- returns `survey_database_available: false`
- documents that typed COM adapters are still required before broader Survey Database reads are enabled

Live validation:

- Civil 3D 2026 installed-bundle validation reported a 24-tool catalog including `survey_database_status`.
- The official MCP client called `survey_database_status` successfully and confirmed `adapter: "environment_probe"`, `status: "interop_available"`, `survey_com_available: true`, and `survey_database_available: false`.

Safety behavior:

- does not create, open, or modify Survey Databases
- does not import FBK
- does not initialize or call Survey COM in Phase 4C
- only checks known Autodesk Survey runtime files under the installed Civil 3D directory
- does not require a DWG or document lock

## survey_working_folder_get

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 10 seconds |

Purpose:

- read the current Civil 3D Survey working folder through a typed adapter
- introduce Survey COM behind a narrow read-only surface
- avoid Survey Database open/import/write behavior while validating the COM bridge path

Input:

- no required arguments

Returns on success:

- `instance_id`
- `adapter`
- `status`
- optional `working_folder`
- `requires_active_document`
- `capabilities`
- `limitations`

Current behavior:

- the pure service default returns `NOT_SUPPORTED` when no Survey Database gateway is injected
- the installed Autodesk bridge uses `adapter: "survey_com"`
- requires an active Civil 3D document before initializing the COM Survey document
- returns structured `DocumentNotFound` when the bridge reports no active document, including `adapter`, `document_count`, and `requires_active_document` details
- includes the active document descriptor on successful bridge-hosted reads

Live validation:

- Civil 3D 2026 installed-bundle validation reported a 25-tool catalog including `survey_working_folder_get`.
- The direct named-pipe call to `survey_working_folder_get` returned structured `DocumentNotFound` because the live launch reported `documentCount: 0`.
- The official MCP client live test passed against the installed 25-tool catalog and included the new tool.
- Phase 4E-A live validation confirmed the same structured no-active-document error from the Start screen, then opened a transient unsaved `Drawing1.dwg` and read `C:\Users\Public\Documents\Autodesk\Civil 3D Projects` successfully through Survey COM.

Safety behavior:

- does not create, open, or modify Survey Databases
- does not set the Survey working folder
- does not import FBK
- does not create projects, networks, figures, setups, or observations
- does not mutate drawing state
- dispatches the COM call through the Autodesk host context

## survey_project_list

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 10 seconds |

Purpose:

- list Survey projects exposed by Civil 3D Survey COM
- continue the typed Survey COM adapter path with a narrow read-only project collection surface
- avoid Survey Database open/create/import/write behavior while validating project discovery

Input:

- no required arguments

Returns on success:

- `instance_id`
- `adapter`
- `status`
- optional `working_folder`
- `count`
- `projects`
- `requires_active_document`
- `capabilities`
- `limitations`

Current behavior:

- the pure service default returns `NOT_SUPPORTED` when no Survey Database gateway is injected
- the installed Autodesk bridge uses `adapter: "survey_com"`
- requires an active Civil 3D document before initializing the COM Survey document
- returns structured `DocumentNotFound` when the bridge reports no active document
- includes the active document descriptor on successful bridge-hosted reads

Live validation:

- Civil 3D 2026 installed-bundle validation reported a 26-tool catalog including `survey_project_list`.
- The direct named-pipe call to `survey_project_list` returned structured `DocumentNotFound` while the live launch reported `documentCount: 0`.
- Phase 4E-B live validation opened a transient unsaved `Drawing1.dwg` and `survey_project_list` returned `status: "projects_available"`, `count: 0`, `working_folder: null`, and response document `Drawing1.dwg`.

Safety behavior:

- does not create, open, or modify Survey Databases
- does not set the Survey working folder
- does not import FBK
- does not create projects, networks, figures, setups, or observations
- does not mutate drawing state
- dispatches the COM call through the Autodesk host context

## propertyset_definition_list

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list the AEC property set definitions available in the drawing
- expose each definition's property list so a caller knows what data can exist before touching objects

Required input:

- `instance_id`
- `document_id`

Optional filters:

- `name_contains`

Returns:

- `count`
- definition records with `name`, `handle`, `is_style_based`, `is_visible`, `is_writeable`, `applies_to_all`, `applies_to`, `property_count`, and `properties`

Each property definition includes:

- `name`, `id`, `data_type`, `is_read_only`, `is_visible`, `is_automatic`, `units`, and `default_value`

Behavior notes:

- definitions come from the drawing's property set definition dictionary, so unused definitions are listed too
- a definition that cannot be opened is skipped rather than failing the request

## propertyset_list

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list the property sets attached to one object, with current values

Required input:

- `instance_id`
- `document_id`
- `handle`, or `object_reference` carrying a handle

Optional filters:

- `name_contains`

Returns:

- `object_handle`
- `count`
- `property_sets`, each with `name`, `handle`, `definition_handle`, `property_count`, and `properties`

Each property value includes:

- `name`, `id`, `data_type`, `value`, `numeric_value`, `units`, and `is_read_only`

Values are returned as `value`, a culture-invariant string, plus `numeric_value`, which is populated only when the underlying value is numeric. That keeps text and numbers unambiguous for callers that need to compute rather than display.

Structured errors:

- `OBJECT_NOT_FOUND` when the handle does not resolve in the drawing
- `INVALID_REQUEST` when no handle is supplied

Behavior notes:

- an object that resolves but carries no property data returns `count: 0`, which is a real answer, not an error
- an unreadable set or property is skipped so one bad record cannot fail the object

## propertyset_get

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- get one property set attached to an object, by property set name

Required input:

- `instance_id`
- `document_id`
- `handle`, or `object_reference` carrying a handle
- `name`

Returns:

- `object_handle` and `property_set`, with the same shape as `propertyset_list`

Structured errors:

- `PROPERTY_SET_NOT_FOUND` when the named set is not attached to that object
- `OBJECT_NOT_FOUND`
- `INVALID_REQUEST`

Safety behavior for these three read tools:

- read-only AutoCAD transaction
- never calls `AddPropertySet`, `RemovePropertySet`, or `PropertySet.SetAt`
- general property set writes remain deliberately unimplemented

## propertyset_numeric_test_create

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | WRITE |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | Transaction rollback before commit |
| Timeout | 15 seconds |

Purpose:

- create a controlled AEC property-set test definition
- attach it to a temporary `DBPoint`
- write one numeric `Real` property value
- read the attached set back through the same structured property-set shape used by `propertyset_get`

Required input:

- `instance_id`
- `document_id`
- `definition_name`
- `property_name`
- `numeric_value`

Optional input:

- `target_kind`, currently only `db_point`

Safety behavior:

- validates the target instance and document before writing
- requires `definition_name` to start with `MCP_NUMERIC_TEST`
- rejects control characters and AutoCAD/AEC-hostile punctuation in the generated test definition name
- rejects non-finite numeric values
- only creates the target object it attaches to; it does not update existing production objects
- locks the target document before writing
- opens an AutoCAD transaction
- commits on success and aborts before commit on failure

Returns:

- `definition_name`
- `definition_handle`
- `object_handle`
- `target_kind`
- `property_set`, including `numeric_value`
- transaction state:
  - `document_locked`
  - `transaction_started`
  - `committed`
  - `rolled_back`

Structured errors:

- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`
- `DOCUMENT_LOCK_FAILED`
- `TRANSACTION_FAILED`
- `ROLLBACK_FAILED`

Live validation:

- Civil 3D 2026 installed-bundle validation on 2026-09-14 reported a 37-tool catalog including `propertyset_numeric_test_create`.
- A live call on a temporary copy of the user-confirmed test DWG created `MCP_NUMERIC_TEST_LIVE_111759`, attached it to object handle `16FD0D`, wrote `987.654321`, and read the same numeric value back through `propertyset_get`.
- The first live retry timed out because Civil 3D was blocked by `References - Not Found Files`; the dialog was read first and then dismissed with `Ignore unresolved reference files` for the test DWG copy before retrying.

## list_dialogs

| Field | Value |
| --- | --- |
| Module | Diagnostics |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- report whether a modal dialog is blocking the Civil 3D host thread
- name the blocking dialog so an operator knows what to do about it
- answer even when every other tool is stalled

Input:

- no arguments

Returns:

- `instance_id`
- `process_id`
- `main_window_handle`, `main_window_title`, `main_window_enabled`
- `blocked_by_modal`
- `blocking_dialog_title` when blocked
- `count`
- `dialogs`, each with `handle`, `title`, `class_name`, `is_visible`, `is_enabled`, `is_modal_candidate`, `text`, `buttons`, and `content_read`
- `text` is the dialog message, joined from its static labels, or null when there is none
- `buttons[]` carries `text`, `control_id`, and `is_enabled`, in the order Windows reports them
- `content_read` is false when the dialog's controls could not be enumerated, which means its buttons are unknown, not absent
- `capabilities` and `limitations`

Detection rule:

- the main window is the visible, titled, unowned window with the largest area
- dialog candidates are visible, titled windows that have an owner
- the host is considered blocked when the main window is disabled and at least one candidate is enabled, which is exactly how Windows expresses modality

Critical behavior:

- this tool does not dispatch through the Autodesk host context, unlike every other tool in the catalog
- that is deliberate: it exists to diagnose a blocked host thread, and dispatching it would make it hang in the only case that matters
- titles are read with `SendMessageTimeout` and `SMTO_ABORTIFHUNG`, never `GetWindowText`, because the latter sends a synchronous message a hung UI thread would never answer
- enumeration failures are reported as `limitations`, never as an exception
- contents are read only for windows already classified as dialogs; the main frame's children are never walked, because it owns far too many controls to sweep while the host is hung
- button and label controls are matched by Win32 class name; edit boxes, list views, and anything else are ignored rather than stringified into something a caller cannot act on
- the child sweep of a single dialog stops at 128 controls

Live validation:

- With the `References - Not Found Files` modal up, the tool answered in 28 ms with `blocked_by_modal: true` and named that dialog.
- It flagged the enabled `#32770` dialog as the modal candidate and did not flag the disabled docked `Properties` palette.
- After dismissal it reported `blocked_by_modal: false` with `count: 0`, and `document_list` recovered. See `docs/PHASE_1C_LIVE_TEST.md`.

Safety behavior:

- enumerates windows of its own process only
- never posts input and never closes, activates, or moves a window
- does not touch the drawing, the document set, or any Autodesk API
- reports window state only; it cannot read dialog body text, so it cannot tell a harmless notification from a destructive confirmation

Related tools not implemented yet:

- `dismiss_dialog` will be WRITE class, will require the caller to pass the expected dialog title, and will refuse anything outside a reviewed allowlist
- `cancel_active_command`

## corridor_list

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list Civil 3D corridors in the target drawing
- expose rebuild state and baseline/surface counts for triage without loading full corridor detail

Required input:

- `instance_id`
- `document_id`

Optional filters:

- `name_contains` case-insensitive substring filter for corridor names

Returns:

- `count`
- corridor records with `name`, `handle`, `object_reference`, `description`, `is_out_of_date`, `rebuild_automatic`, `code_set_style_name`, `maximum_triangle_side_length`, `region_lock_mode`, `baseline_count`, and `corridor_surface_count`

Behavior notes:

- `baselines` is intentionally empty in list results; use `corridor_get` for baseline and region detail
- results are ordered by corridor name
- optional fields are read defensively and returned as null when Civil 3D cannot supply them

Live validation:

- Civil 3D 2026 installed-bundle validation reported a 32-tool catalog including `corridor_list`.
- A copy of the user-confirmed test drawing returned corridor `PAV` with 19 baselines, 2 corridor surfaces, `region_lock_mode: "GeometryLock"`, and `is_out_of_date: false`. See `docs/PHASE_2E_LIVE_TEST.md`.

Safety behavior:

- read-only AutoCAD transaction
- never calls `Corridor.Rebuild()` or any corridor mutation member
- does not mutate drawing state

## corridor_get

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- get one Civil 3D corridor with its baselines, regions, and applied assemblies
- expose the alignment and profile each baseline is built from

Required input:

- `instance_id`
- `document_id`
- one of `name`, `handle`, or `object_reference`

Returns:

- the corridor record from `corridor_list`, plus `baselines`

Each baseline includes:

- `name`
- `baseline_type`
- `alignment_name` and `alignment_handle`
- `profile_name` and `profile_handle`
- `start_station` and `end_station`, derived from the baseline's region range
- `region_count`
- `regions`

Each region includes:

- `name`
- `start_station` and `end_station`
- `assembly_name` and `assembly_handle`

Structured errors:

- `CORRIDOR_NOT_FOUND`
- `INVALID_REQUEST` when no locator is supplied
- `DOCUMENT_NOT_FOUND`

Live validation:

- A handle-based lookup returned corridor `PAV` with all 19 baselines resolved, including alignment names such as `RAMO 100` and `N3`, profile names such as `V-RAMO 100`, region counts, and applied assemblies such as `RAMO - STC` and `NARIZ`.
- A nonexistent corridor name returned structured `CORRIDOR_NOT_FOUND`.

Behavior notes:

- a baseline or region that cannot be read is skipped rather than failing the whole request
- alignment, profile, and assembly names are resolved by opening those objects read-only in the same transaction, and are null when they cannot be resolved

Safety behavior:

- read-only AutoCAD transaction
- never calls corridor, baseline, or region mutation members
- does not mutate drawing state

## profile_list

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- list Civil 3D profiles in the target drawing
- expose the parent alignment of every profile
- provide station range, elevation range, PVI count, and style for profile selection

Required input:

- `instance_id`
- `document_id`

Optional filters:

- `alignment_name` restricts the result to one parent alignment
- `name_contains` case-insensitive substring filter for profile names

Returns:

- `count`
- profile records with `name`, `handle`, `object_reference`, `description`, `alignment_name`, `alignment_handle`, `profile_type`, `starting_station`, `ending_station`, `length`, `offset`, `elevation_min`, `elevation_max`, `pvi_count`, and `style_name`

Behavior notes:

- profiles are enumerated through their parent alignments, so a drawing with no alignments returns zero profiles
- results are ordered by alignment name and then profile name
- an alignment whose profile collection cannot be read is skipped rather than failing the whole listing
- optional fields are read defensively and returned as null when Civil 3D cannot supply them

Live validation:

- Civil 3D 2026 installed-bundle validation reported a 30-tool catalog including `profile_list`.
- A copy of the user-confirmed test drawing returned 59 profiles across 32 alignments, with `EG` and `OffsetProfile` types, PVI counts, and style names. See `docs/PHASE_2D_LIVE_TEST.md`.

Safety behavior:

- read-only AutoCAD transaction
- does not create, edit, or delete profiles
- does not mutate drawing state

## profile_get

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- get one Civil 3D profile by name, handle, or object reference

Required input:

- `instance_id`
- `document_id`
- one of `name`, `handle`, or `object_reference`

Optional input:

- `alignment_name` disambiguates profile names repeated across alignments

Returns:

- one profile record with the same shape as `profile_list`

Structured errors:

- `PROFILE_NOT_FOUND` when the locator matches nothing
- `PROFILE_AMBIGUOUS` when a name matches profiles under more than one alignment and no `alignment_name` or `handle` was supplied; the message lists the competing alignments
- `INVALID_REQUEST` when no locator is supplied
- `DOCUMENT_NOT_FOUND`

Live validation:

- A handle-based lookup returned `TN_DRONE (27)` under alignment `BD - 100`.
- A nonexistent profile name returned structured `PROFILE_NOT_FOUND`.

## profile_elevation_at_station

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- read a profile elevation at a station
- also report the profile grade at that station when Civil 3D can supply it

Required input:

- `instance_id`
- `document_id`
- one of `name`, `handle`, or `object_reference`
- `station`

Optional input:

- `alignment_name` disambiguates profile names repeated across alignments

Returns:

- the resolved profile record
- `station`
- `elevation`
- `grade` when readable, otherwise null

Structured errors:

- `PROFILE_NOT_FOUND`
- `PROFILE_AMBIGUOUS`
- `INVALID_REQUEST` when the locator is missing or `station` is not a finite number
- `CIVIL_QUERY_FAILED` when Civil 3D cannot evaluate the station, for example outside the profile station range

Live validation:

- At station 2178.913 on `TN_DRONE (27)` the tool returned elevation 280.856 and grade -0.216.

Safety behavior:

- read-only AutoCAD transaction
- does not modify the profile or the drawing

## survey_network_list

| Field | Value |
| --- | --- |
| Module | Survey |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 10 seconds |

Purpose:

- list Survey networks exposed by Civil 3D Survey COM for one or all Survey projects
- extend the typed Survey COM adapter from project discovery to network discovery
- map the Survey object model before any Survey import or write phase

Input:

- `project_name` optional string; when omitted, networks from every Survey project are listed

Returns on success:

- `instance_id`
- `adapter`
- `status`
- optional `project_name` echoing the requested filter
- optional `working_folder`
- `count`
- `networks`
- `requires_active_document`
- `capabilities`
- `limitations`
- `warnings`

Each network record includes:

- `name`
- optional `project_name`
- optional `description`
- optional `path`
- optional `id`

Status values:

- `networks_available` when every matched project's networks were read
- `networks_partial` when at least one project's networks could not be read; the reason is reported in `warnings`
- `no_projects` when the Survey working folder exposes no projects

Current behavior:

- the pure service default returns `NOT_SUPPORTED` when no Survey Database gateway is injected
- the installed Autodesk bridge uses `adapter: "survey_com"`
- requires an active Civil 3D document before initializing the COM Survey document
- returns structured `DocumentNotFound` when the bridge reports no active document
- returns structured `SURVEY_PROJECT_NOT_FOUND` when `project_name` matches no Survey project
- when `project_name` is supplied and that project's networks cannot be read, returns `NOT_SUPPORTED` rather than an empty success
- when no filter is supplied, an unreadable project becomes a warning and the remaining projects are still reported
- optional network fields are read defensively and are returned as null when Survey COM cannot supply them
- includes the active document descriptor on successful bridge-hosted reads
- dispatches the COM call through the Autodesk host context

Live validation:

- Civil 3D 2026 installed-bundle validation reported a 27-tool catalog including `survey_network_list`.
- The direct named-pipe call returned structured `DocumentNotFound` with `document_count: 0` while the live launch had no active document.
- Phase 4E-C live validation opened a transient unsaved `Drawing1.dwg` and `survey_network_list` returned `status: "no_projects"`, `count: 0`, `adapter: "survey_com"`, and response document `Drawing1.dwg`.
- An unmatched `project_name` returned `SURVEY_PROJECT_NOT_FOUND` with `project_count: 0` in the error details.
- The live host had no Survey projects, so network item enumeration is currently covered by unit and fake-bridge tests only. See `docs/PHASE_4E_C_LIVE_TEST.md`.

Safety behavior:

- does not create, open, or modify Survey Databases
- does not open Survey projects
- does not set the Survey working folder
- does not import FBK
- does not create or remove networks, figures, setups, or observations
- never calls `IAeccSurveyNetworks.Create` or `IAeccSurveyNetworks.Remove`
- does not mutate drawing state

## dialog_decision_list

| Field | Value |
| --- | --- |
| Module | Diagnostics |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- show what the operator has already decided about each dialog
- show any dialog on screen that still needs a decision, with the options to choose from

Required input:

- none

Returns:

- `count` and `decisions[]`, each with `dialog_title`, `button_text`, `scope`, `created_utc`, and `note`
- `pending[]`, each with `dialog_title`, `text`, `outcome`, and `options[]`
- `store_path`, where persisted defaults live

Outcomes reported for a pending dialog:

- `DecisionRequired` - no decision applies, or the one-time decision was already used
- `ButtonNotPresent` - a decision exists but names a button that is absent or disabled
- `ContentUnreadable` - the dialog's controls could not be read, so its buttons are unknown rather than absent

Like `list_dialogs`, this tool does not dispatch through the Autodesk host context. It exists for the case where that context is blocked by the very dialog being decided.

## dialog_decision_set

| Field | Value |
| --- | --- |
| Module | Diagnostics |
| Operation class | WRITE |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- record the operator's decision for one dialog

This writes operator configuration, never the drawing. That is why it carries no rollback semantics and never opens a transaction.

Required input:

- `dialog_title`, matched exactly
- `button_text`, matched exactly
- `scope`, either `Default` or `Once`

Optional input:

- `note`, why the operator chose this

Scope:

- `Default` stands for every future occurrence and is persisted, so it survives a restart
- `Once` is good for one occurrence, is never written to disk, and the operator is asked again next time

Returns:

- the stored `decision`
- `persisted`, false for a one-time answer
- `count` of decisions now held

Safety behavior:

- a decision names one dialog and one button; both are matched exactly, because a loose match eventually presses a button nobody chose
- recording a decision for a dialog replaces any previous one
- a decision is never invented: with none recorded, the dialog is left alone and the options are handed back

Structured errors:

- `INVALID_REQUEST` when `dialog_title` or `button_text` is missing

## dismiss_dialog

| Field | Value |
| --- | --- |
| Module | Diagnostics |
| Operation class | WRITE |
| Risk | Medium |
| Requires confirmation | No |
| Rollback semantics | Not supported |
| Timeout | 15 seconds |

Purpose:

- close one dialog by pressing the button the operator decided on

This is the only tool that acts on the host UI. Risk is Medium because the caller chooses the button and the platform cannot know what that button does; rollback is not supported because a pressed button cannot be unpressed.

Required input:

- `expected_title`, the exact title of the dialog the caller intends to close

Optional input:

- `button_text` and `scope`, to decide and apply in the same call
- `note`

Returns:

- `dialog_title`
- `pressed`, false whenever the platform declined to act
- `outcome`, the reason when nothing was pressed
- `button_text` and `scope` of the decision that was applied
- `options[]`, whenever a decision is still needed
- `message`, the refusal in plain words

Safety behavior:

- `expected_title` is required; the caller states which dialog they mean rather than closing whatever is on screen
- a title matching no dialog fails with `DIALOG_NOT_FOUND`; a title matching more than one fails with `DIALOG_AMBIGUOUS`; neither guesses
- with no decision recorded, nothing is pressed and the options come back
- the decision names one button; if it is absent or disabled nothing is pressed, and no other button is substituted
- an unreadable dialog is left alone, because its buttons are unknown rather than absent
- the controls are read again immediately before pressing, so a button that vanished in between is reported rather than pressed through a stale handle
- a decision is recorded only after the dialog is confirmed to exist
- the click is posted, not sent: the host main thread is blocked by the very modal being closed, and a synchronous send could park the caller behind it
- a click the host did not accept fails with `DIALOG_DISMISS_FAILED` rather than being reported as success
- like the other dialog tools, it does not dispatch through the Autodesk host context

Structured errors:

- `INVALID_REQUEST` when `expected_title` is missing
- `DIALOG_NOT_FOUND`
- `DIALOG_AMBIGUOUS`
- `DIALOG_DISMISS_FAILED`

## object_get

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- identify any drawing object by handle, including Civil 3D and AEC classes that have no typed tool

Required input:

- `instance_id`
- `document_id`
- `handle`, or `object_reference` carrying a handle

Handle is the only locator. A name would be ambiguous across object classes, and these tools exist precisely for classes the platform cannot resolve by name.

Returns:

- `object.handle`, `object.object_id`
- `object.object_class`, `object.dxf_name`, `object.app_name`
- `object.class_hierarchy`, parent classes from the immediate base upward
- `object.is_entity`, `object.is_erased`, `object.is_proxy`, `object.is_persistent`
- `object.owner_handle`, `object.owner_class`
- `object.object_reference`
- `object.entity` when the object is an entity: `layer`, `linetype`, `linetype_scale`, `lineweight`, `color`, `color_index`, `transparency_alpha`, `is_visible`, `block_name`, and `extents` when the entity reports them

`object.entity` is null for non-entities. `object.entity.extents` is null when the entity raises `eNullExtents`, which is normal for an empty block reference.

Structured errors:

- `OBJECT_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`
- `INSTANCE_NOT_FOUND`

## object_properties

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 30 seconds |

Purpose:

- list the readable properties of any drawing object, so an object can be investigated before a typed tool exists for its class

Required input:

- `instance_id`
- `document_id`
- `handle`, or `object_reference` carrying a handle

Optional input:

- `name_contains`, case-insensitive substring filter for property names
- `include_unreadable`, default false; when true, properties whose value could not be read are returned with an `error` field

Returns:

- `handle`, `object_class`
- `count`, the number of properties returned after filtering
- `truncated`, true when enumeration stopped before the end of the property list
- `properties[]` with `name`, `declaring_type`, `data_type`, `value`, `numeric_value`, `is_read_only`, and `error`

`value` is always invariant text and `numeric_value` is the separate numeric form, the same convention the property-set tools use, so a caller computing stations never reparses localized text.

Reads are bounded and defensive:

- the object is opened `ForRead`, so AutoCAD itself rejects any getter that attempts to mutate it
- a getter that throws produces a row carrying the reason rather than a missing row or an invented value
- a value with no honest scalar form is omitted rather than stringified
- enumeration stops at 500 properties and sets `truncated`

Structured errors:

- `OBJECT_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`
- `INSTANCE_NOT_FOUND`

## propertyset_find

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- find the objects that carry a named AEC property set, optionally narrowed by value

Every other property-set tool takes a handle, which meant they could only be used by a caller who already had one. This is the read that produces handles in the first place, and it is what makes `propertyset_set` usable at scale.

Required input:

- `instance_id`, `document_id`
- `set_name`

Optional input:

- `match[]`, value filters combined with AND; each needs `name` plus either `equals` (exact, case-insensitive) or `contains` (substring, case-insensitive)
- `limit`, maximum objects to return

Returns:

- `set_name`, `count`, `matched_count`, `scanned_count`, `truncated`
- `objects[]`, each with `handle`, `object_class`, and the full `property_set`

Three counts, because they answer different questions: `count` is what came back, `matched_count` is what matched before the limit, and `scanned_count` is how many objects the sweep looked at, so a caller can judge whether the sweep really covered the drawing.

Safety and honesty behavior:

- a filter with a name but no `equals` and no `contains` is rejected; a filter that constrains nothing would silently widen the result
- an object that does not carry the filtered property does not match, rather than being treated as a match
- no match is an empty result with a real `scanned_count`, never an error
- `truncated` is true when the limit cut the list **or** when the sweep stopped before the end of the drawing, so a prefix is never mistaken for the whole answer
- every block table record is walked, not only model and paper space, so an object carrying the set inside a block definition is still found
- one unreadable entity is skipped, not fatal: one bad object out of four thousand must not cost the caller the rest
- the sweep is capped at 50,000 objects, since it runs on the Autodesk idle loop

Optional `max_scan` sets the sweep budget; the default is 250,000.

**Why the budget belongs to the caller.** Live validation first ran with a fixed 50,000 ceiling and hit it on `Way-153`, returning `truncated: true`. Re-run with the raised default, the sweep covered all 94,824 objects in that drawing and found the same 526 carriers. So the earlier count was already correct, but the platform had no way to know it was: the carriers happened to sit inside the first 50,000. Any fixed number is reachable on a large enough drawing, and only the person waiting on Civil 3D can judge how long a sweep may take, so `max_scan` is a default rather than a limit.

Check `truncated` before concluding anything from a count. When it is true on an unfiltered find, raise `max_scan` or narrow the filter.

Structured errors:

- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`
- `CIVIL_QUERY_FAILED`

Validation status: **live validated** against Civil 3D 2026 on 2026-09-19. 526 carriers found in `Way-153`, matching the Phase 2F report by an independent path. Filters proven on real data: an existing value returned one object, a non-existent value returned `count: 0` with a real `scanned_count`, and a filter with no criterion returned `INVALID_REQUEST`.

## propertyset_set

| Field | Value |
| --- | --- |
| Module | Civil3D |
| Operation class | WRITE |
| Risk | Medium |
| Requires confirmation | **Yes** |
| Rollback semantics | Transaction rollback before commit |
| Timeout | 30 seconds |

Purpose:

- update values in an AEC property set already attached to one object

This is the first tool in the platform that requires confirmation. Every earlier write was bounded to something the platform had just created itself, a new layer or a temporary `DBPoint`. This one changes data an engineer put in the drawing.

Required input:

- `instance_id`
- `document_id`
- `handle` of the object carrying the set
- `set_name` of the attached property set
- `properties[]`, each with `name` and `value`, at least one

Values are invariant text, matching how the read tools report them. They are converted to the property's declared AEC type before writing: handing a string to a `Real` property is how a number silently becomes text in the drawing.

Returns:

- `applied`, `changed_count`
- `properties[]`, each with `name`, `data_type`, `previous_value`, `previous_numeric_value`, `new_value`, `new_numeric_value`, `changed`
- `transaction` state

The "after" values are read back from the drawing rather than echoed from the request, so the caller is told what the file now holds.

Confirmation flow:

1. Call without a confirmation token. Nothing is written; the call fails with `CONFIRMATION_REQUIRED` and the message is the preview, naming the drawing, the object, the set, and every before and after pair.
2. Confirm that exact preview and call again with the token.

The token is bound to the tool, the instance, the document and a hash of the request body. The token travels in the request envelope and not in the arguments, so a token can never be part of what it is confirming. Confirming one change does not authorize another.

Safety behavior:

- only updates an attached set; never calls `AddPropertySet`
- only updates an existing property; never adds one
- refuses a read-only property
- refuses a request that would change nothing, because approving a no-op write trains an operator to approve without reading
- refuses a property named twice in one request
- `affected_count` in the confirmation counts only the values that would actually change
- re-resolves the object and the set immediately before writing, so anything that changed between preview and write fails instead of being written blind
- document lock, transaction, commit on success, abort before commit on failure

Structured errors:

- `OBJECT_NOT_FOUND`
- `PROPERTY_SET_NOT_FOUND`
- `PROPERTY_NOT_FOUND`
- `CONFIRMATION_REQUIRED`
- `CONFIRMATION_INVALID`
- `CONFIRMATION_EXPIRED`
- `INVALID_REQUEST`
- `DOCUMENT_LOCK_FAILED`
- `TRANSACTION_FAILED`
- `ROLLBACK_FAILED`

Validation status: **live validated** against Civil 3D 2026 on 2026-09-19. See `docs/PHASE_3E_6A_LIVE_TEST.md`. Proven on real BR-153 data: `Dados_Rodovia.Secao` from `529` to `529-TESTE-MCP`, with an exact preview, `committed: true` and a readback confirming. The no-op guard also fired: repeating an applied write returned `INVALID_REQUEST` without asking for confirmation.

## roadway_station_offset_batch

| Field | Value |
| --- | --- |
| Module | **Roadway** |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 60 seconds |

Purpose:

- station and offset for a list of points against one alignment, in one call

First tool of the roadway module. It replaces reading points into Civil 3D one at a time to build a station/offset table.

Required input:

- `instance_id`, `document_id`
- `alignment`, the name as `alignment_list` reports it
- `points[]`, each with `x` and `y`, and an optional `label` echoed back untouched

Returns:

- `alignment`, `count`, `resolved_count`, `failed_count`
- `points[]`, each with `index`, `x`, `y`, `label`, `resolved`, `station`, `offset`, `error`

Every row carries its request `index` and its original coordinates, so a caller can match results without relying on order.

Behaviour that defines this tool:

- **A batch is not all-or-nothing.** A point outside the alignment is an ordinary outcome in roadway work, so it is reported on its own row with `resolved: false` and a reason, and every other point still comes back. `failed_count` greater than zero is a normal result, not an error.
- A missing alignment fails **once**, before any point is touched: that is one caller mistake, not forty bad points.
- A batch is capped at 2000 points. This runs on the Autodesk idle loop, so an unbounded list would freeze the application the operator is using.

Layering: the per-point geometry is the same code `alignment_station_offset` uses and that was validated live, reached through a narrow port. The roadway module owns the batch and carries no Autodesk reference of its own.

Structured errors:

- `ALIGNMENT_NOT_FOUND`
- `INVALID_REQUEST`
- `DOCUMENT_NOT_FOUND`
- `INSTANCE_NOT_FOUND`

Validation status: **live validated** against Civil 3D 2026 on 2026-09-19. See `docs/PHASE_3E_6A_LIVE_TEST.md`. The batch round-tripped to 11 decimals against `alignment_station_offset`, proving the shared port did not drift from the single-point tool.

## roadway_propertyset_write

| Field | Value |
| --- | --- |
| Module | **Roadway** |
| Operation class | WRITE |
| Risk | Medium |
| Requires confirmation | **Yes** |
| Rollback semantics | Transaction rollback before commit |
| Timeout | 120 seconds |

Purpose:

- update one property set across many objects, in one atomic write

**Name under review.** This tool contains no roadway logic: it takes a set name, handles and name/value pairs, and works identically with any property set. It sits in the Roadway module by accident of where it was built. It should be `propertyset_set_batch` in the Civil module. See `docs/CONSOLIDATION_REVIEW.md`, defect D4.

Required input:

- `instance_id`, `document_id`
- `set_name`, the property set to update on every listed object
- `objects[]`, each with `handle` and a `properties[]` of `name` / `value` pairs

Returns:

- `set_name`, `applied`, `object_count`, `changed_count`, `skipped_count`
- `objects[]`, each with `handle`, `changed`, and the per-property before/after
- `transaction`, with `document_locked`, `transaction_started`, `committed`, `rolled_back`

Behaviour that defines this tool:

- **All or nothing.** Reading half a list is still useful; 526 half-updated objects is a worse state than none, and nobody can tell by looking which half went through. Every object is resolved and validated before anything is written, and one failure aborts the lot.
- **Explicit handles, never a filter.** The caller runs `propertyset_find`, looks at what came back, and passes that list. The platform therefore cannot expand a query into a write the operator never saw.
- **The preview is summarised, not line by line.** A 526-line preview is a preview nobody reads, which transfers responsibility without transferring understanding. It leads with the scale, names the set and the drawing, and shows three real before/after pairs.
- Objects already holding the requested values are reported as `changed: false` and not written.
- Capped at 5000 objects, since this runs on the Autodesk idle loop.

Refused: a handle duplicated within one batch (the outcome would depend on ordering), a batch where nothing would change, an object not carrying the set, an undefined property, a read-only property.

Confirmation: the `CONFIRMATION_REQUIRED` error carries `preview`, `affected_count` and `confirmation_token` in its `details`. A caller told to confirm needs the thing to confirm with.

Structured errors:

- `CONFIRMATION_REQUIRED`, `CONFIRMATION_INVALID`, `CONFIRMATION_EXPIRED`
- `OBJECT_NOT_FOUND`, `PROPERTY_SET_NOT_FOUND`, `PROPERTY_NOT_FOUND`
- `INVALID_REQUEST`, `DOCUMENT_NOT_FOUND`, `INSTANCE_NOT_FOUND`

Validation status: **live validated** against Civil 3D 2026 on 2026-09-19. Three objects updated atomically after confirmation. Atomicity was proven the only way a batch can prove it: a batch with a valid handle followed by an invalid one failed with `OBJECT_NOT_FOUND` and left the valid object at its previous value.

## block_list

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 30 seconds |

Purpose:

- list block definitions, with their attribute definitions and insert counts

Blocks are plain AutoCAD entities, so this tool answers in both project moments, geometric and exported. See `docs/PROJECT_PHASES_GEOMETRIC_EXPORTED.md`.

Optional input:

- `name_contains`, case-insensitive
- `include_anonymous`, default false: anonymous definitions (*U, *D, *X...) are hidden
- `include_layouts`, default false: model space and paper space records are hidden

Returns:

- `count`, `blocks[]`, each with `name`, `handle`, `is_anonymous`, `is_dynamic`, `is_xref`, `is_overlay`, `is_layout`, `is_dependent`, `has_attribute_definitions`, `attribute_definitions[]` (`tag`, `prompt`, `default_value`, `constant`, `invisible`, `multiline`), `reference_count`, `comments`, `units`

`reference_count` for a dynamic block **includes inserts made through its anonymous representations**, which is where most dynamic inserts point. Counting the dynamic definition alone would report near zero.

Validation status: **live validated** 2026-09-24 on both drawings of the reference pair. Geometric: 257 definitions shown, 917 in all. Exported: 271 shown, 2,131 in all. See `docs/PHASE_2J_LIVE_TEST.md`.

## block_reference_list

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 60 seconds |

Purpose:

- list block inserts in model space and every paper space layout, filtered by block name or layer

Optional input:

- `block_name`: exact, case-insensitive. **Matches a dynamic block by its own name**, although its inserts point at anonymous definitions such as *U12
- `layer`: exact, case-insensitive
- `include_anonymous`, default false. An anonymous block named in `block_name` is always included
- `include_attributes`, default false. When true, each insert carries its attributes, including the definition's constant attributes
- `max_scan`, default 250,000 entities; must be greater than zero
- `max_results`, default 2,000; between 1 and 10,000

Returns:

- `count`, `scanned_count`, `truncated`, `truncated_by` (`scan_budget` or `result_limit`), `references[]`
- each reference: `handle`, `block_name` (effective), `definition_name` (what the insert points at), `is_dynamic`, `is_anonymous`, `layer`, `space`, `x`, `y`, `z`, `rotation_radians`, `scale_x`, `scale_y`, `scale_z`, `attribute_count`, `attributes`

Behaviour that defines this tool:

- **Dynamic inserts are found by their real name.** In the reference drawings they are the majority: 66 of 104 inserts in the geometric drawing, 1,372 of 1,961 in the exported one.
- **A cut list says so.** A spent scan budget outranks the result limit, because it means entities were never looked at.
- **Only block inserts are opened.** Entities are filtered by runtime class before opening, so 38,179 entities are swept in 68 ms.
- Tables are block references internally and are excluded. Inserts nested inside other block definitions are part of those definitions and are not listed.

Validation status: **live validated** 2026-09-24 on both drawings. Result limit and scan budget both fired on real data. See `docs/PHASE_2J_LIVE_TEST.md`.

## block_get_attributes

| Field | Value |
| --- | --- |
| Module | AutoCAD |
| Operation class | READ |
| Risk | Low |
| Requires confirmation | No |
| Rollback semantics | None required |
| Timeout | 15 seconds |

Purpose:

- read the attributes of one block insert by handle

Required input:

- `handle` or `object_reference.handle`

Returns:

- `handle`, `block_name`, `definition_name`, `is_dynamic`, `count`, `attributes[]` (`tag`, `value`, `handle`, `constant`, `invisible`, `multiline`)

**Constant attributes are included.** An insert never carries them; their value exists only on the definition. They are appended after the insert's own attributes, with `constant: true` and a null `handle`.

Structured errors:

- `OBJECT_NOT_FOUND` when the handle does not resolve
- `INVALID_REQUEST` when it resolves to something else, **naming the real class**, for example `AcDbBlockTableRecord`

Validation status: **live validated** 2026-09-24 on both drawings. The constant-attribute path is unit tested but did not run live: neither drawing has a block with a constant attribute definition. See `docs/PHASE_2J_LIVE_TEST.md`.

## Not Implemented Yet

The following are intentionally not implemented:

- object select and object delete
- generic object writes
- `cancel_active_command`

- layer delete
- COGO writes
- alignment writes
- surface writes
- Survey Database/COM reads beyond readiness/status, working-folder read, and project list
- FBK import
- Roslyn/Developer Mode
- Roadway beyond `roadway_station_offset_batch` and `roadway_propertyset_write`
- block writes: `block_set_attribute`, `block_replace`

## MCP Result Envelope

MCP calls return structured content with:

- `success`
- `tool`
- `correlationId`
- `data`
- `document` when available
- `durationMs` when available
- `warnings`
- `error` when failed

External error codes use screaming-snake case such as `BRIDGE_UNAVAILABLE`, while `internalCode` preserves the platform enum name such as `BridgeUnavailable`.
