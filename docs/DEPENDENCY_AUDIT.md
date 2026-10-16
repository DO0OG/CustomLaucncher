# Dependency audit

Korean version: [DEPENDENCY_AUDIT.ko.md](DEPENDENCY_AUDIT.ko.md)

## Launcher runtime

| Dependency | Version | Purpose / decision |
|---|---:|---|
| Avalonia | 11.3.12 | Cross-platform desktop UI (MIT) |
| CmlLib.Core | 4.0.6 | Minecraft install and launch orchestration (MIT) |
| CmlLib.Core.Auth.Microsoft | 3.3.1 | Minecraft/Xbox authentication pipeline (MIT) |
| CmlLib.Core.Installer.Forge | 1.1.1 | Forge installation (MIT) |
| CmlLib.Core.Installer.NeoForge | 4.0.1 | NeoForge installation (MIT) |
| XboxAuthNet.Game.Msal | 0.1.2 | MSAL browser + device-code providers and OS-protected token cache (MIT) |
| SharpCompress | 0.50.5 | 7z extraction. Every entry is preflighted through `ModuleValidation` and streamed. |
| Velopack | 1.2.0 | Startup hook only (`VelopackApp.Build().Run()`). No update check/apply UI or update feed yet (MIT) |
| DiscordRichPresence | 1.6.1.70 | Optional Discord IPC, behind a compile flag and a user opt-in (MIT) |

## Manifest tool

Shares `CustomLauncher.Shared` and references Avalonia (11.3.12, MIT) for its window. No other direct dependencies besides the Tmds.DBus.Protocol
pin noted below.

## Notes

- Test-only packages (xUnit, Apache-2.0) are omitted from the in-app attribution list because they
  are not shipped.
- Legacy direct dependencies were removed: WebView2, NAudio, RestSharp, SevenZipSharp, LZMA-SDK,
  Newtonsoft.Json, System.Web, System.Deployment, and the manually pinned BCL assemblies.
- The bundled third-party font was removed because redistribution permission could not be
  established.
- No plaintext token file is written. Tokens are held by the MSAL cache using OS protection where
  available (DPAPI on Windows, Keychain on macOS, libsecret keyring on Linux).
- Tmds.DBus.Protocol is a transitive dependency of Avalonia.Desktop (MIT). It is pinned directly
  to 0.21.3 in CustomLauncher and CustomLauncher.ManifestTool because of GHSA-xrw6-gwf8-vvr9. It
  is part of the Avalonia stack rather than a new runtime feature, so it is not added to the
  in-app license list (`Views/AboutWindow.axaml`). Once Avalonia is upgraded to a version that
  pulls in Tmds.DBus.Protocol 0.21.3 or later, the pin can be removed.
- HtmlAgilityPack is a transitive dependency of CmlLib.Core.Installer.NeoForge (MIT). It is a
  pure parsing library used by the installer, so it is not added to the in-app license list.

Keep this file and `Views/AboutWindow.axaml` in sync whenever a runtime dependency changes.
