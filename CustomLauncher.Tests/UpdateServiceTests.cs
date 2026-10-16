using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class UpdateServiceTests
{
    private static (AppPaths Paths, DebugLogger Logger) CreateLogger()
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-update-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        return (paths, new DebugLogger(paths));
    }

    [Fact]
    public async Task NotInstalledSkipsCheckWithoutCallingManager()
    {
        var (_, logger) = CreateLogger();
        var manager = new FakeManager { IsInstalled = false, Version = "9.9.9" };
        var service = new UpdateService(logger, _ => manager);

        Assert.False(service.IsSupported);
        Assert.Null(await service.CheckAsync(false, CancellationToken.None));
        Assert.Equal(0, manager.CheckCalls);
    }

    [Fact]
    public async Task NoUpdateReturnsNull()
    {
        var (_, logger) = CreateLogger();
        var service = new UpdateService(logger, _ => new FakeManager { IsInstalled = true });

        Assert.True(service.IsSupported);
        Assert.Null(await service.CheckAsync(false, CancellationToken.None));
    }

    [Fact]
    public async Task AvailableUpdateReturnsVersionAndAppliesIt()
    {
        var (_, logger) = CreateLogger();
        var manager = new FakeManager { IsInstalled = true, Version = "2.1.0" };
        var service = new UpdateService(logger, _ => manager);

        Assert.Equal("2.1.0", await service.CheckAsync(false, CancellationToken.None));
        await service.DownloadAndApplyAsync(_ => { }, CancellationToken.None);

        Assert.Equal(1, manager.DownloadCalls);
        Assert.Equal(1, manager.ApplyCalls);
    }

    [Fact]
    public async Task CheckFailureReturnsNullAndLogsIt()
    {
        var (paths, logger) = CreateLogger();
        var service = new UpdateService(logger, _ => new FakeManager { IsInstalled = true, CheckError = new IOException("offline") });

        Assert.Null(await service.CheckAsync(false, CancellationToken.None));

        var log = await File.ReadAllTextAsync(Path.Combine(paths.LogDir, LauncherConfig.DebugLogFileName));
        Assert.Contains("업데이트 확인에 실패함", log);
        Assert.Contains("offline", log);
    }

    [Fact]
    public async Task DownloadFailureIsLoggedAndRethrownWithShortMessage()
    {
        var (paths, logger) = CreateLogger();
        var manager = new FakeManager { IsInstalled = true, Version = "2.1.0", DownloadError = new IOException("disk") };
        var service = new UpdateService(logger, _ => manager);
        await service.CheckAsync(false, CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAndApplyAsync(_ => { }, CancellationToken.None));

        Assert.Equal("업데이트를 설치하지 못했습니다.", error.Message);
        Assert.Equal(0, manager.ApplyCalls);
        Assert.Contains("업데이트 설치에 실패함",
            await File.ReadAllTextAsync(Path.Combine(paths.LogDir, LauncherConfig.DebugLogFileName)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrereleaseFlagIsPassedToTheFactory(bool includePrereleases)
    {
        var (_, logger) = CreateLogger();
        var requested = new List<bool>();
        var service = new UpdateService(logger, prerelease =>
        {
            requested.Add(prerelease);
            return new FakeManager { IsInstalled = true };
        });

        await service.CheckAsync(includePrereleases, CancellationToken.None);

        Assert.Equal(includePrereleases, requested[^1]);
    }

    [Fact]
    public void DefaultServiceIsUnsupportedOutsideAnInstalledApp()
    {
        var (_, logger) = CreateLogger();

        Assert.False(new UpdateService(logger).IsSupported);
    }

    private sealed class FakeManager : IUpdateManager
    {
        public bool IsInstalled { get; init; }
        public string? Version { get; init; }
        public Exception? CheckError { get; init; }
        public Exception? DownloadError { get; init; }
        public int CheckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public Task<string?> CheckForUpdatesAsync()
        {
            CheckCalls++;
            return CheckError is null ? Task.FromResult(Version) : Task.FromException<string?>(CheckError);
        }

        public Task DownloadUpdatesAsync(Action<int> progress, CancellationToken cancellationToken)
        {
            DownloadCalls++;
            return DownloadError is null ? Task.CompletedTask : Task.FromException(DownloadError);
        }

        public void ApplyUpdatesAndRestart() => ApplyCalls++;
    }
}
