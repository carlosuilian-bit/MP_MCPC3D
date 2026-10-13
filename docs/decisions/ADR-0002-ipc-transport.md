# ADR-0002: IPC Transport

Date: 2026-09-04

Status: Accepted

## Context

The platform needs local communication between the MCP server and the Civil 3D bridge. The references use different transports:

- `hjlrosales/Civil3D_MCP`: JSON-RPC over Windows named pipes with current-user restriction.
- `barbosaihan/civil3d-mcp`: TCP loopback on fixed port 8080.
- `KevinGriffin-new/civil3d-mcp`: HTTP localhost on fixed port 8270.

Production drawings require avoiding unnecessary network exposure and avoiding ambiguity across multiple Civil 3D processes.

## Decision

Use Windows Named Pipes restricted to the current user as the initial production IPC.

Each Civil 3D bridge instance will advertise a unique pipe name through a per-user endpoint descriptor.

## Alternatives

1. Fixed TCP loopback port.
   - Rejected. Port conflicts and weak per-user isolation make multiple Civil 3D instances fragile.

2. Fixed HTTP localhost port.
   - Rejected for the same reasons. It is convenient for experiments but weaker as the production default.

3. COM-only automation from outside Civil 3D.
   - Rejected. It does not provide the same typed MCP tool and transaction boundary.

## Consequences

Positive:

- no public network port
- current-user isolation
- per-process endpoint naming
- better fit for multiple instances

Negative:

- Windows-specific transport
- requires server-side endpoint registry and stale descriptor cleanup
- future non-Windows support would need another transport
