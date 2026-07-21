# Microsoft authentication operations

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
