# ADR-0001: Base Architecture

Date: 2026-09-04

Status: Accepted

## Context

The project needs a Civil 3D MCP platform that is safe for production drawings, supports multiple Civil 3D instances, exposes typed tools, and can grow into Survey and Developer Mode workflows.

The audit found three different reference shapes:

- `hjlrosales/Civil3D_MCP`: modular TypeScript MCP server plus .NET bridge, dynamic manifests, named pipes, transaction pipeline.
- `barbosaihan/civil3d-mcp`: dynamic C# execution first, with a small MCP meta-tool surface.
- `KevinGriffin-new/civil3d-mcp`: broad practical Survey and modal recovery bridge, but monolithic and fixed-port.

## Decision

Use a separated server/bridge architecture as the base:

```text
MCP Client -> Hybrid MCP Server -> Secure Local IPC -> Civil 3D Bridge -> Civil 3D
```

The bridge is the source of truth for tool availability and publishes typed tool manifests. The server dynamically registers MCP tools from those manifests.

## Alternatives

1. Directly merge all three references.
   - Rejected. This violates project policy and would mix incompatible architecture, safety, and dispatcher styles.

2. Use dynamic C# execution as the main tool surface.
   - Rejected for production. It is too broad and hard to validate.

3. Use a single monolithic bridge with static server wrappers.
   - Rejected. It does not scale cleanly to permissions, tests, Survey, and versioned tools.

## Consequences

Positive:

- clear ownership boundaries
- dynamic tool catalog
- easier testing with fake bridges
- safer production operation
- room for Survey and Developer Mode modules

Negative:

- more initial scaffolding than a single-file bridge
- schema generation and version negotiation must be built early
- confirmation and permission must be designed as protocol features, not afterthoughts
