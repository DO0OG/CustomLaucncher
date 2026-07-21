# Dependency audit

| Runtime dependency | Version | Purpose / decision |
|---|---:|---|
| Avalonia | 11.3.12 | Cross-platform desktop UI (MIT) |
| CmlLib.Core | 4.0.6 | Minecraft install and launch orchestration (MIT) |
| CmlLib.Core.Auth.Microsoft | 3.3.1 | Minecraft/Xbox authentication pipeline (MIT) |
| CmlLib.Core.Installer.Forge | 1.1.1 | Forge installation (MIT) |
| XboxAuthNet.Game.Msal | 0.1.2 | Cross-platform MSAL device-code provider and protected cache (MIT) |
| SharpCompress | 0.44.0 | 7z extraction; the suppressed advisory affects an unused convenience API |
| Velopack | 1.2.0 | Signed release update bootstrap (MIT) |
| DiscordRichPresence | 1.6.1.70 | Optional Discord IPC integration (MIT) |

Test-only packages are intentionally omitted from the in-app attribution list. Direct legacy
dependencies for WebView2, audio, deployment, and archive interop remain removed. The bundled
third-party font was removed because redistribution permission could not be established.

Authentication tokens are stored by MSAL Extensions using OS protection where available. No
plaintext legacy token file is created. Release approval still requires live identity, package
signing, notarization, and target-platform IPC checks.
