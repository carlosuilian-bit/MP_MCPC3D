# Phase 0.5 Decisions

Date: 2026-09-04

Status: Complete

Phase 0 was approved. Phase 0.5 closes the architectural decisions required before Phase 1 Core Scaffolding.

## Decision Summary

| Decision | Final choice | ADR | Reason | Impact on Phase 1 |
| --- | --- | --- | --- | --- |
| Base architecture | Separated MCP Server, Protocol/SDK/Core, Civil 3D Bridge | ADR-0001 | Safer, testable, and compatible with typed tool manifests | Create separate projects, not a merged reference repo |
| Transport | Windows Named Pipes, current user only | ADR-0002 | Avoids network port exposure and supports multiple instances | Implement pipe host/client in SDK |
| Tool safety | `READ`, `WRITE`, `DESTRUCTIVE` plus risk levels | ADR-0003 | Tools need machine-readable safety metadata | Add operation class and risk to tool descriptors |
| Developer Mode | Separate module, disabled by default | ADR-0004 | Dynamic C# is useful but too powerful for normal production use | Do not implement Roslyn in Phase 1; include capability flag only |
| Survey | Separate future module with adapters | ADR-0005 | Survey is essential but COM-heavy | Do not implement Survey in Phase 1; include capability flag only |
| Modal dialogs | Diagnostics core concern | ADR-0006 | Any Civil 3D automation can block on modals | Add Diagnostics interfaces and partial status |
| Server runtime | C#/.NET | ADR-0007 | Shared contracts, bridge integration, named pipes, Windows deployment, Roslyn future | Create C# server project |
| Capability model | Explicit capabilities, tool manifest version, feature flags | ADR-0008 | Product version alone is insufficient | Add handshake/version negotiation contracts and tests |

## Definitive Names

| Item | Name |
| --- | --- |
| Solution | `Civil3D.HybridMcp.sln` |
| Namespace root | `Civil3D.HybridMcp` |
| Protocol project | `Civil3D.HybridMcp.Protocol` |
| SDK project | `Civil3D.HybridMcp.Sdk` |
| Core project | `Civil3D.HybridMcp.Core` |
| Diagnostics project | `Civil3D.HybridMcp.Diagnostics` |
| Server project/executable | `Civil3D.HybridMcp.Server` |
| Bridge assembly | `Civil3D.HybridMcp.Bridge.dll` |
| Bundle | `Civil3D.HybridMcp.bundle` |
| Endpoint directory | `%LOCALAPPDATA%\Civil3DHybridMcp\endpoints` |
| Log directory | `%LOCALAPPDATA%\Civil3DHybridMcp\logs` |
| Pipe prefix | `Civil3DHybridMcp` |

These names avoid implying Autodesk ownership or direct fork status.

## Endpoint Registry Convention

Directory:

```text
%LOCALAPPDATA%\Civil3DHybridMcp\endpoints
```

File name:

```text
civil3d-hybrid-mcp-{pid}-{instance_id}.json
```

Lifecycle:

1. Bridge starts.
2. Bridge generates an `instance_id`.
3. Bridge starts pipe host.
4. Bridge writes endpoint descriptor.
5. Bridge updates `last_heartbeat_utc`.
6. Server discovers descriptors.
7. Server validates PID, process start time, heartbeat freshness, and pipe name.
8. Bridge removes descriptor on shutdown.
9. Server ignores or removes stale descriptors.

Stale detection:

- PID does not exist
- process start time differs from descriptor
- heartbeat is older than configured threshold
- descriptor cannot be parsed
- pipe cannot be reached during selection

## Pipe Naming Convention

```text
Civil3DHybridMcp-{pid}-{instance_id}
```

Rationale:

- includes product-neutral project prefix
- includes PID for diagnostics
- includes instance ID for uniqueness
- supports multiple Civil 3D sessions under one Windows user
- descriptor cleanup can be tied to process lifecycle

## Document Identity

Official structure:

```json
{
  "instance_id": "...",
  "document_id": "...",
  "name": "Projeto.dwg",
  "full_path": "C:\\Projetos\\Projeto.dwg",
  "is_active": true,
  "is_saved": true,
  "is_read_only": false,
  "units": "Meters",
  "database_fingerprint": "...",
  "session_identifier": "..."
}
```

Lifetime rules:

- `document_id`: stable for the document within the current bridge instance/session.
- `session_identifier`: only stable while the document object remains open in that Civil 3D process.
- `database_fingerprint`: preferred when Civil 3D exposes it; may persist with the DWG but must not be assumed present.
- `full_path`: useful for saved drawings, unavailable or unstable for unsaved drawings.
- `ObjectId`: valid only inside the Autodesk database/session context, not a cross-session document identity.
- `Handle`: useful for object identity after save/reopen in many cases, but not sufficient alone and not a document identity.

Sensitive operations must accept or resolve `instance_id` and `document_id`. If ambiguous, fail with `INSTANCE_AMBIGUOUS` or `DOCUMENT_AMBIGUOUS`.

## Confirmation Model

Flow:

```text
Preview
  -> ConfirmationRequired
  -> user confirmation
  -> confirmation token
  -> bridge validation
  -> execution
```

A confirmation token is valid only when all bound fields match:

- tool name
- instance ID
- document ID
- correlation ID
- normalized arguments hash
- affected count
- expiration
- operation class

The bridge must reject plain `confirmed=true`. The bridge validates token integrity and expiry before any DESTRUCTIVE operation and before any WRITE operation that declares confirmation required.

Phase 1 implements contracts and isolated token/hash tests only. No UI is implemented in Phase 1.

## ADR Gate

All required ADRs are accepted:

- ADR-0001 Accepted
- ADR-0002 Accepted
- ADR-0003 Accepted
- ADR-0004 Accepted
- ADR-0005 Accepted
- ADR-0006 Accepted
- ADR-0007 Accepted
- ADR-0008 Accepted

Gate result: Phase 1 may proceed.

