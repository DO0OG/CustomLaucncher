# Microsoft authentication operations

## Setup (required before login works at all)

`LauncherConfig.MicrosoftClientId` ships empty. None of the libraries in this solution carry a
built-in client id, so login is disabled and the launcher shows a setup notice until an operator
registers an application and fills this in:

1. Azure Portal → Microsoft Entra ID → App registrations → New registration.
2. Supported account types: personal Microsoft accounts (add organizational accounts only if the
   deployment needs them).
3. Authentication → Advanced settings → Allow public client flows: **Yes**. Device code cannot work
   without this.
4. Copy the Application (client) ID into `LauncherConfig.MicrosoftClientId`.
5. Request Minecraft API access for that client id from Mojang/Microsoft. Sign-in succeeds without
   it, but the Minecraft profile call will be rejected, so complete this before shipping.


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
