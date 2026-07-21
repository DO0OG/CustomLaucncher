# Microsoft authentication operations

## Setup (required before login works at all)

`LauncherConfig.MicrosoftClientId` ships empty. None of the libraries in this solution carry a
built-in client id, so login is disabled and the launcher shows a setup notice until an operator
registers an application and fills this in:

1. Azure Portal → Microsoft Entra ID → App registrations → New registration.
2. Supported account types: **personal Microsoft accounts**. The library pins the authority to the
   `consumers` tenant, so a directory-only registration can never sign a Minecraft account in.
3. Authentication → Add a platform → **Mobile and desktop applications** → redirect URI
   `http://localhost`. It must be this platform, not "Web": a public-client registration matches any
   loopback port, which is what MSAL picks at runtime. Registering it under Web fails every attempt
   with `invalid_request: The provided value for the input parameter 'redirect_uri' is not valid`.
4. Authentication → Advanced settings → Allow public client flows: **Yes**. The device-code fallback
   cannot work without this.
5. Copy the Application (client) ID into `LauncherConfig.MicrosoftClientId`.
6. Request Minecraft API access for that client id from Mojang/Microsoft. Microsoft sign-in, Xbox
   Live and XSTS all succeed without it; `login_with_xbox` is what returns 403, so this is the last
   gate and it is easy to mistake for a code bug.

## Flows

The launcher signs in through the system browser and picks up the loopback redirect on its own. If
the browser cannot be launched at all - headless machines, SSH sessions, minimal desktops - it falls
back to the device-code flow and shows the code in the window. A cancelled sign-in is treated as a
user decision and does not silently restart as a device code.


The launcher uses MSAL device-code authentication on Windows, macOS, and Linux. Set
`LauncherConfig.MicrosoftClientId` to an operator-owned Microsoft Entra public-client application.

Required tenant configuration:

- allow organizational and personal Microsoft accounts;
- enable public-client flows;
- register the native-client redirect URI required by MSAL;
- complete Minecraft API client-ID allowlisting before production rollout.

Tokens use the platform-protected MSAL cache under the launcher configuration directory. The UI
shows the short-lived device code and verification URL. Real-account, MFA, child-account, Xbox
policy, ownership, and allowlist behavior require manual validation and are not exercised in CI.
