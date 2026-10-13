# ADR-0008: Capability and Version Model

Date: 2026-09-04

Status: Accepted

## Context

The platform must not assume that every open Civil 3D instance supports every possible tool. Capabilities can vary by:

- Civil 3D version
- AutoCAD version
- bridge version
- protocol version
- loaded Autodesk modules
- available COM components
- enabled feature flags
- Developer Mode configuration
- Survey availability
- Dialog Monitor availability
- per-tool contract versions

The MCP Server and Bridge also need a safe response when their protocol versions differ.

## Decision

Use an explicit capability and version model published during handshake and repeated in endpoint descriptors.

The Bridge publishes:

- instance identity
- product and product version
- bridge version
- protocol version
- API versions when detectable
- module capability flags
- feature flags
- tool manifest version
- available tool descriptors

The Server evaluates compatibility before exposing or executing tools.

## Capability Model

Core contract:

```json
{
  "instance_id": "...",
  "product": "Autodesk Civil 3D",
  "product_version": "2026",
  "bridge_version": "0.1.0",
  "protocol_version": "1.0.0",
  "tool_manifest_version": "1.0.0",
  "api_versions": {
    "autocad": "R25.0",
    "civil3d": "2026"
  },
  "capabilities": {
    "autocad": true,
    "civil3d": true,
    "core": true,
    "diagnostics": true,
    "dialog_monitor": "partial",
    "survey": false,
    "survey_com": false,
    "developer_mode": false
  },
  "feature_flags": {
    "confirmation_tokens": true,
    "named_pipe_current_user_only": true
  },
  "tools": []
}
```

Implementation does not need this exact JSON layout, but it must preserve these meanings.

## Version Negotiation

Use semantic versions for:

- protocol version
- bridge version
- server version
- tool manifest version
- each tool contract version

Negotiation classes:

- `Compatible`: same major version and both sides support the required minor contract.
- `PartiallyCompatible`: same major version, but one side has a lower minor version or some tool contracts are newer than the server supports. The server may expose only compatible tools.
- `Incompatible`: major version differs or required protocol features are missing. The server must not execute tools.

Rules:

1. Major protocol mismatch is incompatible.
2. Same major protocol with bridge minor lower than the server-required minor is partially compatible.
3. Same major protocol with bridge minor equal or higher is compatible.
4. Tool contract major mismatch disables that tool.
5. Tool contract same major but newer minor may be exposed only if required features are present; otherwise it is hidden or reported as partially compatible.
6. The server must never silently execute an incompatible tool contract.

## Alternatives

1. Infer capabilities from product version only.
   - Rejected. Version alone does not prove modules or feature flags are available.

2. Expose all tools and let execution fail.
   - Rejected. This is unsafe and creates poor agent behavior.

3. Hardcode tool availability in the MCP Server.
   - Rejected. The bridge is the only process that knows the loaded Autodesk environment.

## Consequences

Positive:

- avoids fake support for missing APIs
- enables multiple Civil 3D versions
- supports optional Survey and Developer Mode
- lets the server hide incompatible tools
- creates a stable foundation for future modules

Negative:

- every tool needs contract version metadata
- bridge startup must inspect capabilities carefully
- tests must cover compatible, partial, and incompatible negotiation

## Phase 1 Impact

Phase 1 must implement:

- `ProtocolVersion`
- `CapabilityManifest`
- `EndpointDescriptor`
- `ToolDescriptor`
- `VersionNegotiator`
- tests for compatible, partially compatible, and incompatible negotiation

