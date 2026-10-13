# ADR-0004: Developer Mode

Date: 2026-09-04

Status: Accepted

## Context

The project requires the ability to run new Civil 3D operations before typed production tools exist. `barbosaihan/civil3d-mcp` demonstrates dynamic C# execution with Roslyn and useful Civil 3D globals.

However, arbitrary C# execution is too powerful for normal production use, especially with broad assembly references and regex-only safety checks.

## Decision

Implement dynamic C# as a separate Developer Mode module, disabled by default.

When enabled, Developer Mode may expose tools such as:

- `civil3d_query_csharp`
- `civil3d_execute_csharp`
- `developer_mode_skills`

Developer Mode must include:

- explicit configuration gate
- visible status indicator
- assembly allowlist
- namespace/API blocklist
- Roslyn syntax and semantic analysis
- Civil 3D destructive API detection
- transaction wrapping when possible
- code hash logging
- confirmation for writes and destructive operations

## Alternatives

1. Make dynamic C# the primary platform interface.
   - Rejected. It undermines typed tools and predictable safety.

2. Omit Developer Mode entirely.
   - Rejected. The product vision requires prototyping new Civil 3D operations.

3. Use regex-only sandboxing.
   - Rejected. It is easy to bypass and cannot classify Civil 3D semantics.

## Consequences

Positive:

- preserves extensibility
- supports exploration of Civil 3D API gaps
- creates a path from prototype to permanent tool

Negative:

- sandbox engineering is substantial
- some APIs cannot be proven safe statically
- Developer Mode tests must be broad and adversarial
