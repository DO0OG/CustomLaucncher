using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Threading;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using CustomLauncher.Models;

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
    private readonly ServerStatusPollingService _statusPolling;
    private CancellationTokenSource? _currentOperation;
    private IDiscordPresenceService? _discord;
    private ProcessWrapper? _gameProcessWrapper;
    private Process? _gameProcess;
    private LauncherSettings _settings = new();
    private MSession? _session;
    private string _status = "초기화 중...";
    private string _account = "로그인하지 않음";
    private double _progress;
    private bool _busy;
    private bool _windowActive = true;
    private string _serverStatus = "서버 확인 중...";
    private string _serverMotd = string.Empty;

    public MainViewModel(AppPaths paths, AppSettingsManager settingsManager, DebugLogger logger)
        : this(paths, settingsManager, logger, new AuthService(paths), new LauncherService()) { }

    public MainViewModel(
        AppPaths paths,
        AppSettingsManager settingsManager,
        DebugLogger logger,
        IAuthService auth,
        ILauncherService launcher)
    {
        _paths = paths;
        _settingsManager = settingsManager;
        _logger = logger;
        _auth = auth;
        if (_auth is AuthService deviceAuth) deviceAuth.DeviceCodeReceived += OnDeviceCodeReceived;
        _launcher = launcher;
        _statusHttpClient = new HttpClient();
        var statusChecker = new ServerStatusChecker(_statusHttpClient);
        _statusPolling = new ServerStatusPollingService(statusChecker.CheckAsync, () => WindowActive);
        _statusPolling.StatusChanged += OnServerStatusChanged;
        LoginCommand = new AsyncCommand(LoginAsync, () => !Busy);
        LaunchCommand = new AsyncCommand(LaunchAsync, () => !Busy && _session is not null);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => Busy && _currentOperation is not null);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? SettingsRequested;
    public ICommand LoginCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public AppPaths Paths => _paths;
    public LauncherSettings Settings => _settings;
    public bool WindowActive { get => _windowActive; set => SetProperty(ref _windowActive, value); }
    public string ServerStatus { get => _serverStatus; private set => SetProperty(ref _serverStatus, value); }
    public string ServerMotd { get => _serverMotd; private set => SetProperty(ref _serverMotd, value); }
    public string DeviceCode { get; private set; } = string.Empty;
    public string DeviceCodeUrl { get; private set; } = string.Empty;
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
            _session = await _auth.TryRestoreAsync(_lifetime.Token);
            _discord = new DiscordPresenceService(LauncherConfig.DiscordClientId, LauncherConfig.EnableDiscordRpc,
                _settings.DiscordRpcEnabled, () => new DiscordRpcClientAdapter(),
                (message, exception) => _ = _logger.WriteAsync(LauncherLogLevel.Warn, message, exception));
            _discord.Init();
            _discord.SetState(_session is null ? DiscordPresenceState.LauncherOpen : DiscordPresenceState.Ready);
            Account = _session?.Username ?? "로그인하지 않음";
            Status = _session is null ? "로그인이 필요합니다." : "게임을 시작할 수 있습니다.";
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
            NotifyCommandStates();
        }
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _settingsManager.SaveAsync(_settings, linked.Token);
        _discord?.SetEnabled(_settings.DiscordRpcEnabled);
        RaisePropertyChanged(nameof(Settings));
    }

    public SettingsViewModel CreateSettingsViewModel() =>
        new(_settings, cancellationToken => SaveSettingsAsync(cancellationToken), _paths);

    public async Task LoginAsync()
    {
        if (Busy) return;
        using var operation = BeginOperation();
        Busy = true;
        Status = "Microsoft 계정 로그인 중...";
        try
        {
            _session = await _auth.AuthenticateAsync(operation.Token);
            Account = _session?.Username ?? "로그인 실패";
            Status = _session is null ? "로그인하지 못했습니다." : "로그인했습니다.";
            if (_session is not null) _discord?.SetState(DiscordPresenceState.Ready);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            Status = "로그인이 취소되었습니다.";
        }
        catch (Exception exception)
        {
            Status = "로그인 중 오류가 발생했습니다.";
            await _logger.WriteAsync(LauncherLogLevel.Error, "Authentication failed", exception);
        }
        finally
        {
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
                Status = string.IsNullOrWhiteSpace(value.Detail) ? value.Stage : $"{value.Stage}: {value.Detail}";
            }));
            var prepared = await _launcher.PrepareGameSessionAsync(_settings, _session, progress, operation.Token);
            AttachGameProcess(prepared.Process);
            _gameProcessWrapper!.StartWithEvents();
            _discord?.SetState(DiscordPresenceState.Playing);
            Status = prepared.Warning ?? "게임 실행 중";
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

    private void OnGameExited(object? sender, EventArgs eventArgs) => Dispatcher.UIThread.Post(() =>
    {
        DetachGameProcess();
        Busy = false;
        Status = "게임이 종료되었습니다.";
        _discord?.SetState(DiscordPresenceState.Ready);
    });

    private void OnServerStatusChanged(object? sender, ServerStatusInfo status) => Dispatcher.UIThread.Post(() =>
    {
        ServerStatus = status.RequestSucceeded
            ? status.IsOnline ? $"온라인 · {status.OnlinePlayers ?? 0}/{status.MaxPlayers ?? 0}" : "오프라인"
            : "서버 상태를 확인할 수 없음";
        ServerMotd = status.Motd;
    });

    private void NotifyCommandStates()
    {
        ((AsyncCommand)LoginCommand).NotifyCanExecuteChanged();
        ((AsyncCommand)LaunchCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
    }

    private void OnDeviceCodeReceived(object? sender, DeviceCodeInfo info) => Dispatcher.UIThread.Post(() =>
    {
        DeviceCode = info.UserCode;
        DeviceCodeUrl = info.VerificationUrl;
        RaisePropertyChanged(nameof(DeviceCode));
        RaisePropertyChanged(nameof(DeviceCodeUrl));
        Status = info.Message;
    });

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _currentOperation?.Cancel();
        _statusPolling.StatusChanged -= OnServerStatusChanged;
        if (_auth is AuthService deviceAuth) deviceAuth.DeviceCodeReceived -= OnDeviceCodeReceived;
        await _statusPolling.DisposeAsync();
        DetachGameProcess();
        _statusHttpClient.Dispose();
        _discord?.Dispose();
        _launcher.Dispose();
        _currentOperation?.Dispose();
        _lifetime.Dispose();
    }
}
