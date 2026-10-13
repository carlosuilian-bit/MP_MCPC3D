# ADR-0007: MCP Server Runtime

Date: 2026-09-04

Status: Accepted

## Context

The hybrid platform needs an MCP Server runtime that works well with:

- Civil 3D bridge code written in C#
- shared protocol contracts and DTOs
- generated JSON Schema
- Windows named pipes
- multiple Civil 3D instances
- audit logging
- package/bundle deployment on Windows
- future Developer Mode using Roslyn

The two serious options are C#/.NET and TypeScript/Node.

The TypeScript SDK is mature and widely used. The references also show a successful TypeScript server in `hjlrosales/Civil3D_MCP`. However, this project will put the bridge, Autodesk adapters, protocol contracts, typed tools, schemas, transaction model, confirmation contracts, and future Roslyn Developer Mode in the .NET ecosystem.

The official MCP C# SDK is available and stable enough for this architecture. Therefore C#/.NET can provide MCP compatibility without forcing a second language/runtime for the server.

## Decision

Implement the Hybrid MCP Server in C#/.NET.

Target:

- .NET 8 as the initial target framework for server libraries and tests
- C# namespaces rooted at `Civil3D.HybridMcp`
- official MCP C# SDK integration as the server-facing MCP adapter path
- shared `Civil3D.HybridMcp.Protocol` contracts between server, bridge, SDK, tests, and fake bridge

The server will not hardcode the Civil 3D tool catalog. It will discover bridge endpoints, handshake over named pipes, receive tool manifests, and register/execute tools through the approved protocol.

## Alternatives

### TypeScript/Node

Pros:

- highly mature MCP SDK ecosystem
- strong prior art in `hjlrosales/Civil3D_MCP`
- fast iteration for MCP adapters
- good JSON tooling

Cons for this project:

- duplicates protocol DTOs across TypeScript and C#
- requires schema/type synchronization across runtimes
- adds a Node runtime to a Windows/Civil 3D deployment
- named pipe and Autodesk-side debugging cross language boundaries
- future Developer Mode/Roslyn remains .NET-only
- harder to reuse assemblies/contracts directly

### C#/.NET

Pros:

- one language for server, protocol, bridge, SDK, diagnostics, tests, and future Developer Mode
- direct sharing of DTOs and version contracts
- strong Windows named pipe support in the BCL
- direct compatibility with Roslyn and Autodesk integration tests
- simpler Windows deployment next to Civil 3D
- consistent logging, serialization, and test infrastructure

Cons:

- TypeScript MCP SDK has more historical examples
- dynamic MCP tool registration in C# may require more adapter work
- fewer prior Civil 3D MCP references use C# for the outer MCP server

## Consequences

Positive:

- fewer moving parts in deployment
- shared protocol contracts are source-of-truth
- easier fake bridge and integration tests
- less type drift between bridge and server
- better fit for Windows/Civil 3D operations
- future Roslyn Developer Mode can reuse the same contract assemblies

Negative:

- the MCP adapter must be designed carefully around the C# SDK
- TypeScript examples cannot be copied directly
- cross-client MCP compatibility must be covered by integration tests

## Implementation Impact

Phase 1 creates:

- `src/Civil3D.HybridMcp.Server`
- `src/Civil3D.HybridMcp.Protocol`
- `src/Civil3D.HybridMcp.Sdk`
- `src/Civil3D.HybridMcp.Core`
- `src/Civil3D.HybridMcp.Bridge`
- `src/Civil3D.HybridMcp.Diagnostics`

Future MCP-specific host wiring should live in the Server project and consume shared services rather than leaking into Protocol, Bridge, or Core.

