# Phase 3C Live Test - AEC Property Set Numeric Write/Readback

Date: 2026-09-14

Scope: validate the installed Phase 3C bundle against Civil 3D 2026 with `propertyset_numeric_test_create`.

## Tool Under Test

`propertyset_numeric_test_create`

- Module: Civil3D
- Operation class: WRITE
- Risk: Low
- Confirmation: not required
- Rollback semantics: transaction rollback before commit

The tool creates a prefixed test Property Set definition, creates a temporary `DBPoint`, attaches the set to that point, writes one AEC `Real` value, and returns the attached set using the same property value shape used by `propertyset_get`.

This is not a general `propertyset_set` implementation. It is a controlled test/write probe for the AEC numeric path.

## Build, Install, And Offline Diagnostic

Commands executed:

```powershell
dotnet build .\src\Civil3D.HybridMcp.Bridge\Civil3D.HybridMcp.Bridge.csproj -c Release --no-restore -v:q --nologo
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-bundle.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-bundle-install.ps1 -AsJson
```

Result:

- Bridge Release build passed with 0 errors.
- Build/install emitted the 9 known Autodesk MSB3277 assembly-resolution warnings.
- Installed-bundle diagnostic returned `success: true` with no issues or warnings.

## Test Drawing

Original test DWG:

```text
C:\Users\CGomes\Desktop\TBD-25-009 - GEOMETRICO R2-svapmh5d.dwg
```

Temporary copy opened in Civil 3D:

```text
artifacts\live-tests\TBD-25-009_GEOMETRICO_R2_propertyset_numeric_20260914-111406.dwg
```

The original DWG was not opened or modified.

## Live Host

Civil 3D 2026 was launched with the quoted `/ld` argument form documented in `docs/TROUBLESHOOTING.md`.

Observed bridge endpoint:

- process id: `42372`
- instance id: `aebe84c2232e41a0a4e1c65af733d28a`
- advertised tool count: `37`
- active document: the temporary DWG copy above

## Modal Dialog Incident

The first live client call timed out during `bridge/handshake`.

Root cause:

- Civil 3D was blocked by an AutoCAD modal dialog.
- The top modal title was `References - Not Found Files`.
- The main Civil 3D window was disabled while the modal was enabled.
- Visible buttons included `Open the External References palette` and `Ignore unresolved reference files`.

Action taken:

- The dialog was read before dismissal.
- Because the target was a temporary DWG copy, the safe test-flow choice was `Ignore unresolved reference files`.
- After dismissal, the main Civil 3D window became enabled and the live client retried successfully.

Operational lesson:

- When a host-dispatched call times out but server-only calls still work, check/read modal dialogs before retrying.
- `References - Not Found Files` can block handshake and drawing tools.

## Live Tool Call

Request values:

- `definition_name`: `MCP_NUMERIC_TEST_LIVE_111759`
- `property_name`: `NumericValue`
- `numeric_value`: `987.654321`
- `target_kind`: `db_point`

Create response:

- `success`: `true`
- `tool`: `propertyset_numeric_test_create`
- `definition_name`: `MCP_NUMERIC_TEST_LIVE_111759`
- `definition_handle`: `16FD0C`
- `object_handle`: `16FD0D`
- `target_kind`: `db_point`
- property set handle: `16FD10`
- property name: `NumericValue`
- property data type: `Real`
- `value`: `987.654321`
- `numeric_value`: `987.654321`
- `transaction.document_locked`: `true`
- `transaction.transaction_started`: `true`
- `transaction.committed`: `true`
- `transaction.rolled_back`: `false`

Readback:

- `propertyset_get` was called with the returned object handle and definition name.
- The same property set was found on object handle `16FD0D`.
- `NumericValue` read back as `value: 987.654321` and `numeric_value: 987.654321`.

## Cleanup

The Civil 3D test instance was closed without saving. After `Quit()` did not fully remove the process, the test PID was ended under the user's temporary permission for 2026-09-14.

Final process check found no remaining `acad.exe`.

## Result

Phase 3C passed.

Evidence established:

- AEC Property Set definitions can be created from the bridge.
- A Property Set can be attached to a newly created DB object.
- A numeric `Real` value can be written with transaction protection.
- The value can be read back through the existing property-set read path as `numeric_value`.
- Modal dialog handling must be part of live-test procedure, not an afterthought.

Remaining work:

- General `propertyset_set` remains unimplemented.
- Existing production objects are not modified by this tool.
- Rollback failure-path live tests still need a dedicated phase.
