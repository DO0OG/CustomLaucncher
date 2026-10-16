using Velopack;
using Velopack.Sources;

namespace CustomLauncher.Core;

public interface IUpdateService
{
    bool IsSupported { get; }
    Task<string?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken);
    Task DownloadAndApplyAsync(Action<int> progress, CancellationToken cancellationToken);
}

/// <summary>Velopack UpdateManager를 테스트에서 바꿔 끼울 수 있게 얇게 감싼 래퍼. UpdateInfo는 구현 안에 보관한다.</summary>
public interface IUpdateManager
{
    bool IsInstalled { get; }
    Task<string?> CheckForUpdatesAsync();
    Task DownloadUpdatesAsync(Action<int> progress, CancellationToken cancellationToken);
    void ApplyUpdatesAndRestart();
}

public sealed class UpdateService : IUpdateService
{
    private static readonly string RepositoryUrl = "https://github.com/DO0OG/CustomLaucncher";

    private readonly DebugLogger _logger;
    private readonly Func<bool, IUpdateManager> _managerFactory;
    private IUpdateManager? _manager;
    private bool? _isSupported;

    public UpdateService(DebugLogger logger) : this(logger, prerelease => new VelopackUpdateManager(prerelease)) { }

    public UpdateService(DebugLogger logger, Func<bool, IUpdateManager> managerFactory)
    {
        _logger = logger;
        _managerFactory = managerFactory;
    }

    public bool IsSupported => _isSupported ??= Probe();

    public async Task<string?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            await _logger.WriteAsync(LauncherLogLevel.Info, "설치되지 않은 실행이라 업데이트 확인을 건너뜀");
            return null;
        }

        try
        {
            var manager = _managerFactory(includePrereleases);
            var version = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            _manager = version is null ? null : manager;
            return version;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _manager = null;
            await _logger.WriteAsync(LauncherLogLevel.Warn, "업데이트 확인에 실패함", exception);
            return null;
        }
    }

    public async Task DownloadAndApplyAsync(Action<int> progress, CancellationToken cancellationToken)
    {
        try
        {
            var manager = _manager ?? throw new InvalidOperationException("확인된 업데이트가 없습니다.");
            await manager.DownloadUpdatesAsync(progress, cancellationToken);
            manager.ApplyUpdatesAndRestart();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await _logger.WriteAsync(LauncherLogLevel.Error, "업데이트 설치에 실패함", exception);
            throw new InvalidOperationException("업데이트를 설치하지 못했습니다.", exception);
        }
    }

    private bool Probe()
    {
        try
        {
            return _managerFactory(false).IsInstalled;
        }
        catch (Exception exception)
        {
            // 미설치 환경에서 매니저 생성이 실패하면 지원하지 않는 것으로 본다.
            _ = _logger.WriteAsync(LauncherLogLevel.Debug, "업데이트 매니저를 만들지 못해 업데이트를 지원하지 않음", exception);
            return false;
        }
    }

    private sealed class VelopackUpdateManager : IUpdateManager
    {
        private readonly UpdateManager _inner;
        private UpdateInfo? _info;

        public VelopackUpdateManager(bool prerelease) =>
            _inner = new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease, null), null, null);

        public bool IsInstalled => _inner.IsInstalled;

        public async Task<string?> CheckForUpdatesAsync()
        {
            _info = await _inner.CheckForUpdatesAsync();
            return _info?.TargetFullRelease.Version.ToString();
        }

        public Task DownloadUpdatesAsync(Action<int> progress, CancellationToken cancellationToken) =>
            _inner.DownloadUpdatesAsync(_info ?? throw new InvalidOperationException("확인된 업데이트가 없습니다."),
                progress, cancellationToken);

        public void ApplyUpdatesAndRestart() =>
            _inner.ApplyUpdatesAndRestart(_info?.TargetFullRelease);
    }
}
