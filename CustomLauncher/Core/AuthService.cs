using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using Microsoft.Identity.Client;
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
    private readonly Lazy<Task<JELoginHandler>> _handler;
    public event EventHandler<DeviceCodeInfo>? DeviceCodeReceived;

    public AuthService(AppPaths? paths = null)
    {
        _paths = paths ?? new AppPaths();
        _handler = new Lazy<Task<JELoginHandler>>(CreateHandlerAsync);
    }

    public async Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default) =>
        await (await _handler.Value).AuthenticateInteractively(cancellationToken);

    public async Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(LauncherConfig.MicrosoftClientId)) return null;
        try { return await (await _handler.Value).AuthenticateSilently(cancellationToken); }
        catch (MsalUiRequiredException) { return null; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return null; }
    }

    private async Task<JELoginHandler> CreateHandlerAsync()
    {
        if (string.IsNullOrWhiteSpace(LauncherConfig.MicrosoftClientId))
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
        var application = await MsalClientHelper.BuildApplicationWithCache(LauncherConfig.MicrosoftClientId, cache);
        var provider = new MsalDeviceCodeProvider(application, result =>
        {
            DeviceCodeReceived?.Invoke(this, new DeviceCodeInfo(
                result.UserCode, result.VerificationUrl, result.ExpiresOn, result.Message));
            return Task.CompletedTask;
        });
        return new JELoginHandlerBuilder()
            .WithAccountManager(Path.Combine(_paths.ConfigDir, "minecraft-accounts.json"))
            .WithOAuthProvider(provider)
            .Build();
    }
}
