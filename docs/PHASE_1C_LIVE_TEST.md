# Phase 1C Live Test - Modal Dialog Monitor

Date: 2026-09-14

Scope: validate the installed Phase 1C Autodesk bundle against Civil 3D 2026 with the new `list_dialogs` tool and the real dialog state now reported by `civil3d_status`.

## Result

Status: Passed.

`list_dialogs` answered in 28 milliseconds while the Civil 3D host thread was blocked by a modal dialog, correctly named the blocking dialog, and correctly ignored a disabled palette that is not a modal. The same tool reported a clean state after the dialog was dismissed.

This closes the diagnostic gap that made Phase 2E hang with no explanation.

## Why This Phase Exists

Phase 2E and Phase 3B both stalled on the same modal. From a client's point of view the failure was indistinguishable from a slow tool: `tools/list` answered instantly because the bridge serves the manifest itself, while every tool that dispatches through the Autodesk idle loop waited forever.

The fix is structural: `list_dialogs` never touches the Autodesk host context. It answers from the pipe thread using Win32 only, so it still works precisely when the host is stuck.

## Detection Rule

Windows disables a dialog's owner window while the dialog is modal. The monitor uses that directly:

- main window: the visible, titled, unowned window with the largest area
- dialog candidates: visible, titled windows that have an owner
- blocked: the main window is disabled and at least one candidate is enabled

## Installed Bundle

Installed to `%APPDATA%\Autodesk\ApplicationPlugins\Civil3D.HybridMcp.bundle`. Offline diagnostic: success, no issues, no warnings. The live catalog advertised 33 tools.

## Test Drawing

A copy was used, never the original:

```text
source : C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg
copy   : artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_dialog_monitor_20260914-085139.dwg
```

The source kept its pre-test timestamp.

## Blocked Case

Ground truth from an independent out-of-process window sweep:

```text
References - Not Found Files                        enabled
Properties                                          disabled   (palette)
Autodesk Civil 3D 2026 - [ ... .dwg]                DISABLED   (main window)
```

`list_dialogs` returned, in 28 ms:

```json
{
  "blocked_by_modal": true,
  "blocking_dialog_title": "References - Not Found Files",
  "main_window_title": "Autodesk Civil 3D 2026 - [TBD-25-009_GEOMETRICO_R2_dialog_monitor_20260914-085139.dwg]",
  "main_window_enabled": false,
  "count": 2,
  "dialogs": [
    {
      "title": "References - Not Found Files",
      "class_name": "#32770",
      "is_enabled": true,
      "is_modal_candidate": true
    },
    {
      "title": "Properties",
      "class_name": "Afx:00007FF7D11A0000:8:0000000000010003:0000000000000000:0000000000000000",
      "is_enabled": false,
      "is_modal_candidate": false
    }
  ]
}
```

Two things are proven here. The tool answered while the host was blocked, and it discriminated correctly: the standard dialog class `#32770` that was enabled is flagged as the modal candidate, while the disabled docked palette named `Properties` is reported but not flagged.

The response time matters as much as the verdict. 28 ms against an indefinite hang is the difference between a diagnosable system and a mysterious one.

## Cleared Case

After the dialog was dismissed with `WM_CLOSE`, the main window became enabled again and the tools agreed:

```json
{
  "list_dialogs": {
    "blocked_by_modal": false,
    "blocking_dialog_title": null,
    "main_window_enabled": true,
    "count": 0
  },
  "document_list": { "answered": true, "count": 1 },
  "civil3d_status.dialogState": {
    "supported": true,
    "state": "active",
    "hasBlockingDialog": false,
    "dialogCount": 0
  },
  "capabilities.dialogMonitor": "active"
}
```

`document_list` answered normally once unblocked, confirming the host thread had recovered.

The capability flag moved from `partial` to `active`, and `civil3d_status` now reports `supported: true` instead of the Phase 1 placeholder.

## Not Yet Implemented

- `dismiss_dialog` is deliberately absent. Dismissing a dialog is a write-class action, and AGENTS.md section 14 forbids closing a dialog without identifying it first. The design agreed for a later phase requires the caller to pass the expected title and restricts dismissal to a known allowlist.
- `cancel_active_command` is not implemented.
- The monitor reports window state only. It does not read dialog body text or button captions, so it cannot tell a harmless notification from a destructive confirmation. That judgement stays with the operator.

## Cleanup

The drawing copy was closed without saving. Civil 3D accepted `Quit()` but stayed alive, as in every prior phase, and the test process was force-stopped under permission the user granted for 2026-09-14, limited to the instance started by this test. No `acad.exe` remained running.

## Safety Boundary

`list_dialogs` is READ/Low:

- enumerates windows of its own process only
- reads titles with `SendMessageTimeout` and `SMTO_ABORTIFHUNG`, never `GetWindowText`, so a hung UI thread cannot hang the bridge
- never posts input, never closes or activates a window
- does not touch the drawing, the document set, or the Autodesk host context
