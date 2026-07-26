# Microsoft authentication

Korean version: [AUTH_FLOW.ko.md](AUTH_FLOW.ko.md)

Players sign in with their own Microsoft account. The launcher never handles passwords; the token
lives in the OS-protected MSAL cache under the launcher's config directory.

## Setup (once per launcher build)

`LauncherConfig.MicrosoftClientId` ships empty, and no library here carries a built-in id, so login
is disabled and the launcher shows a setup notice until an operator registers an app and fills it in.

1. **Azure Portal → Microsoft Entra ID → App registrations → New registration.**
2. **Supported account types: personal Microsoft accounts.** The library pins the authority to the
   `consumers` tenant, so a directory-only ("organization") registration can never sign a Minecraft
   account in — it fails with `unauthorized_client: not enabled for consumers`.
3. **Authentication → Add a platform → Mobile and desktop applications → redirect URI
   `http://localhost`.** It must be this platform, not "Web". A public-client registration matches any
   loopback port, which is what the browser flow picks at runtime; registering the URI under "Web"
   fails every attempt with
   `invalid_request: The provided value for the input parameter 'redirect_uri' is not valid`.
4. **Authentication → Advanced settings → Allow public client flows: Yes.** The device-code fallback
   cannot work without this.
5. Copy the **Application (client) ID** into `LauncherConfig.MicrosoftClientId`.
6. **Request Minecraft API access for that client id** through the review form: <https://aka.ms/mce-reviewappid>.

The client id is not a secret and may be committed. One registration serves every launcher build and
every player; do not create a separate app per server, because step 6 is per client id.

## The Minecraft API review (step 6 in detail)

Until the client id is approved, Microsoft sign-in, Xbox Live and XSTS all succeed and only the final
`login_with_xbox` call returns **403**. It is easy to mistake this for a bug in the launcher; it is
not. The launcher surfaces it as "마인크래프트 계정 확인을 거부했습니다(403)".

The form asks for the Application ID, the Directory (Tenant) ID, an associated website, and a
justification. Submitting requires that at least one real sign-in attempt has already happened, so
the 403s you see before applying are expected and are what registers the app on Microsoft's side.

**Approval is not announced by email.** There is no confirmation message; login simply starts working,
and Microsoft allows up to ~24 hours after approval for it to take effect. The way to check is to
attempt login again, not to wait for a mail.

## Flows

The launcher signs in through the **system browser** and picks up the loopback redirect itself. If a
browser cannot be launched at all — headless machines, SSH sessions, minimal desktops — it falls back
to the **device-code flow** and shows the code and URL in the window. A cancelled sign-in is treated
as a user decision and does not silently restart as a device code.

`TryRestoreAsync` runs at startup to reuse a cached session. It is best effort: no cached account, an
expired refresh token, or no network all just mean the sign-in button is shown, never a startup error.

The MSAL cache and the Minecraft account file are scoped by `LauncherConfig.LauncherId`, so separate
server builds do not share credentials.
