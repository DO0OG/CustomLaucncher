using System.Windows.Input;
using Avalonia.Threading;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using CustomLauncher.Models;

namespace CustomLauncher.ViewModels;

public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly AppSettingsManager _settingsManager;
    private readonly DebugLogger _logger;
    private readonly IAuthService _auth = new AuthService();
    private readonly LauncherService _launcher = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _statusHttpClient;
    private readonly ServerStatusPollingService _statusPolling;
    private IDiscordPresenceService? _discord;
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
    {
        _settingsManager = settingsManager;
        _logger = logger;
        _statusHttpClient = new HttpClient();
        var statusChecker = new ServerStatusChecker(_statusHttpClient);
        _statusPolling = new ServerStatusPollingService(statusChecker.CheckAsync, () => WindowActive);
        _statusPolling.StatusChanged += (_, status) => Dispatcher.UIThread.Post(() =>
        {
            ServerStatus = status.RequestSucceeded
                ? status.IsOnline ? $"온라인 · {status.OnlinePlayers ?? 0}/{status.MaxPlayers ?? 0}" : "오프라인"
                : "서버 상태를 확인할 수 없음";
            ServerMotd = status.Motd;
        });
        LoginCommand = new AsyncCommand(LoginAsync, () => !Busy);
        LaunchCommand = new AsyncCommand(LaunchAsync, () => !Busy && _session is not null);
        CancelCommand = new RelayCommand(() => _lifetime.Cancel(), () => Busy);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? SettingsRequested;
    public ICommand LoginCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public LauncherSettings Settings => _settings;
    public bool WindowActive { get => _windowActive; set => SetProperty(ref _windowActive, value); }
    public string ServerStatus { get => _serverStatus; private set => SetProperty(ref _serverStatus, value); }
    public string ServerMotd { get => _serverMotd; private set => SetProperty(ref _serverMotd, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Account { get => _account; private set => SetProperty(ref _account, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (!SetProperty(ref _busy, value)) return;
            ((AsyncCommand)LoginCommand).NotifyCanExecuteChanged();
            ((AsyncCommand)LaunchCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        }
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsManager.LoadAsync(_lifetime.Token);
        _session = await _auth.TryRestoreAsync(_lifetime.Token);
        _discord = new DiscordPresenceService(LauncherConfig.DiscordClientId, LauncherConfig.EnableDiscordRpc,
            _settings.DiscordRpcEnabled, () => new DiscordRpcClientAdapter(),
            (message, exception) => _ = _logger.WriteAsync(LauncherLogLevel.Warn, message, exception));
        _discord.Init();
        _discord.SetState(_session is null ? DiscordPresenceState.LauncherOpen : DiscordPresenceState.Ready);
        _statusPolling.Start();
        Account = _session?.Username ?? "로그인하지 않음";
        Status = _session is null ? "로그인이 필요합니다." : "게임을 시작할 수 있습니다.";
        ((AsyncCommand)LaunchCommand).NotifyCanExecuteChanged();
    }

    public async Task SaveSettingsAsync()
    {
        await _settingsManager.SaveAsync(_settings, _lifetime.Token);
        _discord?.SetEnabled(_settings.DiscordRpcEnabled);
        RaisePropertyChanged(nameof(Settings));
    }

    private async Task LoginAsync()
    {
        Busy = true;
        Status = "Microsoft 계정 로그인 중...";
        try
        {
            _session = await _auth.AuthenticateAsync(_lifetime.Token);
            Account = _session?.Username ?? "로그인 실패";
            Status = _session is null ? "로그인하지 못했습니다." : "로그인했습니다.";
            if (_session is not null) _discord?.SetState(DiscordPresenceState.Ready);
            ((AsyncCommand)LaunchCommand).NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            Status = "로그인 중 오류가 발생했습니다.";
            await _logger.WriteAsync(LauncherLogLevel.Error, "Authentication failed", ex);
        }
        finally { Busy = false; }
    }

    private async Task LaunchAsync()
    {
        if (_session is null) return;
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
            var prepared = await _launcher.PrepareGameSessionAsync(_settings, _session, progress, _lifetime.Token);
            var processWrapper = new ProcessWrapper(prepared.Process);
            processWrapper.OutputReceived += (_, line) =>
                _ = _logger.WriteAsync(LauncherLogLevel.Debug, $"[GAME] {line}");
            processWrapper.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                prepared.Process.Dispose();
                Busy = false;
                Status = "게임이 종료되었습니다.";
                _discord?.SetState(DiscordPresenceState.Ready);
            });
            processWrapper.StartWithEvents();
            _discord?.SetState(DiscordPresenceState.Playing);
            Status = prepared.Warning ?? "게임 실행 중";
        }
        catch (Exception ex)
        {
            Busy = false;
            Status = "게임을 시작하지 못했습니다.";
            await _logger.WriteAsync(LauncherLogLevel.Error, "Game launch failed", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _statusPolling.DisposeAsync();
        _statusHttpClient.Dispose();
        _discord?.Dispose();
        _launcher.Dispose();
        _lifetime.Dispose();
        await Task.CompletedTask;
    }
}
