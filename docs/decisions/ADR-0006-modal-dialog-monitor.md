# ADR-0006: Modal Dialog Monitor

Date: 2026-09-04

Status: Accepted

## Context

Civil 3D can block automation with modal dialogs, active commands, warning prompts, and file dialogs. A blocked main command thread can make an MCP operation appear hung.

`KevinGriffin-new/civil3d-mcp` includes a practical Win32 `DialogProbe` that enumerates modal dialogs in the Civil 3D process, identifies buttons, and allows selected dismiss/cancel actions through bypass tools.

## Decision

Implement modal dialog monitoring in the core `Diagnostics` module.

Required tools:

- `civil3d_status`
- `list_dialogs`
- `dismiss_dialog`
- `cancel_active_command`

Normal tools should pre-check dialog state and fail fast with structured `BLOCKED_BY_DIALOG` when a modal is present. Status/dialog/cancel tools may bypass main-thread dispatch when needed.

## Alternatives

1. Rely only on operation timeouts.
   - Rejected. Timeouts are too slow and do not tell the agent what to do next.

2. Auto-dismiss dialogs silently.
   - Rejected. Some dialogs are destructive or require user judgement.

3. Leave modal handling inside Survey only.
   - Rejected. Any Civil 3D automation can be blocked by dialogs.

## Consequences

Positive:

- faster recovery from blocked Civil 3D sessions
- better user-visible status
- safer handling of warning/confirmation dialogs

Negative:

- Win32 dialog probing is Windows-specific
- button matching must be conservative
- localization may affect button captions and messages
