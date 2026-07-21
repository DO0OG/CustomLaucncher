using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using Microsoft.Identity.Client;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;

namespace CustomLauncher.Core;

public sealed record DeviceCodeInfo(string UserCode, string VerificationUrl, DateTimeOffset ExpiresOn, string Message);

public interface IAuthService
{
    Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default);
    Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthService : IAuthService
{
    private readonly AppPaths _paths;
    private readonly string _clientId;
    private readonly Lazy<Task<IPublicClientApplication>> _application;
    private JELoginHandler? _browserHandler;
    private JELoginHandler? _deviceCodeHandler;

    /// <summary>Raised only when the launcher falls back to the device-code flow.</summary>
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;

    /// <param name="clientId">
    /// Overrides <see cref="LauncherConfig.MicrosoftClientId"/>. Tests supply a blank id to exercise
    /// the unconfigured guard; without this they would drive a real sign-in against Microsoft as
    /// soon as an operator filled the constant in.
    /// </param>
    public AuthService(AppPaths? paths = null, string? clientId = null)
    {
        _paths = paths ?? new AppPaths();
        _clientId = clientId ?? LauncherConfig.MicrosoftClientId;
        _application = new Lazy<Task<IPublicClientApplication>>(CreateApplicationAsync);
    }

    private bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId);

    /// <summary>
    /// Signs in through the system browser, which redirects back to the launcher on its own. The
    /// device-code flow is kept as a fallback for machines where no browser can be launched
    /// (headless boxes, SSH sessions, minimal desktops), because there it is the only option.
    /// </summary>
    public async Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var browser = await GetBrowserHandlerAsync();
        try
        {
            return await browser.AuthenticateInteractively(cancellationToken);
        }
        catch (MsalClientException exception) when (CanRetryWithDeviceCode(exception))
        {
            var deviceCode = await GetDeviceCodeHandlerAsync();
            return await deviceCode.AuthenticateInteractively(cancellationToken);
        }
    }

    public async Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;
        try
        {
            var browser = await GetBrowserHandlerAsync();
            return await browser.AuthenticateSilently(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // Restoring a cached session is best effort. Any failure here - no cached account,
            // an expired refresh token, no network - simply means the user has to sign in, and
            // must never be reported as a startup error.
            return null;
        }
    }

    /// <summary>
    /// True when the browser itself could not be used. A cancelled sign-in is a deliberate user
    /// action, so it must not silently restart the whole flow as a device code.
    /// </summary>
    private static bool CanRetryWithDeviceCode(MsalClientException exception) =>
        exception.ErrorCode != MsalError.AuthenticationCanceledError;

    private async Task<JELoginHandler> GetBrowserHandlerAsync() =>
        _browserHandler ??= BuildHandler(new MsalCodeFlowProvider(await _application.Value));

    private async Task<JELoginHandler> GetDeviceCodeHandlerAsync() =>
        _deviceCodeHandler ??= BuildHandler(new MsalDeviceCodeProvider(await _application.Value, result =>
        {
            DeviceCodeReceived?.Invoke(this, new DeviceCodeInfo(
                result.UserCode, result.VerificationUrl, result.ExpiresOn, result.Message));
            return Task.CompletedTask;
        }));

    private JELoginHandler BuildHandler(IAuthenticationProvider provider) =>
        new JELoginHandlerBuilder()
            .WithAccountManager(Path.Combine(_paths.ConfigDir, "minecraft-accounts.json"))
            .WithOAuthProvider(provider)
            .Build();

    private async Task<IPublicClientApplication> CreateApplicationAsync()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("MicrosoftClientId가 설정되지 않았습니다. 운영자 인증 설정을 확인해 주세요.");
        _paths.EnsureCreated();
        var cache = new MsalCacheSettings
        {
            CacheDir = _paths.ConfigDir,
            CacheFileName = "msal-token-cache.bin",
            // Scoped per launcher so separate server builds do not share a keychain entry.
            KeyChainServiceName = $"{_paths.LauncherId}.Msal",
            KeyChainAccountName = "token-cache",
            LinuxKeyRingSchema = $"com.{_paths.LauncherId.ToLowerInvariant()}.msal",
            LinuxKeyRingCollection = "default",
            LinuxKeyRingLabel = $"{_paths.LauncherId} MSAL token cache",
            LinuxKeyRingAttr1 = new("Version", "1"),
            LinuxKeyRingAttr2 = new("Product", _paths.LauncherId)
        };
        return await MsalClientHelper.BuildApplicationWithCache(_clientId, cache);
    }
}
