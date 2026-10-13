# Surface TIN Correction Tools Design

Date: 2026-09-30

## Context

The operator needs a controlled audit and correction workflow for Civil 3D TIN surfaces, first driven by the live surface `TN RTK` (`handle: 20E98B`, layer `_MP TN GERAL`). The current MCP bridge can read surfaces through `surface_list`, `surface_get`, and `surface_elevation_at_xy`, but it has no MCP tools for copying, rebuilding, adding boundaries, adding breaklines, swapping edges, or deleting TIN lines. Developer Mode is not implemented in this build and must not be treated as a workaround.

The live read-only audit of `TN RTK` found:

- 158 points by MCP surface metadata; 159 coordinate triples from COM `Points`.
- 258 TIN triangles.
- 414 unique TIN edges.
- 54 boundary edges and 360 interior edges.
- 0 breaklines and 0 boundaries in the surface definition.
- 4 long edges above the computed threshold of about 30.81 m.
- 47 triangles with aspect ratio greater than 10.
- Largest edge about 92.346 m, between COGO points 31 and 8.

## Goal

Add typed, safe MCP tools that let an agent audit and correct a Civil 3D TIN surface through a copy-first workflow, preserving the original production surface unless the operator explicitly chooses otherwise in a future tool.

## Non-Goals

- Do not implement broad Developer Mode or arbitrary C# execution for this workflow.
- Do not mutate the original `TN RTK`.
- Do not delete COGO points, move source points, change observed elevations, purge drawings, or alter unrelated Civil 3D objects.
- Do not use UI automation for surface edits while Civil 3D .NET API or COM APIs can provide a programmatic path.

## Safety Model

Surface tools are split into three groups:

- READ: audit TIN triangles, edges, definition sources, and correction candidates.
- WRITE: create a copied surface, set build options on a target copied surface, add a boundary, add a breakline, rebuild.
- DESTRUCTIVE: delete TIN lines or perform swap-edge operations. These require preview and confirmation even when applied only to a copied surface.

Every WRITE/DESTRUCTIVE surface tool must:

1. Require `instance_id`, `document_id`, and a surface locator.
2. Resolve the target by name or handle and verify object type.
3. Reject edits to a protected source when `protect_source_name` or `protect_source_handle` matches the target.
4. Use document lock and an Autodesk transaction where the API supports it.
5. Return a structured response with affected counts, before/after summary where possible, and warnings.
6. Log tool, document, surface, affected count, confirmation token when used, and duration.

## Tool Set

### `surface_audit_tin`

READ/Low. Returns points, triangles, unique edges, edge classification, length, grade, area, aspect ratio, source definition counts, and candidate problem lists.

Inputs:

- `instance_id`, `document_id`
- surface locator: `name`, `handle`, or `object_reference`
- optional thresholds: `long_edge_threshold_m`, `aspect_ratio_threshold`, `include_edges`, `include_triangles`, `max_items`

Outputs:

- `SurfaceTinAuditResponse`
- `summary`
- `edges`
- `triangles`
- `long_edges`
- `skinny_triangles`
- `source_definition`
- `warnings`

Probable Autodesk APIs to confirm by metadata before implementation:

- `Autodesk.Civil.DatabaseServices.TinSurface`
- `TinSurface.GetTriangles()` or equivalent triangle enumeration
- `TinSurface.GetVertices()` or equivalent point/vertex enumeration
- `Surface.GetGeneralProperties()`
- definition/source collections for boundaries, breaklines, point groups, and operations

If .NET does not expose TIN triangle enumeration, the implementation may use the already verified Civil 3D COM read-only members `OutputTriangles`, `Points`, `Statistics`, `Breaklines`, `Boundaries`, and `PointGroups`, but must document that fallback in the response.

### `surface_copy`

WRITE/Medium. Creates a new surface from an existing surface with a requested name. This is the gate for all correction work.

Inputs:

- `instance_id`, `document_id`
- source locator
- `new_name`
- optional `target_layer`, `target_style_name`, `copy_description`
- optional `fail_if_exists`, default true

Outputs:

- `SurfaceCopyResponse`
- source and copy references
- copied source definition summary

Probable Autodesk APIs to confirm:

- `TinSurface.Create(...)`
- `TinSurface.PasteSurface(...)`
- Civil 3D COM fallback candidates: `IAeccTinSurface.Copy()` and `PasteSurface(IAeccTinSurface)`

### `surface_rebuild`

WRITE/Medium. Rebuilds one target surface, normally the copied surface, and returns before/after audit summary.

Probable Autodesk API:

- `Surface.Rebuild()` / `TinSurface.Rebuild()`

### `surface_set_build_options`

WRITE/Medium. Sets bounded build options, initially `UseMaximumTriangleLength` and `MaximumTriangleLength`, on a copied target surface.

Inputs:

- target surface locator
- `use_maximum_triangle_length`
- `maximum_triangle_length_m`
- protected source name/handle

Probable Autodesk APIs:

- surface build/definition properties for maximum triangle length
- COM fallback candidate verified read-only: `DefinitionProperties.UseMaximumTriangleLength` and `DefinitionProperties.MaximumTriangleLength`

### `surface_boundary_preview`

READ/Low. Computes candidate outer boundaries from audited TIN edges and point distribution. Does not create geometry and does not alter a surface.

Inputs:

- audit thresholds and alpha/concavity parameters
- optional selected edge ids or point numbers

Outputs:

- candidate polylines as coordinate rings
- doubtful spans
- confidence level

### `surface_add_outer_boundary`

WRITE/Medium, confirmation recommended. Adds a provided outer boundary to a copied target surface and rebuilds only if requested.

Inputs:

- target copied surface locator
- boundary vertices
- `mid_ordinate_distance`
- `non_destructive_breakline`, if supported by API
- protected source name/handle

Probable Autodesk APIs:

- `SurfaceDefinitionBoundaries.AddBoundaries(...)`
- `SurfaceBoundaryType.Outer`

### `surface_breakline_candidates`

READ/Low. Groups COGO points by raw/full description, numerical sequence, spatial continuity, and direction to identify likely feature lines. Does not write.

Inputs:

- point group or point list
- descriptions to include/exclude
- maximum gap distance
- maximum direction change

Outputs:

- candidate sequences with confidence and warnings

### `surface_add_standard_breakline`

WRITE/Medium, confirmation recommended. Creates a 3D polyline or feature line from an explicit vertex sequence and adds it as a standard breakline to a copied surface.

Probable Autodesk APIs:

- `SurfaceDefinitionBreaklines.AddStandardBreaklines(...)`
- AutoCAD `Polyline3d` or Civil 3D feature line API for geometry creation

### `surface_tin_edit_preview`

READ/Low. Previews swap-edge and delete-line candidates from an audit. Produces explicit edge ids, point pairs, and evidence. Does not write.

### `surface_swap_edge`

DESTRUCTIVE/Medium, confirmation required. Applies one or more explicit edge swaps to a copied surface only, then rebuilds or audits.

Probable Autodesk APIs:

- Tin surface operation or edit API for edge swap, to be confirmed by metadata.

### `surface_delete_tin_line`

DESTRUCTIVE/Medium, confirmation required. Deletes explicit TIN lines from a copied surface only.

Probable Autodesk APIs:

- Tin surface operation or edit API for deleting TIN lines, to be confirmed by metadata.

## Workflow

For `TN RTK` correction:

1. `surface_get` validates `TN RTK` and handle `20E98B`.
2. `surface_audit_tin` captures the initial state.
3. `surface_copy` creates `TN RTK_CORRIGIDA`.
4. `surface_audit_tin` verifies the copy matches the original before edits.
5. `surface_boundary_preview` proposes an outer boundary. If confidence is high, `surface_add_outer_boundary` applies it to the copy.
6. `surface_rebuild`.
7. `surface_breakline_candidates` proposes feature sequences. High confidence candidates may be added with `surface_add_standard_breakline`.
8. `surface_rebuild`.
9. `surface_set_build_options` may enable maximum triangle length only after preview shows it removes unsupported extrapolation rather than valid interior terrain.
10. `surface_rebuild`.
11. `surface_tin_edit_preview` identifies remaining swap/delete candidates.
12. `surface_swap_edge` and `surface_delete_tin_line` are used only for explicit, confirmed, high-evidence edits on the copy.
13. Final `surface_audit_tin` compares original and corrected surfaces.

## Validation

Unit tests cover pure audit math: edge de-duplication, boundary/interior classification, percentile thresholds, triangle aspect ratio, boundary candidate confidence, and breakline sequence grouping.

Integration tests cover MCP schema publication, fake bridge execution, confirmation requirements, protected source rejection, and structured errors.

Live validation must run on a copied test DWG, never the original production DWG. The live test must prove that `TN RTK` remains unchanged and all writes target `TN RTK_CORRIGIDA`.
