# QA matrix

## Automated evidence

- Windows, macOS, Linux CI: restore, compile, unit tests, and formatting.
- Unit tests: path mapping/migration, manifest validation and staged updates, archive traversal,
  mod-loader selection, Java version/factory policy, settings transactions, cancellation lifetime,
  server status parsing/backoff, option editing, pack ordering, and manifest tooling.

These are isolated tests with fakes and temporary files; they are not certified end-to-end launches.

## Manual release gates

| End-to-end flow | Windows | macOS | Linux |
|---|---|---|---|
| Fresh install and UI rendering | Required | Required | Required |
| Device-code login, restart, silent restore | Required | Required | Required |
| Java discovery/install and modded game launch | Required | Required (x64 + arm64) | Required |
| Discord IPC discovery | Required | Required | Required (native, Flatpak, Snap) |
| Distribution update/cancel/offline fallback | Required | Required | Required |
| Signed package update | Required | Required + notarization | Required |

Public release remains blocked until operator endpoints/client IDs, real accounts, target machines,
and signing credentials are supplied. Shader settings must be confirmed against the production
modpack because Iris and OptiFine use different option targets.
