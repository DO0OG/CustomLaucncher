# QA matrix

## Automated on every pull request

- Windows, macOS, Linux: restore, build, unit tests, formatting.
- Path mapping and legacy settings migration.
- Server status parsing/backoff and Java discovery/validation/RAM policy.
- HTTPS, SHA-256, archive path traversal, cancellation, and drop-in preservation.
- `options.txt` targeted editing and resource ordering.
- Manifest generation, merge preservation, hashing, and semantic diff.

## Manual release gates

| Flow | Windows | macOS | Linux |
|---|---|---|---|
| Fresh install and UI rendering | Required | Required | Required |
| Microsoft login, restart, silent session | Required | Required | Required |
| Java discovery/install and game launch | Required | Required (x64 + arm64) | Required |
| Discord native IPC | Required | Required | Required (native, Flatpak, Snap) |
| Full distribution update/cancel/rollback | Required | Required | Required |
| Old launcher update to signed new package | Required | Required + notarization | Required |

These manual rows cannot be certified by hosted unit-test runners. Public release
is blocked until real accounts, real distribution endpoints, target OS machines,
Windows signing material, and Apple Developer signing/notarization are supplied.

Shader application uses a configurable target strategy because Iris and OptiFine
write different files. The release profile must select the target confirmed by
the actual server modpack; the launcher does not guess from a filename.
