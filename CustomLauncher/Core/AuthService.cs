using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Microsoft.Identity.Client;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Authenticators;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;
using XboxAuthNet.Game.OAuth;

namespace CustomLauncher.Core;

public sealed record DeviceCodeInfo(string UserCode, string VerificationUrl, DateTimeOffset ExpiresOn, string Message);

/// <summary>화면 표시용 이름과 MSAL 계정 식별자. 식별자는 화면에 보이지 않도록 ToString은 이름만 돌려준다.</summary>
public sealed record AuthAccount(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public interface IAuthService
{
    Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default);
    Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the signed-in account so the next sign-in starts from account selection.
    /// </summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthAccount>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuthAccount>>([]);

    Task<MSession?> RestoreAccountAsync(string accountId, CancellationToken cancellationToken = default) =>
        TryRestoreAsync(cancellationToken);

    /// <summary>Signs in through the browser with the account chooser and keeps the accounts already signed in.</summary>
    Task<(AuthAccount Account, MSession Session)?> AddAccountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<(AuthAccount Account, MSession Session)?>(null);

    /// <summary>Forgets only the given account so the other signed-in accounts stay restorable.</summary>
    Task SignOutAccountAsync(string accountId, CancellationToken cancellationToken = default) =>
        SignOutAsync(cancellationToken);
}

public sealed class AuthService : IAuthService
{
    private readonly AppPaths _paths;
    private readonly string _clientId;
    private readonly Lazy<Task<IPublicClientApplication>> _application;
    private JELoginHandler? _browserHandler;
    private JELoginHandler? _deviceCodeHandler;
    private IXboxGameAccountManager? _accountManager;

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

    /// <summary>저장된 id가 목록에 있으면 그 계정, 없으면 첫 계정, 목록이 비면 null.</summary>
    public static AuthAccount? PickAccount(IReadOnlyList<AuthAccount> accounts, string? selectedId) =>
        accounts.FirstOrDefault(account => account.Id == selectedId) ?? accounts.FirstOrDefault();

    public async Task<IReadOnlyList<AuthAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return [];
        try
        {
            var handler = await GetBrowserHandlerAsync();
            var msalAccounts = await (await _application.Value).GetAccountsAsync();
            return msalAccounts.Select(account => ToAuthAccount(account, FindGameAccount(handler, account))).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // Listing accounts is best effort for the same reason as restoring: an unreadable
            // token cache just means the user has to sign in, never a startup error.
            return [];
        }
    }

    public async Task<MSession?> RestoreAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;
        try
        {
            var handler = await GetBrowserHandlerAsync();
            var account = await FindMsalAccountAsync(accountId);
            var gameAccount = account is null ? null : FindGameAccount(handler, account);
            return gameAccount is null ? null : await handler.AuthenticateSilently(gameAccount, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // Same best-effort contract as TryRestoreAsync: any failure just means signing in again.
            return null;
        }
    }

    public async Task<(AuthAccount Account, MSession Session)?> AddAccountAsync(CancellationToken cancellationToken = default)
    {
        var handler = await GetBrowserHandlerAsync();
        var gameAccount = handler.AccountManager.NewAccount();
        MSession session;
        try
        {
            session = await handler.AuthenticateInteractively(gameAccount, cancellationToken);
        }
        catch (MsalClientException exception) when (CanRetryWithDeviceCode(exception))
        {
            session = await (await GetDeviceCodeHandlerAsync()).AuthenticateInteractively(gameAccount, cancellationToken);
        }

        // The sign-in stores the signed-in Microsoft account name as the account's login hint.
        var loginHint = MicrosoftOAuthLoginHintSource.Default.Get(gameAccount.SessionStorage);
        var account = (await (await _application.Value).GetAccountsAsync())
            .FirstOrDefault(candidate => IsLoginHint(candidate, loginHint))
            ?? throw new InvalidOperationException("로그인한 Microsoft 계정을 찾지 못했습니다.");
        return (ToAuthAccount(account, gameAccount as JEGameAccount), session);
    }

    public async Task SignOutAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return;

        var account = await FindMsalAccountAsync(accountId);
        if (account is null) return;

        var handler = await GetBrowserHandlerAsync();
        var gameAccount = FindGameAccount(handler, account);
        if (gameAccount is not null) await handler.Signout(gameAccount, cancellationToken);
        await (await _application.Value).RemoveAsync(account);
    }

    /// <summary>
    /// Clears both stores that make a sign-in "sticky": the Minecraft account file and the MSAL
    /// token cache. Dropping only the first would let the next sign-in restore the same Microsoft
    /// account silently, so the user could never switch accounts.
    /// </summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return;

        var browser = await GetBrowserHandlerAsync();
        await browser.Signout(cancellationToken);
        // Signout only drops the default account; the others would keep their tokens on disk.
        browser.AccountManager.ClearAccounts();
        browser.AccountManager.SaveAccounts();
        await MsalClientHelper.RemoveAccounts(await _application.Value);

        // The handlers cache the account they signed in with; rebuild them on the next attempt.
        _browserHandler = null;
        _deviceCodeHandler = null;
    }

    /// <summary>
    /// True when the browser itself could not be used. A cancelled sign-in is a deliberate user
    /// action, so it must not silently restart the whole flow as a device code.
    /// </summary>
    private static bool CanRetryWithDeviceCode(MsalClientException exception) =>
        exception.ErrorCode != MsalError.AuthenticationCanceledError;

    private async Task<JELoginHandler> GetBrowserHandlerAsync() =>
        _browserHandler ??= BuildHandler(new SelectAccountCodeFlowProvider(await _application.Value));

    private async Task<IAccount?> FindMsalAccountAsync(string accountId) =>
        (await (await _application.Value).GetAccountsAsync())
            .FirstOrDefault(account => account.HomeAccountId.Identifier == accountId);

    /// <summary>The Minecraft account whose stored login hint is this Microsoft account's name.</summary>
    private static JEGameAccount? FindGameAccount(JELoginHandler handler, IAccount account) =>
        handler.AccountManager.GetAccounts().OfType<JEGameAccount>().FirstOrDefault(gameAccount =>
            IsLoginHint(account, MicrosoftOAuthLoginHintSource.Default.Get(gameAccount.SessionStorage)));

    private static bool IsLoginHint(IAccount account, string? loginHint) =>
        string.Equals(account.Username, loginHint, StringComparison.OrdinalIgnoreCase);

    private static AuthAccount ToAuthAccount(IAccount account, JEGameAccount? gameAccount)
    {
        var profileName = gameAccount?.Profile?.Username;
        return new AuthAccount(account.HomeAccountId.Identifier,
            string.IsNullOrWhiteSpace(profileName) ? account.Username : profileName);
    }

    private async Task<JELoginHandler> GetDeviceCodeHandlerAsync() =>
        _deviceCodeHandler ??= BuildHandler(new MsalDeviceCodeProvider(await _application.Value, result =>
        {
            DeviceCodeReceived?.Invoke(this, new DeviceCodeInfo(
                result.UserCode, result.VerificationUrl, result.ExpiresOn, result.Message));
            return Task.CompletedTask;
        }));

    // Every handler shares one account manager: each keeps its own in-memory copy of the account
    // file and writes all of it back, so separate copies would overwrite each other's accounts.
    private JELoginHandler BuildHandler(IAuthenticationProvider provider) =>
        new JELoginHandlerBuilder()
            .WithAccountManager(_accountManager ??= new JsonXboxGameAccountManager(
                Path.Combine(_paths.ConfigDir, "minecraft-accounts.json"),
                JEGameAccount.FromSessionStorage,
                JsonXboxGameAccountManager.DefaultSerializerOption))
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

    /// <summary>
    /// The MSAL browser code flow, except that interactive sign-in always shows the account
    /// chooser. Without it the browser's current Microsoft session is reused silently and a
    /// second account could never be added.
    /// </summary>
    private sealed class SelectAccountCodeFlowProvider : IAuthenticationProvider
    {
        private readonly MsalOAuthBuilder _builder;
        private readonly MsalCodeFlowProvider _codeFlow;

        public SelectAccountCodeFlowProvider(IPublicClientApplication application)
        {
            _builder = new MsalOAuthBuilder(application);
            _codeFlow = new MsalCodeFlowProvider(_builder);
        }

        public IAuthenticator Authenticate() => _codeFlow.Authenticate();
        public ISessionValidator CreateSessionValidator() => _codeFlow.CreateSessionValidator();
        public IAuthenticator AuthenticateSilently() => _codeFlow.AuthenticateSilently();
        public IAuthenticator AuthenticateInteractively() =>
            _builder.Interactive(builder => builder.WithPrompt(Prompt.SelectAccount));
        public IAuthenticator ClearSession() => _codeFlow.ClearSession();
        public IAuthenticator Signout() => _codeFlow.Signout();
    }
}
