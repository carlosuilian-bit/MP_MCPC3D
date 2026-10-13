# ADR-0003: Tool Safety Model

Date: 2026-09-04

Status: Accepted

## Context

The hybrid platform must operate on real DWG files without silent destructive behavior. The references show partial safety mechanisms:

- `hjlrosales/Civil3D_MCP` has permissions, risks, validation, transactions, and rollback, but confirmation propagation needs rework.
- `barbosaihan/civil3d-mcp` separates query and execute, but arbitrary code is too broad.
- `KevinGriffin-new/civil3d-mcp` has many practical write actions but no central permission/confirmation model.

## Decision

Every production tool must declare:

- operation class: `READ`, `WRITE`, or `DESTRUCTIVE`
- risk level: `Low`, `Medium`, `High`, or `Critical`
- target module
- input schema
- output schema
- timeout
- confirmation requirement
- rollback semantics

All WRITE and DESTRUCTIVE drawing operations must execute through a central command pipeline with validation, document lock, transaction, commit, and rollback.

DESTRUCTIVE tools require explicit confirmation with impact preview.

## Alternatives

1. Trust tool naming and documentation only.
   - Rejected. Agents need machine-readable safety metadata.

2. Let the MCP server alone enforce safety.
   - Rejected. The bridge must re-check safety before touching Civil 3D.

3. Treat all write operations the same.
   - Rejected. Delete/purge/mass changes require stronger controls than ordinary create/update tools.

## Consequences

Positive:

- predictable tool behavior
- safer production DWG operations
- consistent audit logging
- easier review of new tools

Negative:

- every tool needs metadata and validators
- confirmation token flow must be implemented carefully
- Survey COM and queued-command tools need explicit non-rollback documentation
