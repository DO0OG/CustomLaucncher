# QA matrix

Korean version: [QA_MATRIX.ko.md](QA_MATRIX.ko.md)

## Automated evidence

CI runs restore, build, unit tests, and `dotnet format --verify-no-changes` on Windows, macOS, and
Linux for every pull request. The suite is 138 tests covering:

- Path mapping/migration and per-launcher directory scoping.
- Settings transactions (validate-before-mutate, atomic write, schema version).
- Manifest validation and staged updates: HTTPS, SHA-256 re-check, archive path traversal, archive
  limits, cancellation, and drop-in preservation.
- Mod-loader selection, Java discovery/validation/factory policy, and RAM policy from `RamCalculator`.
- Server status parsing and backoff; the multiplayer-list (`servers.dat`) writer, including modified
  UTF-8 round-tripping and merge preservation.
- `options.txt` targeted editing and resource-pack ordering.
- Content tab behaviour: every `OptionsEditStatus` branch reaches the user, a required server pack
  refuses to move/disable with a message instead of throwing, optional-module toggles cascade to
  submodules and persist, and a manifest fetch failure becomes a message not an unhandled fault.
- Auth error mapping: the 403 that Minecraft returns is recognised whether the library fills
  `StatusCode` or only the message.
- Manifest tool: hashing, merge preservation, semantic diff, URL templating, and the tool window's
  validation/defaults/change-report.

These are isolated tests with fakes and temporary files; they are not end-to-end launches.

## Verified by hand

- Microsoft login end to end (browser flow, real account) once the client id was approved for the
  Minecraft API. The 403-before-approval path and its on-screen message were also observed.
- The launcher and settings windows render and operate on Windows.

## Not covered by automation

Drag-and-drop reordering of resource packs is driven by pointer and `DragDrop` events and is only
exercised by hand. The ▲▼ buttons call the same `MoveToAsync` entry point, which is unit tested, so
the ordering rules are covered even though the gesture is not.

## Manual release gates

| End-to-end flow | Windows | macOS | Linux |
|---|---|---|---|
| Fresh install and UI rendering | Done | Required | Required |
| Login, restart, silent restore | Done | Required | Required |
| Java discovery/install and modded game launch | Required | Required (x64 + arm64) | Required |
| Discord IPC discovery | Required | Required | Required (native, Flatpak, Snap) |
| Distribution update / cancel / offline fallback | Required | Required | Required |
| Signed package update | Required | Required + notarization | Required |

Public release still needs the real distribution endpoint filled in, target-OS machines, and
signing credentials (Windows certificate, Apple Developer signing/notarization). Shader settings must
be confirmed against the production modpack, since Iris and OptiFine write different option targets.
