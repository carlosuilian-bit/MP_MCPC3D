# Surface TIN Correction Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build typed MCP tools for safe Civil 3D TIN surface audit and copy-first correction, starting with the `TN RTK` use case.

**Architecture:** Extend the existing `Civil3D.HybridMcp.Tools.Civil` module with read-only TIN audit logic, then add guarded write tools that refuse to edit the protected source surface and require a copied target. Autodesk API calls stay inside the bridge adapters; pure classification, thresholding, and candidate generation stay testable outside Civil 3D.

**Tech Stack:** C#/.NET 10 for Autodesk-facing projects, .NET 8 for non-Autodesk projects, xUnit, Civil 3D .NET API, AutoCAD .NET API, optional Civil 3D COM fallback only when .NET lacks a read/write surface.

**Spec:** `docs/superpowers/specs/2026-09-30-surface-tin-correction-tools-design.md`

## Global Constraints

- Never edit `TN RTK` directly; correction tools must target `TN RTK_CORRIGIDA` or another explicit copied target.
- Developer Mode/Roslyn is not implemented and is not an acceptable workaround for this workflow.
- Do not delete COGO points, move source points, change observed elevations, purge drawings, or alter unrelated Civil 3D objects.
- Every write must use document target validation, document lock, transaction/rollback when supported, structured errors, request logging, and confirmation where the operation is medium risk or destructive.
- Confirm Autodesk API members through metadata or SDK inspection before implementation; do not invent Civil 3D API calls.
- Live validation must use a copied test DWG and must document that the original surface is unchanged.

## Review Focus

- Protected-source rejection: every write must fail when the target matches `TN RTK`/`20E98B`; Task 3, Task 4, Task 6, and Task 7 each include a test.
- Duplicate corrected surface name: `surface_copy` must reject `TN RTK_CORRIGIDA` if it already exists unless an explicit replace strategy is later designed; Task 3 tests this.
- Missing Autodesk API member: each write task starts with a metadata confirmation step and records a structured `NOT_SUPPORTED` path if the API is absent.
- Boundary confidence: `surface_add_outer_boundary` must accept explicit vertices only; generated candidate boundaries are preview data, not automatic writes; Task 5 tests this.
- TIN destructive edits: `surface_swap_edge` and `surface_delete_tin_line` must require explicit edge ids and confirmation, and must refuse filter-based mass edits; Task 7 tests this.

---

### Task 1: Surface TIN Audit Contracts and Pure Analyzer

**Files:**
- Modify: `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilToolCatalog.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs`
- Test: `tests/Civil3D.HybridMcp.Tools.Civil.Tests/CivilToolTests.cs`

**Interfaces:**
- Consumes: existing `DocumentDescriptor`, `CivilObjectLocator`, `CivilObjectGetRequest`, and surface locator schema conventions.
- Produces: `SurfaceTinAuditRequest`, `SurfaceTinAuditResponse`, `SurfaceTinEdgeInfo`, `SurfaceTinTriangleInfo`, `SurfaceTinSourceDefinitionInfo`, `SurfaceTinAuditAnalyzer.Analyze(...)`, and catalog name `surface_audit_tin`.

- [ ] **Step 1: Write failing contract serialization tests**

Add tests that serialize and deserialize `SurfaceTinAuditResponse` and assert snake_case fields:

```csharp
Assert.Contains("\"long_edge_threshold_m\"", json);
Assert.Contains("\"aspect_ratio\"", json);
Assert.Contains("\"boundary_edge_count\"", json);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug --filter SurfaceTin -v:q --nologo`

Expected: FAIL because the contracts and analyzer do not exist.

- [ ] **Step 3: Add the audit DTOs**

Define:

```csharp
public sealed record SurfaceTinAuditRequest : CivilObjectGetRequest
{
    [JsonPropertyName("long_edge_threshold_m")]
    public double? LongEdgeThreshold { get; init; }
    [JsonPropertyName("aspect_ratio_threshold")]
    public double? AspectRatioThreshold { get; init; }
    [JsonPropertyName("include_edges")]
    public bool IncludeEdges { get; init; }
    [JsonPropertyName("include_triangles")]
    public bool IncludeTriangles { get; init; }
    [JsonPropertyName("max_items")]
    public int? MaxItems { get; init; }
}
```

Add response records for summary, edges, triangles, and source definition counts. Keep edge ids stable by sorting descending by 2D length, then by coordinates.

- [ ] **Step 4: Add pure analyzer tests**

Test a two-triangle square with one shared interior edge. Assert:

```csharp
Assert.Equal(5, result.UniqueEdgeCount);
Assert.Equal(4, result.BoundaryEdgeCount);
Assert.Equal(1, result.InteriorEdgeCount);
Assert.Single(result.Triangles, t => t.AspectRatio > 1);
```

- [ ] **Step 5: Implement `SurfaceTinAuditAnalyzer.Analyze(...)`**

Create a pure helper in `CivilService.cs` or a new focused file `SurfaceTinAuditAnalyzer.cs`. It takes coordinate triples and optional thresholds, de-duplicates edges by endpoint coordinate keys, classifies edge triangle counts, computes length, grade, area, aspect ratio, percentiles, and candidate lists.

- [ ] **Step 6: Register `surface_audit_tin` as READ/Low**

Add `CivilToolNames.SurfaceAuditTin`, a schema method `SurfaceTinAuditInput()`, and a catalog descriptor using a 30-second timeout.

- [ ] **Step 7: Run task tests**

Run: `dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug -v:q --nologo`

Expected: PASS.

### Task 2: Autodesk Read Adapter and Fake Bridge for `surface_audit_tin`

**Files:**
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/AutodeskCivilAdapter.cs`
- Modify: `src/Civil3D.HybridMcp.Bridge/BridgeApplication.cs`
- Modify: `tests/Civil3D.HybridMcp.Integration.Tests/FakeBridgeHost.cs`
- Modify: `tests/Civil3D.HybridMcp.Integration.Tests/ServerFakeBridgeTests.cs`

**Interfaces:**
- Consumes: Task 1 DTOs and `SurfaceTinAuditAnalyzer`.
- Produces: `ICivilReadAdapter.ReadTinSurfaceAuditData(CivilObjectLocator locator)` or equivalent data carrier, bridge dispatch for `surface_audit_tin`, and fake bridge support.

- [ ] **Step 1: Confirm Autodesk API by metadata**

Use `System.Reflection.Metadata` against `AeccDbMgd.dll` to confirm whether `TinSurface` exposes triangle/vertex enumeration in .NET. Record the exact member names in a short note in `docs/PHASE_SURFACE_TIN_TOOLS.md`.

- [ ] **Step 2: Write fake bridge integration test**

Add a test that calls `surface_audit_tin` against fake triangle data and asserts:

```csharp
AssertMcpSuccess(result);
Assert.Equal(5, Structured(result).GetProperty("data").GetProperty("summary").GetProperty("unique_edge_count").GetInt32());
```

- [ ] **Step 3: Run integration test to verify it fails**

Run: `dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug --filter surface_audit_tin -v:q --nologo`

Expected: FAIL because the bridge dispatch is missing.

- [ ] **Step 4: Implement adapter read**

If .NET exposes triangles, use it. If not, use a contained COM fallback only for `OutputTriangles`, `Points`, `Statistics`, `Breaklines`, `Boundaries`, and `PointGroups`, and add a response warning `TIN_AUDIT_COM_FALLBACK`.

- [ ] **Step 5: Add bridge dispatch**

Add `ExecuteSurfaceAuditTin` in `BridgeApplication.cs`, following existing Civil read tool patterns.

- [ ] **Step 6: Update fake bridge manifest and dispatch**

Add fake response data and schema. Update expected tool counts in integration tests.

- [ ] **Step 7: Run task verification**

Run: `dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug -v:q --nologo`

Expected: PASS.

### Task 3: `surface_copy`

**Files:**
- Modify: `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilToolCatalog.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/AutodeskCivilAdapter.cs`
- Modify: `src/Civil3D.HybridMcp.Bridge/BridgeApplication.cs`
- Test: `tests/Civil3D.HybridMcp.Tools.Civil.Tests/CivilToolTests.cs`
- Test: `tests/Civil3D.HybridMcp.Integration.Tests/ServerFakeBridgeTests.cs`

**Interfaces:**
- Consumes: surface locator conventions and write pipeline.
- Produces: `surface_copy`, `SurfaceCopyRequest`, `SurfaceCopyResponse`, `ICivilWriteAdapter.CopySurface(...)`.

- [ ] **Step 1: Confirm copy API**

Inspect Civil 3D .NET metadata for `TinSurface.Create`, `PasteSurface`, and any clone/copy operation. If .NET cannot copy definitions, confirm COM `IAeccTinSurface.Copy()` and `PasteSurface(IAeccTinSurface)` behavior on a disposable drawing before using it.

- [ ] **Step 2: Write failing validation tests**

Add tests for:

```csharp
Assert.Throws<CivilToolException>(() => ValidateSurfaceCopy(newName: "TN RTK"));
Assert.Throws<CivilToolException>(() => ValidateSurfaceCopy(newName: ""));
Assert.Throws<CivilToolException>(() => ValidateSurfaceCopy(existingName: "TN RTK_CORRIGIDA"));
```

- [ ] **Step 3: Register `surface_copy` as WRITE/Medium**

Use confirmation if the source is not platform-created. It is not destructive, but it writes a substantial Civil 3D object.

- [ ] **Step 4: Implement copy through write pipeline**

Resolve source by name/handle, reject duplicate target names, create `TN RTK_CORRIGIDA`, assign layer/style if requested, and return source/copy `CadObjectReference` values.

- [ ] **Step 5: Add fake bridge integration**

Fake bridge should reject duplicate `new_name` and return a copied surface descriptor for success.

- [ ] **Step 6: Run task verification**

Run:

```powershell
dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug -v:q --nologo
dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug -v:q --nologo
```

Expected: PASS.

### Task 4: Rebuild and Maximum Triangle Length

**Files:**
- Modify: `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilToolCatalog.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/AutodeskCivilAdapter.cs`
- Modify: `src/Civil3D.HybridMcp.Bridge/BridgeApplication.cs`
- Test: `tests/Civil3D.HybridMcp.Tools.Civil.Tests/CivilToolTests.cs`

**Interfaces:**
- Consumes: copied surface locator and protected source guard.
- Produces: `surface_rebuild` and `surface_set_build_options`.

- [ ] **Step 1: Write protected-source tests**

Assert both tools reject a target whose name is `TN RTK` or handle is `20E98B` when those values are passed as `protect_source_name`/`protect_source_handle`.

- [ ] **Step 2: Confirm APIs**

Confirm `Surface.Rebuild()` and maximum triangle length build-option members by metadata. If only COM exposes these members, mark the implementation as COM fallback in response warnings.

- [ ] **Step 3: Implement `surface_rebuild`**

Run rebuild on the target surface and return before/after summary using `surface_audit_tin` summary fields when possible.

- [ ] **Step 4: Implement `surface_set_build_options`**

Allow only known bounded fields initially:

```csharp
bool? UseMaximumTriangleLength
double? MaximumTriangleLengthM
```

Reject nonpositive length values.

- [ ] **Step 5: Run task verification**

Run: `dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug -v:q --nologo`

Expected: PASS.

### Task 5: Boundary Preview and Add Outer Boundary

**Files:**
- Create: `src/Civil3D.HybridMcp.Tools.Civil/SurfaceBoundaryAnalyzer.cs`
- Modify: `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilToolCatalog.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/AutodeskCivilAdapter.cs`
- Test: `tests/Civil3D.HybridMcp.Tools.Civil.Tests/CivilToolTests.cs`

**Interfaces:**
- Consumes: Task 1 audit data.
- Produces: `surface_boundary_preview`, `surface_add_outer_boundary`, and explicit boundary vertex DTOs.

- [ ] **Step 1: Write preview tests**

Use point sets with a concavity and assert the preview reports a lower confidence when closing a large unsupported gap. The preview must not return `auto_apply: true`.

- [ ] **Step 2: Implement `SurfaceBoundaryAnalyzer`**

Generate candidate rings from boundary edges and optional alpha/concavity parameters. Return doubtful spans and confidence. Do not create drawing geometry.

- [ ] **Step 3: Confirm add-boundary API**

Confirm `SurfaceDefinitionBoundaries.AddBoundaries(...)` and `SurfaceBoundaryType.Outer` signatures by metadata.

- [ ] **Step 4: Implement `surface_add_outer_boundary`**

Accept explicit vertices only. Reject generated candidate ids as write input unless the caller expands them to concrete vertices. Require protected source guard.

- [ ] **Step 5: Run task verification**

Run: `dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug -v:q --nologo`

Expected: PASS.

### Task 6: Breakline Candidates and Standard Breaklines

**Files:**
- Create: `src/Civil3D.HybridMcp.Tools.Civil/SurfaceBreaklineAnalyzer.cs`
- Modify: `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilToolCatalog.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/AutodeskCivilAdapter.cs`
- Test: `tests/Civil3D.HybridMcp.Tools.Civil.Tests/CivilToolTests.cs`

**Interfaces:**
- Consumes: COGO points from `cogo_list` and target copied surface locator.
- Produces: `surface_breakline_candidates` and `surface_add_standard_breakline`.

- [ ] **Step 1: Write grouping tests**

Use repeated descriptions in two distant regions and assert the analyzer returns two sequences, not one global line.

- [ ] **Step 2: Implement candidate grouping**

Group by description, numerical sequence, distance gap, direction continuity, and local neighborhood. Return confidence and warnings.

- [ ] **Step 3: Confirm breakline API**

Confirm `SurfaceDefinitionBreaklines.AddStandardBreaklines(...)` signatures and whether it requires `ObjectIdCollection`, 3D polylines, feature lines, or point collections.

- [ ] **Step 4: Implement `surface_add_standard_breakline`**

Require explicit vertices or explicit point numbers. Preserve elevations exactly. Create temporary/supporting geometry only on a configured MCP layer, and return handles for any created geometry.

- [ ] **Step 5: Run task verification**

Run: `dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug -v:q --nologo`

Expected: PASS.

### Task 7: TIN Edit Preview, Swap Edge, and Delete TIN Line

**Files:**
- Modify: `src/Civil3D.HybridMcp.Protocol/CivilContracts.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilToolCatalog.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/CivilService.cs`
- Modify: `src/Civil3D.HybridMcp.Tools.Civil/AutodeskCivilAdapter.cs`
- Modify: `src/Civil3D.HybridMcp.Bridge/BridgeApplication.cs`
- Test: `tests/Civil3D.HybridMcp.Tools.Civil.Tests/CivilToolTests.cs`
- Test: `tests/Civil3D.HybridMcp.Integration.Tests/ServerFakeBridgeTests.cs`

**Interfaces:**
- Consumes: Task 1 audit edge ids and confirmation gate patterns.
- Produces: `surface_tin_edit_preview`, `surface_swap_edge`, and `surface_delete_tin_line`.

- [ ] **Step 1: Confirm edit APIs**

Inspect metadata for TinSurface edge swap and delete-line operations. If no programmatic API exists, implement only preview and return `NOT_SUPPORTED` for write tools.

- [ ] **Step 2: Write safety tests**

Assert write tools reject:

```csharp
new SurfaceDeleteTinLineRequest { EdgeIds = [] }
new SurfaceDeleteTinLineRequest { Filter = "length > 30" }
new SurfaceSwapEdgeRequest { TargetName = "TN RTK", ProtectSourceName = "TN RTK" }
```

- [ ] **Step 3: Register write tools as DESTRUCTIVE/Medium**

Both write tools require confirmation tokens and explicit edge ids. They must refuse filter-based mass edits.

- [ ] **Step 4: Implement preview**

Return explicit edge ids, point pairs, length, grade, boundary/interior classification, and evidence string.

- [ ] **Step 5: Implement writes if API exists**

Apply only requested edge ids to the copied surface. Return count changed and before/after audit summary. If API does not exist, return structured `NOT_SUPPORTED`.

- [ ] **Step 6: Run task verification**

Run:

```powershell
dotnet test .\tests\Civil3D.HybridMcp.Tools.Civil.Tests\Civil3D.HybridMcp.Tools.Civil.Tests.csproj -c Debug -v:q --nologo
dotnet test .\tests\Civil3D.HybridMcp.Integration.Tests\Civil3D.HybridMcp.Integration.Tests.csproj -c Debug -v:q --nologo
```

Expected: PASS.

### Task 8: Documentation, Packaging, and Live Validation

**Files:**
- Modify: `docs/TOOL_CATALOG.md`
- Modify: `docs/SECURITY_MODEL.md`
- Modify: `docs/ROADMAP_PROGRESS.md`
- Create: `docs/PHASE_SURFACE_TIN_TOOLS_LIVE_TEST.md`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: all tool contracts from Tasks 1-7.
- Produces: user-facing docs and live validation record.

- [ ] **Step 1: Update tool catalog**

Document inputs, outputs, operation class, confirmation behavior, rollback semantics, and known limitations for every new surface tool.

- [ ] **Step 2: Update security model**

Add the copy-first protected-source rule and the destructive classification for swap/delete line operations.

- [ ] **Step 3: Run full automated verification**

Run:

```powershell
dotnet test .\Civil3D.HybridMcp.sln -c Debug -v:q --nologo
dotnet build .\src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj -c Release -v:q -clp:ErrorsOnly --nologo
```

Expected: PASS.

- [ ] **Step 4: Package and install bundle**

Follow existing packaging flow. Run `scripts/check-bundle-install.ps1` against the installed bundle.

- [ ] **Step 5: Live validate on a copied DWG**

Use a test copy of the Pratao drawing. Required live sequence:

1. `surface_get` validates `TN RTK`/`20E98B`.
2. `surface_audit_tin` captures original state.
3. `surface_copy` creates `TN RTK_CORRIGIDA`.
4. `surface_audit_tin` confirms the copy.
5. Apply only high-confidence boundary/build-option corrections.
6. Rebuild and audit.
7. Confirm `TN RTK` still has the original handle, source definition counts, and audit summary.

- [ ] **Step 6: Record final report**

Create `docs/PHASE_SURFACE_TIN_TOOLS_LIVE_TEST.md` with before/after values:

- points
- triangles
- area 2D and 3D
- longest edge
- mean, median, P95, P99 edge length
- edges greater than 30 m
- triangles with aspect ratio greater than 10
- max grade
- boundary count
- breakline count
- swap edge count
- delete line count
- excluded points
- min/max elevation

- [ ] **Step 7: Final verification**

Run: `git diff --check`

Expected: no whitespace errors.
