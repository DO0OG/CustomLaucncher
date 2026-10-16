# QA matrix

Korean version: [QA_MATRIX.ko.md](QA_MATRIX.ko.md)

## Automated evidence

CI runs restore, build, unit tests, and `dotnet format --verify-no-changes` on Windows, macOS, and
Linux for every pull request. The suite is 151 tests covering:

- Path mapping/migration and per-launcher directory scoping.
- Settings transactions (validate-before-mutate, atomic write, schema version).
- Manifest validation and staged updates: HTTPS, SHA-256 re-check, archive path traversal, archive
  limits, cancellation, and drop-in preservation.
- Mod-loader selection, Java discovery/validation/factory policy, and RAM policy from `RamCalculator`.
- Quilt and NeoForge install branches with a fake backend, including reuse of an installed version
  (a NeoForge `21.1.10` request does not reuse an installed `21.1.100`).
- Server status parsing and backoff; the multiplayer-list (`servers.dat`) writer, including modified
  UTF-8 round-tripping and merge preservation.
- Server List Ping (SLP): VarInt and handshake encoding, a round trip against a local fake server,
  parsing of both description forms (plain string and component object), offline conversion for
  timeout, closed port, and oversized responses, and fallback to SLP when `statusUrl` fails.
- `options.txt` targeted editing and resource-pack ordering.
- Content tab behaviour: every `OptionsEditStatus` branch reaches the user, a required server pack
  refuses to move/disable with a message instead of throwing, optional-module toggles cascade to
  submodules and persist, and a manifest fetch failure becomes a message not an unhandled fault.
- Auth error mapping: the 403 that Minecraft returns is recognised whether the library fills
  `StatusCode` or only the message.
- Sign-out: the session is cleared and the primary action swaps back to sign-in, and the session is
  still dropped when clearing the cached credentials throws.
- Sign-out: the session is cleared and the primary action swaps back to sign-in, and the session is
  still dropped when clearing the cached credentials throws.
- Multiple accounts: the account picker appears only with two or more accounts, choosing one switches
  and persists `SelectedAccountId`, and the saved account is restored with fallback to the first account
  (`CustomLauncher.Tests/AuthAccountTests.cs`, `MainViewModelLifecycleTests.cs`).
- Per-account sign-out: with a fake auth service, the selected account's id is passed to the individual
  sign-out call, the first remaining account becomes active and is saved, and the signed-out state is
  shown when no account remains (`CustomLauncher.Tests/MainViewModelLifecycleTests.cs`). Real MSAL cache
  and account file deletion is not covered by this test; see "Not covered by automation".
- Privacy: account identifiers, email addresses and tokens do not appear in logs or status text
  (`CustomLauncher.Tests/AuthAccountTests.cs`, `MainViewModelLifecycleTests.cs`).
- Progress status text (`게임 파일 설치 3/10 파일, 5.0/20.0 MB` form and the file-only variant).
- Non-zero exit message with the newest crash-report and log paths.
- Close-launcher-after-start request, and persistence of the `FullScreen` and `CloseLauncherOnGameStart` settings.
- Drop-in mod enable/disable (rename to `.disabled`), and refusal to toggle server-managed mods.
- Java candidate sequential check, mismatch notice for a configured Java, and install version selection in the settings window.
- Discord Details includes the server name.
- Manifest tool: hashing, merge preservation, semantic diff, URL templating, and the tool window's
  validation/defaults/change-report.
- File repair: only files whose disk hash differs or that are missing are re-downloaded; if any download
  fails, existing files are kept.
- Archive hash records: the archive hash and extracted-file hashes are stored and compared; an install
  without a record re-downloads once.
- Game log buffer: the 500-line cap and batched UI updates at 100 ms.
- Log folder path calculation under the game folder's `logs` directory.

These are isolated tests with fakes and temporary files; they are not end-to-end launches.

## Verified by hand

- Microsoft login end to end (browser flow, real account) once the client id was approved for the
  Minecraft API. The 403-before-approval path and its on-screen message were also observed.
- The launcher and settings windows render and operate on Windows.

## Not covered by automation

Drag-and-drop reordering of resource packs is driven by pointer and `DragDrop` events and is only
exercised by hand. The ▲▼ buttons call the same `MoveToAsync` entry point, which is unit tested, so
the ordering rules are covered even though the gesture is not.

Adding, switching and signing out two real Microsoft accounts, including deletion of the real MSAL cache
and `minecraft-accounts.json`, is not automated and must be checked by hand.

Real NeoForge and Quilt installation, and status checks against a live server over SLP, are not
automated.

## Manual release gates

| End-to-end flow | Windows | macOS | Linux |
|---|---|---|---|
| Fresh install and UI rendering | Done | Required | Required |
| Login, restart, silent restore | Done | Required | Required |
| Java discovery/install and modded game launch | Required | Required (x64 + arm64) | Required |
| Discord IPC discovery | Required | Required | Required (native, Flatpak, Snap) |
| Distribution update / cancel / offline fallback | Required | Required | Required |
| Fullscreen launch | Required | Required | Required |
| Close launcher after game start (game keeps running) | Required | Required | Required |
| Disabled drop-in mod not loaded in game | Required | Required | Required |
| Crash message with crash-report and log paths | Required | Required | Required |
| Progress numbers in the status line | Required | Required | Required |
| Discord status shows server name | Required | Required | Required |
| Signed package update | Required | Required + notarization | Required |
| Multi-server: switching servers updates name, status, and Discord | Required | Required | Required |
| Multi-server: selected server persists across restart | Required | Required | Required |
| Multi-server: disabled optional modules are kept separately per server | Required | Required | Required |
| Multi-server: `default` keeps the existing game folder, other servers use `instances/{id}`, offline check per server | Required | Required | Required |
| Old single-server manifest and old settings still load (id `default`) | Required | Required | Required |
| Server selector is hidden when the manifest has one server | Required | Required | Required |
| File verification and repair: after one file is tampered with or deleted, the button re-downloads only that file, shows "verified n, repaired m", and keeps existing files when the network is blocked | Required | Required | Required |
| Archive module: an existing install with no record re-downloads once on first run | Required | Required | Required |
| Game log area: shows output while running (latest 500 lines), window stays responsive under heavy output, and "Open log folder" opens the `logs` folder in the OS file explorer | Required | Required | Required |
| Auto-update, dev run or `dotnet publish` output run as-is: the update check is skipped and logged | Required | Required | Required |
| Auto-update, installed build or Portable.zip: the "new version x.y.z" banner and "Update now" button appear on the main window | Required | Required | Required |
| Auto-update: "Update now" is disabled while a task (launch, repair) is running | Required | Required | Required |
| Auto-update: applying is blocked with a notice while the game is running | Required | Required | Required |
| Auto-update: download progress shows in the progress bar and status line, and the launcher restarts after applying | Required | Required | Required |
| Auto-update: "Include prereleases" off ignores a prerelease, on receives it | Required | Required | Required |
| Auto-update: a network failure is ignored silently and written to the log only | Required | Required | Required |
| Auto-update: the info window shows the current version | Required | Required | Required |
| Unsigned installer: Windows SmartScreen warning appears and "More info → Run" installs | Required | N/A | N/A |
| Unsigned installer: macOS Gatekeeper warning appears and right-click → Open launches | N/A | Required | N/A |

Public release still needs the real distribution endpoint filled in, target-OS machines, and
signing credentials (Windows certificate, Apple Developer signing/notarization). Shader settings must
be confirmed against the production modpack, since Iris and OptiFine write different option targets.
