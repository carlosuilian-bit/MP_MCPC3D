# License Compatibility

Date: 2026-09-04

Scope: local license and manifest audit for the three reference repositories. This document is not legal advice. It records evidence and engineering risk for Phase 0.

## Summary

All three repositories contain MIT license files. MIT is generally permissive and allows reuse, modification, distribution, and private/commercial use if the copyright and license notices are preserved.

However, this project should still prefer conceptual reimplementation in Phase 1 because:

- `AGENTS.md` explicitly requires not copying code before license analysis.
- The target product is a new safe production platform, not a direct merge.
- Autodesk managed assemblies and SDK files are proprietary and must not be redistributed.
- `barbosaihan/civil3d-mcp` has a license metadata conflict: local `LICENSE` says MIT, but `package.json` says ISC.
- The safest integration path is to reuse ideas first, and copy/adapt code later only with deliberate attribution and review.

## Repository License Table

| Repository | Local license evidence | Manifest metadata | Initial compatibility | Direct code reuse status | Notes |
| --- | --- | --- | --- | --- | --- |
| `hjlrosales/Civil3D_MCP` | MIT License, copyright `(c) 2026 hjlrosales` | Root `package.json` and server package identify MIT/private package metadata | Compatible in principle with attribution | Allowed only after preserving MIT notice and reviewing copied files | Best used as architectural reference for core patterns. |
| `barbosaihan/civil3d-mcp` | MIT License, copyright `(c) 2025 lisiting01` | `package.json` says `ISC`; README says MIT | Needs clarification before direct reuse | Treat as reference only until metadata conflict is resolved | Dynamic/Roslyn ideas are useful; sandbox is not production-ready. |
| `KevinGriffin-new/civil3d-mcp` | MIT License, copyright `(c) 2026 Kevin Griffin` | Python/C# project files do not introduce conflicting license metadata in inspected files | Compatible in principle with attribution | Allowed only after preserving MIT notice and reviewing copied files | Best used as Survey/modal reference; direct code reuse has high integration risk. |

## MIT Obligations

If code is copied or adapted from a MIT-licensed reference, the hybrid project must:

- retain the original copyright notice
- include the MIT permission notice
- include the MIT warranty disclaimer
- document which files or substantial snippets were copied or adapted
- avoid implying Autodesk endorsement or ownership
- avoid redistributing Autodesk proprietary DLLs, SDK assemblies, or copied binary references

## Autodesk-Specific Constraint

All repositories depend on Autodesk/Civil 3D managed assemblies or COM interop references. These references are not made open-source merely because the sample code is MIT.

Project policy:

- do not commit Autodesk DLLs
- do not redistribute Autodesk assemblies in packages
- require local path detection/configuration for SDK/installation assemblies
- document that users must have an appropriately licensed Autodesk Civil 3D installation

## Per-Repository Notes

### hjlrosales/Civil3D_MCP

License finding:

- local `LICENSE`: MIT
- package metadata: MIT/private monorepo style metadata

Obligations if reused:

- preserve MIT notice and copyright
- preserve attribution in `NOTICE` or equivalent
- clearly mark any copied/adapted files in changelog or source headers

Potential direct reuse candidates after review:

- protocol contracts and envelope shapes
- named-pipe host abstractions
- endpoint descriptor concepts
- command/transaction abstractions
- tests around fake bridge/server behavior

Recommendation:

- reimplement core concepts rather than copy code in the first implementation slice
- use exact code only if doing so reduces risk and the copied boundary is narrow
- fix confirmation propagation in the hybrid design rather than inheriting the current inspected gap

### barbosaihan/civil3d-mcp

License finding:

- local `LICENSE`: MIT, copyright `(c) 2025 lisiting01`
- `README.md`: references MIT
- `package.json`: `license` field is `ISC`

Compatibility issue:

- The source tree contains conflicting license metadata.
- Treat the local LICENSE as strong evidence for the repository text, but do not copy code until this conflict is resolved or accepted by project owner/legal review.

Potential direct reuse candidates after review:

- small Roslyn host structure
- `ScriptContext` idea exposing `Document`, `CivilDoc`, `Database`, `Editor`, and `Transaction`
- skill-template format, if adapted with attribution

Recommendation:

- use as reference only for Phase 1 and Phase 5 design
- rebuild sandbox and risk analyzer from scratch
- do not reuse regex-only safety checks as the final protection layer

### KevinGriffin-new/civil3d-mcp

License finding:

- local `LICENSE`: MIT, copyright `(c) 2026 Kevin Griffin`

Obligations if reused:

- preserve MIT notice and copyright
- preserve attribution for any copied Survey/FBK/dialog code
- document modifications

Potential direct reuse candidates after review:

- isolated pieces of FBK parsing logic
- modal dialog probing logic
- Survey COM resolution strategies
- regression input examples and test ideas

Recommendation:

- avoid copying the monolithic bridge
- reimplement Survey under typed modules and interfaces
- isolate COM-specific behavior behind adapters
- avoid exposing `send_command`, `com_invoke`, `load_assembly`, or `setvar` as normal production tools

## Code Reuse Classification

| Area | Can reuse code later? | Should serve as reference only now? | Notes |
| --- | --- | --- | --- |
| hjlrosales named-pipe transport | Yes, with attribution | Yes for Phase 1 | Preferred path is reimplementation matching our contracts. |
| hjlrosales endpoint registry | Yes, with attribution | Yes for Phase 1 | Keep concept; use `Civil3DHybridMcp` namespace/path. |
| hjlrosales command pipeline | Yes, with attribution | Yes for Phase 1 | Strong model; confirmation needs correction. |
| hjlrosales typed tool attributes | Yes, with attribution | Yes for Phase 1 | Recreate with our module names and risk classes. |
| barbosaihan Roslyn executor | Maybe after license clarification | Yes | Rebuild with allowlists and analyzers. |
| barbosaihan skill templates | Maybe after license clarification | Yes | Useful as examples for Developer Mode prompts. |
| KevinGriffin DialogProbe | Yes, with attribution | Yes | High-value but should be integrated into Diagnostics core. |
| KevinGriffin FBK parser | Yes, with attribution | Yes | Good candidate for isolated parser module plus unit tests. |
| KevinGriffin Survey COM resolver | Yes, with attribution | Yes | Rebuild behind `ISurveyGateway`. |
| KevinGriffin command/COM escape hatches | Avoid | Yes | Too broad for production safety. |

## Architectural Ideas That Can Be Reimplemented

Architectural ideas are not protected in the same way as source text, but implementation must still avoid copying expression unnecessarily.

Safe to reimplement conceptually:

- MCP server separated from Civil 3D bridge
- local IPC between server and bridge
- tool manifest discovery
- structured schemas and result envelopes
- endpoint registry for multiple instances
- document lock and transaction wrapping
- risk classes READ/WRITE/DESTRUCTIVE
- disabled-by-default Developer Mode
- Roslyn allowlist/sandbox model
- Survey module with COM adapter
- modal dialog list/dismiss/cancel tools
- dry-run FBK parser before import

## Required Attribution Plan

If later code is copied or substantially adapted:

1. Add a `NOTICE.md` or license section to `README.md`.
2. Preserve original MIT license text for the relevant source.
3. Mark files or modules that contain adapted code.
4. Record the source repository and commit hash, if available.
5. Confirm no Autodesk binary or proprietary SDK content was copied.

## Phase 0 License Decision

For Phase 1, use the references as design inputs only. Do not copy source code yet.

Direct code reuse remains possible in principle for MIT repositories after attribution, but `barbosaihan` requires license metadata clarification before any direct copying.

