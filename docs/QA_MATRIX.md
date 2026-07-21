# QA matrix

## Automated evidence

- Windows, macOS, Linux CI: restore, compile, unit tests, and formatting.
- Unit tests: path mapping/migration, manifest validation and staged updates, archive traversal,
  mod-loader selection, Java version/factory policy, settings transactions, cancellation lifetime,
  server status parsing/backoff, option editing, pack ordering, and manifest tooling.
- Content tab behaviour: every `OptionsEditStatus` branch reaches the user, refusing to reorder or
  disable a required server pack reports instead of throwing, optional-module toggles cascade to
  submodules and persist, and a manifest fetch failure becomes a message rather than an unhandled
  command fault.
- Java tab: RAM bounds derive from `RamCalculator` against the machine's memory instead of a fixed
  range, and hand-written `-Xmx`/`-Xms` arguments raise a conflict warning.

These are isolated tests with fakes and temporary files; they are not certified end-to-end launches.

## Not covered by automation

Drag-and-drop reordering of resource packs is driven by pointer and `DragDrop` events and is only
exercised by hand. The ▲▼ buttons call the same `MoveToAsync` entry point, which is unit tested, so
the ordering rules are covered even though the gesture is not.

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
