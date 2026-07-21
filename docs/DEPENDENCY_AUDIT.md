# Dependency audit

| Dependency | Version | TFM / role | Decision |
|---|---:|---|---|
| Avalonia | 11.3.12 | .NET 8 desktop UI | Added as the cross-platform UI layer (MIT) |
| CmlLib.Core | 4.0.6 | netstandard2.0 Minecraft orchestration | Retained; CI exercises loading on three OSes |
| CmlLib.Core.Auth.Microsoft | 3.3.1 | netstandard2.0 Microsoft/Xbox authentication | Retained without WebView2 UI packages; uses the package account cache |
| CmlLib.Core.Installer.Forge | 1.1.1 | Forge installer | Retained |
| SharpCompress | 0.44.0 | net8 archive support | Retained for 7z support. The current advisory targets `WriteToDirectory`; that API is not used. Every entry is validated and streamed through `ModuleValidation`, and the exact advisory is audit-suppressed with this rationale. |
| Velopack | 1.2.0 | launcher update bootstrap | Added; release signing remains credential-gated |
| DiscordRichPresence | 1.6.1.70 | cross-platform Discord IPC | Added behind a compile-time master flag and user opt-in (MIT) |
| xUnit | 2.9.3 | net8 tests | Added for portable core regression tests |

Removed direct references: RestSharp, SevenZipSharp, SevenZipSharp.Interop,
LZMA-SDK, WebView2 UI, NAudio, Newtonsoft.Json, System.Web, System.Deployment,
and manually pinned BCL assemblies. Transitive dependencies remain controlled by
NuGet lock resolution. Background audio was deliberately excluded because the
legacy fields never played audio and the viable native runtimes add license and
distribution costs disproportionate to the feature.

## Authentication cache finding

The authentication package stores its account cache as `cml_accounts.json` via
XboxAuthNet's JSON account manager. The old `customServer_udata` access token was
never read and duplicated sensitive material, so the v2 application does not
write it and deletes the obsolete file during the one-time Windows migration.
Interactive login and cache persistence still require live-account testing on
each target OS; no test credential is stored in the repository.
