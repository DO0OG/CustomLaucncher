# Dependency audit

Korean version: [DEPENDENCY_AUDIT.ko.md](DEPENDENCY_AUDIT.ko.md)

## Launcher runtime

| Dependency | Version | Purpose / decision |
|---|---:|---|
| Avalonia | 11.3.12 | Cross-platform desktop UI (MIT) |
| CmlLib.Core | 4.0.6 | Minecraft install and launch orchestration (MIT) |
| CmlLib.Core.Auth.Microsoft | 3.3.1 | Minecraft/Xbox authentication pipeline (MIT) |
| CmlLib.Core.Installer.Forge | 1.1.1 | Forge installation (MIT) |
| XboxAuthNet.Game.Msal | 0.1.2 | MSAL browser + device-code providers and OS-protected token cache (MIT) |
| SharpCompress | 0.44.0 | 7z extraction. The suppressed advisory affects `WriteToDirectory`, which the launcher never calls; every entry is preflighted through `ModuleValidation` and streamed. |
| Velopack | 1.2.0 | Signed release update bootstrap (MIT) |
| DiscordRichPresence | 1.6.1.70 | Optional Discord IPC, behind a compile flag and a user opt-in (MIT) |

## Manifest tool

Shares `CustomLauncher.Shared` and references Avalonia (11.3.12, MIT) for its window. No other
dependencies.

## Notes

- Test-only packages (xUnit, Apache-2.0) are omitted from the in-app attribution list because they
  are not shipped.
- Legacy direct dependencies were removed: WebView2, NAudio, RestSharp, SevenZipSharp, LZMA-SDK,
  Newtonsoft.Json, System.Web, System.Deployment, and the manually pinned BCL assemblies.
- The bundled third-party font was removed because redistribution permission could not be
  established.
- No plaintext token file is written. Tokens are held by the MSAL cache using OS protection where
  available (DPAPI on Windows, Keychain on macOS, libsecret keyring on Linux).

Keep this file and `Views/AboutWindow.axaml` in sync whenever a runtime dependency changes.
