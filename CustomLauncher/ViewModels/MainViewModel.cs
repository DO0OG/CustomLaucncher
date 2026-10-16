using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Threading;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using CustomLauncher.Models;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.ViewModels;

public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly AppPaths _paths;
    private readonly AppSettingsManager _settingsManager;
    private readonly DebugLogger _logger;
    private readonly IAuthService _auth;
    private readonly ILauncherService _launcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _statusHttpClient;
    private readonly HttpClient _contentHttpClient;
    private readonly Func<string, IContentUpdateService> _contentFactory;
    private readonly ServerStatusPollingService _statusPolling;
    private readonly SemaphoreSlim _serverSwitch = new(1, 1);
    private CancellationTokenSource? _currentOperation;
    private IDiscordPresenceService? _discord;
    private ProcessWrapper? _gameProcessWrapper;
    private Process? _gameProcess;
    private readonly Func<ServerEntry?, CancellationToken, Task<ServerStatusInfo>> _checkStatus;
    private IReadOnlyList<ServerEntry> _servers = [];
    private ServerEntry? _selectedServer;
    private int _statusGeneration;
    private LauncherSettings _settings = new();
    private MSession? _session;
    private IReadOnlyList<AuthAccount> _accounts = [];
    private AuthAccount? _selectedAccount;
    private string _status = "초기화 중...";
    private string _account = "로그인하지 않음";
    private double _progress;
    private bool _busy;
    private bool _windowActive = true;
    private bool _serverOnline;
    private string _serverStatus = "서버 확인 중...";
    private string _serverMotd = string.Empty;

    public MainViewModel(AppPaths paths, AppSettingsManager settingsManager, DebugLogger logger)
        : this(paths, settingsManager, logger, new AuthService(paths), new LauncherService()) { }

    public MainViewModel(
        AppPaths paths,
        AppSettingsManager settingsManager,
        DebugLogger logger,
        IAuthService auth,
        ILauncherService launcher,
        Func<string, IContentUpdateService>? contentFactory = null,
        Func<ServerEntry?, CancellationToken, Task<ServerStatusInfo>>? checkStatus = null)
    {
        _paths = paths;
        _settingsManager = settingsManager;
        _logger = logger;
        _auth = auth;
        if (_auth is AuthService deviceAuth) deviceAuth.DeviceCodeReceived += OnDeviceCodeReceived;
        _launcher = launcher;
        _statusHttpClient = new HttpClient();
        _contentHttpClient = new HttpClient();
        _contentHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherConfig.UserAgent);
        _contentFactory = contentFactory ?? (path => new ContentUpdateService(_contentHttpClient, path));
        _checkStatus = checkStatus ?? ((server, token) =>
        {
            server ??= LauncherServers.Legacy;
            return new ServerStatusChecker(_statusHttpClient, server.StatusUrl, logger: _logger)
                .CheckAsync(server.Address, server.Port, token);
        });
        _statusPolling = new ServerStatusPollingService(
            token => _checkStatus(_selectedServer, token), () => WindowActive);
        _statusPolling.StatusChanged += OnServerStatusChanged;
        LoginCommand = new AsyncCommand(LoginAsync, () => !Busy && AuthConfigured);
        AddAccountCommand = new AsyncCommand(AddAccountAsync, () => !Busy && AuthConfigured);
        LogoutCommand = new AsyncCommand(LogoutAsync, () => !Busy && _session is not null);
        LaunchCommand = new AsyncCommand(LaunchAsync, () => !Busy && _session is not null);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => Busy && _currentOperation is not null);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? SettingsRequested;

    /// <summary>Raised once the game process has started and the user asked to close the launcher then.</summary>
    public event EventHandler? CloseRequested;
    public ICommand LoginCommand { get; }
    public ICommand AddAccountCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public AppPaths Paths => _paths;
    public string AppDisplayName => LauncherConfig.GameLauncherName;
    /// <summary>
    /// The main screen shows the server's name, not its address. This is presentation, not
    /// protection: the address is a compile-time constant and readable straight out of the binary.
    /// </summary>
    public string ServerName => _selectedServer?.Name ?? LauncherConfig.ServerName;

    /// <summary>Kept for the About window and logs, where knowing the endpoint helps support.</summary>
    public string ServerEndpoint => _selectedServer is { } server
        ? ServerCatalog.JoinAddress(server)
        : $"{LauncherConfig.ServerIp}:{LauncherConfig.ServerPort}";

    public IReadOnlyList<ServerEntry> Servers => _servers;
    public bool HasMultipleServers => _servers.Count > 1;

    /// <summary>Picking another server while idle switches the saved selection and the status polling.</summary>
    public ServerEntry? SelectedServer
    {
        get => _selectedServer;
        set
        {
            // The combo box writes null while its items are being replaced; that is not a user choice.
            if (value is null) return;
            _ = SelectServerAsync(value);
        }
    }
    public IReadOnlyList<AuthAccount> Accounts => _accounts;
    public bool HasMultipleAccounts => _accounts.Count > 1;

    /// <summary>Picking another account while idle restores its session and saves the choice.</summary>
    public AuthAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            // The combo box writes null while its items are being replaced; that is not a user choice.
            if (value is null) return;
            _ = SwitchAccountAsync(value);
        }
    }
    public LauncherSettings Settings => _settings;
    public bool WindowActive { get => _windowActive; set => SetProperty(ref _windowActive, value); }
    public string ServerStatus { get => _serverStatus; private set => SetProperty(ref _serverStatus, value); }
    public string ServerMotd { get => _serverMotd; private set => SetProperty(ref _serverMotd, value); }
    public string DeviceCode { get; private set; } = string.Empty;
    public string DeviceCodeUrl { get; private set; } = string.Empty;

    /// <summary>The device-code panel only belongs on screen while a code is pending.</summary>
    public bool HasDeviceCode => !string.IsNullOrEmpty(DeviceCode);

    /// <summary>
    /// False until an operator fills in the Microsoft client id. Without it the login button would
    /// fail on every click with a generic error, so the launcher says so up front instead.
    /// </summary>
    public bool AuthConfigured => LauncherConfig.IsMicrosoftAuthConfigured;
    public bool HasConfigurationWarning => !AuthConfigured;
    public string ConfigurationWarning =>
        "Microsoft 로그인이 아직 설정되지 않았습니다. 이 런처를 배포하는 운영자가 "
        + "LauncherConfig.MicrosoftClientId에 Azure 앱의 클라이언트 ID를 넣어야 로그인할 수 있습니다.";

    /// <summary>Swaps the primary action between "sign in" and "play".</summary>
    public bool IsAuthenticated => _session is not null;
    public bool IsSignedOut => _session is null;
    public bool ServerOnline { get => _serverOnline; private set => SetProperty(ref _serverOnline, value); }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Account { get => _account; private set => SetProperty(ref _account, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (!SetProperty(ref _busy, value)) return;
            NotifyCommandStates();
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            Status = "설정을 불러오는 중...";
            _settings = await _settingsManager.LoadAsync(_lifetime.Token);
            RaisePropertyChanged(nameof(Settings));
            var content = _contentFactory(string.IsNullOrWhiteSpace(_settings.InstallPath)
                ? _paths.DefaultGameDir : _settings.InstallPath);
            var cached = await content.TryLoadCacheAsync(_lifetime.Token)
                ?? LauncherServers.Normalize(new ServerDistribution());
            SetServers(cached, _settings.SelectedServerId);
            var accounts = await _auth.GetAccountsAsync(_lifetime.Token);
            var picked = AuthService.PickAccount(accounts, _settings.SelectedAccountId);
            _session = picked is null
                ? await _auth.TryRestoreAsync(_lifetime.Token)
                : await _auth.RestoreAccountAsync(picked.Id, _lifetime.Token);
            SetAccounts(accounts, picked);
            _discord = new DiscordPresenceService(LauncherConfig.DiscordClientId, LauncherConfig.EnableDiscordRpc,
                _settings.DiscordRpcEnabled, () => new DiscordRpcClientAdapter(),
                (message, exception) => _ = _logger.WriteAsync(LauncherLogLevel.Warn, message, exception));
            ApplyDiscordServer();
            _discord.Init();
            _discord.SetState(_session is null ? DiscordPresenceState.LauncherOpen : DiscordPresenceState.Ready);
            Account = _session?.Username ?? "로그인하지 않음";
            Status = IdleStatus();
            _ = RefreshServersAsync(content);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = "초기화에 실패했습니다. 로그를 확인해 주세요.";
            await _logger.WriteAsync(LauncherLogLevel.Error, "Application initialization failed", exception);
        }
        finally
        {
            _statusPolling.Start();
            NotifyAuthState();
        }
    }

    /// <summary>
    /// One startup fetch of the live manifest. It only refreshes the on-screen list: the cache file
    /// keeps meaning "last verified content", so nothing is saved here.
    /// </summary>
    private async Task RefreshServersAsync(IContentUpdateService content)
    {
        try
        {
            var fetched = await content.FetchAsync(_lifetime.Token);
            var previous = _selectedServer;
            SetServers(fetched, previous?.Id);
            ApplyDiscordServer();
            if (previous?.Id != _selectedServer?.Id || previous?.StatusUrl != _selectedServer?.StatusUrl)
            {
                await _serverSwitch.WaitAsync(_lifetime.Token);
                try { await RestartStatusPollingAsync(); }
                finally { _serverSwitch.Release(); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await _logger.WriteAsync(LauncherLogLevel.Warn, "Server list refresh failed; keeping the cached list", exception);
        }
    }

    private void SetServers(ServerDistribution manifest, string? preferredId)
    {
        _servers = manifest.Servers;
        _selectedServer = ServerCatalog.Select(manifest, preferredId);
        RaisePropertyChanged(nameof(Servers));
        RaisePropertyChanged(nameof(HasMultipleServers));
        RaisePropertyChanged(nameof(SelectedServer));
        RaisePropertyChanged(nameof(ServerName));
        RaisePropertyChanged(nameof(ServerEndpoint));
    }

    private void ApplyDiscordServer()
    {
        if (_selectedServer is not { } server) return;
        _discord?.SetServer(server.Discord?.Details ?? server.Name, server.Discord?.LargeImageKey);
    }

    private async Task SelectServerAsync(ServerEntry server)
    {
        if (Busy)
        {
            RaisePropertyChanged(nameof(SelectedServer));
            return;
        }
        if (string.Equals(server.Id, _selectedServer?.Id, StringComparison.Ordinal)) return;
        _selectedServer = server;
        _settings.SelectedServerId = server.Id;
        RaisePropertyChanged(nameof(SelectedServer));
        RaisePropertyChanged(nameof(ServerName));
        RaisePropertyChanged(nameof(ServerEndpoint));
        ApplyDiscordServer();
        try
        {
            // One switch at a time: polling restarts and settings writes never overlap.
            await _serverSwitch.WaitAsync(_lifetime.Token);
            try
            {
                await RestartStatusPollingAsync();
                await SaveSettingsAsync();
            }
            finally { _serverSwitch.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await _logger.WriteAsync(LauncherLogLevel.Warn, "Switching the selected server failed", exception);
        }
    }

    /// <summary>
    /// Stops the running poll, then starts one for the current server. Posted results from the
    /// previous poll are dropped through the generation counter so they never label the new server.
    /// Callers hold <see cref="_serverSwitch"/>.
    /// </summary>
    private async Task RestartStatusPollingAsync()
    {
        await _statusPolling.StopAsync();
        Interlocked.Increment(ref _statusGeneration);
        ServerOnline = false;
        ServerStatus = "서버 확인 중...";
        ServerMotd = string.Empty;
        _statusPolling.Start();
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _settingsManager.SaveAsync(_settings, linked.Token);
        _discord?.SetEnabled(_settings.DiscordRpcEnabled);
        RaisePropertyChanged(nameof(Settings));
    }

    public SettingsViewModel CreateSettingsViewModel() =>
        new(_settings, cancellationToken => SaveSettingsAsync(cancellationToken), _paths,
            _selectedServer?.Id ?? ServerCatalog.LegacyServerId);

    public async Task LoginAsync()
    {
        if (Busy) return;
        await RunSignInAsync("Microsoft 계정 로그인 중...", async token =>
        {
            var session = await _auth.AuthenticateAsync(token);
            if (session is not null)
            {
                var accounts = await _auth.GetAccountsAsync(token);
                var picked = accounts.FirstOrDefault(account => account.DisplayName == session.Username)
                    ?? AuthService.PickAccount(accounts, _settings.SelectedAccountId);
                SetAccounts(accounts, picked);
                await SaveSelectedAccountAsync();
            }
            ApplySession(session);
            Status = session is null ? "로그인하지 못했습니다." : "게임을 시작할 수 있습니다.";
        });
    }

    public async Task AddAccountAsync()
    {
        if (Busy) return;
        await RunSignInAsync("계정을 추가하는 중...", async token =>
        {
            if (await _auth.AddAccountAsync(token) is not var (account, session))
            {
                Status = IdleStatus();
                return;
            }
            var accounts = (await _auth.GetAccountsAsync(token)).Where(known => known.Id != account.Id).Append(account).ToList();
            SetAccounts(accounts, account);
            await SaveSelectedAccountAsync();
            ApplySession(session);
            Status = "계정을 추가했습니다.";
        });
    }

    private async Task SwitchAccountAsync(AuthAccount account)
    {
        if (Busy)
        {
            RaisePropertyChanged(nameof(SelectedAccount));
            return;
        }
        if (string.Equals(account.Id, _selectedAccount?.Id, StringComparison.Ordinal)) return;
        await RunSignInAsync("계정을 전환하는 중...", async token =>
        {
            var session = await _auth.RestoreAccountAsync(account.Id, token);
            SetAccounts(_accounts, account);
            await SaveSelectedAccountAsync();
            ApplySession(session);
            Status = session is null ? IdleStatus() : "계정을 전환했습니다.";
        });
    }

    private string IdleStatus() => _session is not null ? "게임을 시작할 수 있습니다."
        : AuthConfigured ? "로그인이 필요합니다."
        : "로그인 설정이 필요합니다.";

    private void ApplySession(MSession? session)
    {
        _session = session;
        Account = session?.Username ?? "로그인하지 않음";
        _discord?.SetState(session is null ? DiscordPresenceState.LauncherOpen : DiscordPresenceState.Ready);
    }

    private void SetAccounts(IReadOnlyList<AuthAccount> accounts, AuthAccount? selected)
    {
        _accounts = accounts;
        _selectedAccount = selected;
        RaisePropertyChanged(nameof(Accounts));
        RaisePropertyChanged(nameof(HasMultipleAccounts));
        RaisePropertyChanged(nameof(SelectedAccount));
    }

    private async Task SaveSelectedAccountAsync()
    {
        if (_selectedAccount is not { } selected || _settings.SelectedAccountId == selected.Id) return;
        _settings.SelectedAccountId = selected.Id;
        await SaveSettingsAsync();
    }

    /// <summary>Shared Busy/cancel/error handling for every flow that talks to the account service.</summary>
    private async Task RunSignInAsync(string progress, Func<CancellationToken, Task> signIn)
    {
        using var operation = BeginOperation();
        Busy = true;
        Status = progress;
        try
        {
            await signIn(operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            Status = "로그인이 취소되었습니다.";
        }
        catch (JEAuthException exception) when (AuthErrors.IsMinecraftForbidden(exception))
        {
            // Microsoft, Xbox Live and XSTS all succeeded and Minecraft itself refused the token.
            // Saying "check the log" here hides the two things that actually cause it.
            Status = "마인크래프트 계정 확인을 거부했습니다(403). 이 런처의 클라이언트 ID가 "
                + "마인크래프트 API 사용 승인을 받았는지, 그리고 이 계정이 마인크래프트를 "
                + "보유했는지 확인해 주세요.";
            await _logger.WriteAsync(LauncherLogLevel.Error, "Minecraft rejected the Xbox token", exception);
        }
        catch (InvalidOperationException exception)
        {
            // Configuration problems are actionable, so show what actually went wrong.
            Status = exception.Message;
            await _logger.WriteAsync(LauncherLogLevel.Error, "Authentication failed", exception);
        }
        catch (Exception exception)
        {
            Status = "로그인 중 오류가 발생했습니다. 로그를 확인해 주세요.";
            await _logger.WriteAsync(LauncherLogLevel.Error, "Authentication failed", exception);
        }
        finally
        {
            // The code is single-use; leaving it on screen after the attempt is noise.
            SetDeviceCode(string.Empty, string.Empty);
            RaisePropertyChanged(nameof(SelectedAccount));
            NotifyAuthState();
            EndOperation(operation);
            Busy = false;
        }
    }

    public async Task LogoutAsync()
    {
        if (_session is null || Busy) return;
        using var operation = BeginOperation();
        Busy = true;
        Status = "로그아웃하는 중...";
        MSession? next = null;
        try
        {
            if (_selectedAccount is { } selected) await _auth.SignOutAccountAsync(selected.Id, operation.Token);
            else await _auth.SignOutAsync(operation.Token);
            Status = "로그아웃했습니다.";
            var accounts = await _auth.GetAccountsAsync(operation.Token);
            SetAccounts(accounts, null);
            if (accounts.FirstOrDefault() is { } first
                && await _auth.RestoreAccountAsync(first.Id, operation.Token) is { } restored)
            {
                next = restored;
                SetAccounts(accounts, first);
                await SaveSelectedAccountAsync();
                Status = "계정을 전환했습니다.";
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            // Half-cleared credentials cannot be restored, so the session is dropped regardless.
            // Reporting a plain "cancelled" here would imply the user is still signed in.
            Status = "로그아웃을 중단했지만 계정 연결은 해제했습니다. 다시 로그인해 주세요.";
        }
        catch (Exception exception)
        {
            // The cached credentials may survive, so say so rather than claiming a clean sign-out.
            Status = "로그아웃 중 오류가 발생했습니다. 로그를 확인해 주세요.";
            await _logger.WriteAsync(LauncherLogLevel.Error, "Sign-out failed", exception);
        }
        finally
        {
            // The in-memory session is dropped either way: leaving the launcher showing a signed-in
            // account it can no longer vouch for is worse than forcing another sign-in.
            ApplySession(next);
            NotifyAuthState();
            EndOperation(operation);
            Busy = false;
        }
    }

    public async Task LaunchAsync()
    {
        if (_session is null || Busy) return;
        using var operation = BeginOperation();
        Busy = true;
        _discord?.SetState(DiscordPresenceState.LaunchingGame);
        Status = "게임 파일을 준비하는 중...";
        try
        {
            var progress = new Progress<LaunchProgress>(value => Dispatcher.UIThread.Post(() =>
            {
                Progress = value.Ratio * 100;
                Status = value.ToStatusText();
            }));
            var prepared = await _launcher.PrepareGameSessionAsync(_settings, _session, progress, operation.Token);
            AttachGameProcess(prepared.Process);
            _gameProcessWrapper!.StartWithEvents();
            _discord?.SetState(DiscordPresenceState.Playing);
            Status = prepared.Warning ?? "게임 실행 중";
            if (_settings.CloseLauncherOnGameStart) CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            Status = "게임 준비가 취소되었습니다.";
            _discord?.SetState(DiscordPresenceState.Ready);
            Busy = false;
        }
        catch (Exception exception)
        {
            Busy = false;
            Status = exception.Message;
            _discord?.SetState(DiscordPresenceState.Ready);
            await _logger.WriteAsync(LauncherLogLevel.Error, "Game launch failed", exception);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    public void CancelCurrentOperation() => _currentOperation?.Cancel();

    private CancellationTokenSource BeginOperation()
    {
        _currentOperation?.Dispose();
        _currentOperation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        NotifyCommandStates();
        return _currentOperation;
    }

    private void EndOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(_currentOperation, operation)) _currentOperation = null;
        NotifyCommandStates();
    }

    private void AttachGameProcess(Process process)
    {
        DetachGameProcess();
        _gameProcess = process;
        _gameProcessWrapper = new ProcessWrapper(process);
        _gameProcessWrapper.OutputReceived += OnGameOutputReceived;
        _gameProcessWrapper.Exited += OnGameExited;
    }

    private void DetachGameProcess()
    {
        if (_gameProcessWrapper is not null)
        {
            _gameProcessWrapper.OutputReceived -= OnGameOutputReceived;
            _gameProcessWrapper.Exited -= OnGameExited;
            _gameProcessWrapper = null;
        }
        _gameProcess?.Dispose();
        _gameProcess = null;
    }

    private void OnGameOutputReceived(object? sender, string line) =>
        _ = _logger.WriteAsync(LauncherLogLevel.Debug, $"[GAME] {line}");

    /// <summary>Message for a non-zero exit, pointing at the newest crash report and the game log when they exist.</summary>
    public static string DescribeAbnormalExit(int exitCode, string gameDirectory)
    {
        var message = $"게임이 비정상 종료되었습니다(코드 {exitCode}).";
        var crashDirectory = Path.Combine(gameDirectory, "crash-reports");
        var newestCrash = Directory.Exists(crashDirectory)
            ? new DirectoryInfo(crashDirectory).EnumerateFiles().OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (newestCrash is not null) message += $" 충돌 보고서: {newestCrash.FullName}.";
        var log = Path.Combine(gameDirectory, "logs", "latest.log");
        if (File.Exists(log)) message += $" 로그: {log}.";
        return message;
    }

    private int? ReadExitCode()
    {
        try { return _gameProcess?.ExitCode; }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private string DescribeGameExit()
    {
        if (ReadExitCode() is not { } code || code == 0) return "게임이 종료되었습니다.";
        // The server cannot change while a game runs (switching is blocked while busy).
        var gameDirectory = LauncherServers.GameDirectoryFor(
            string.IsNullOrWhiteSpace(_settings.InstallPath) ? _paths.DefaultGameDir : _settings.InstallPath,
            _selectedServer?.Id ?? ServerCatalog.LegacyServerId);
        try { return DescribeAbnormalExit(code, gameDirectory); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"게임이 비정상 종료되었습니다(코드 {code}).";
        }
    }

    private void OnGameExited(object? sender, EventArgs eventArgs) => Dispatcher.UIThread.Post(() =>
    {
        var exitStatus = DescribeGameExit();
        DetachGameProcess();
        Busy = false;
        Status = exitStatus;
        _discord?.SetState(DiscordPresenceState.Ready);
    });

    private void OnServerStatusChanged(object? sender, ServerStatusInfo status)
    {
        var generation = Volatile.Read(ref _statusGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == Volatile.Read(ref _statusGeneration)) ApplyServerStatus(status);
        });
    }

    private void ApplyServerStatus(ServerStatusInfo status)
    {
        ServerOnline = status.RequestSucceeded && status.IsOnline;
        var ping = status.PingMilliseconds is { } milliseconds ? $" · {milliseconds}ms" : string.Empty;
        ServerStatus = status.RequestSucceeded
            ? status.IsOnline ? $"온라인 · {status.OnlinePlayers ?? 0}/{status.MaxPlayers ?? 0}명{ping}" : "오프라인"
            : "상태 확인 불가";
        ServerMotd = status.Motd;
    }

    private void NotifyCommandStates()
    {
        ((AsyncCommand)LoginCommand).NotifyCanExecuteChanged();
        ((AsyncCommand)AddAccountCommand).NotifyCanExecuteChanged();
        ((AsyncCommand)LogoutCommand).NotifyCanExecuteChanged();
        ((AsyncCommand)LaunchCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
    }

    private void OnDeviceCodeReceived(object? sender, DeviceCodeInfo info) => Dispatcher.UIThread.Post(() =>
    {
        SetDeviceCode(info.UserCode, info.VerificationUrl);
        Status = "브라우저에서 코드를 입력해 로그인을 완료해 주세요.";
    });

    private void SetDeviceCode(string code, string url)
    {
        DeviceCode = code;
        DeviceCodeUrl = url;
        RaisePropertyChanged(nameof(DeviceCode));
        RaisePropertyChanged(nameof(DeviceCodeUrl));
        RaisePropertyChanged(nameof(HasDeviceCode));
    }

    private void NotifyAuthState()
    {
        RaisePropertyChanged(nameof(IsAuthenticated));
        RaisePropertyChanged(nameof(IsSignedOut));
        NotifyCommandStates();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _currentOperation?.Cancel();
        _statusPolling.StatusChanged -= OnServerStatusChanged;
        if (_auth is AuthService deviceAuth) deviceAuth.DeviceCodeReceived -= OnDeviceCodeReceived;
        await _statusPolling.DisposeAsync();
        DetachGameProcess();
        _statusHttpClient.Dispose();
        _contentHttpClient.Dispose();
        _discord?.Dispose();
        _launcher.Dispose();
        _currentOperation?.Dispose();
        _lifetime.Dispose();
    }
}
