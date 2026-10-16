using System.Diagnostics;
using CmlLib.Core.Auth;
using CustomLauncher.Core;
using CustomLauncher.Models;
using CustomLauncher.Shared.Models;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Tests;

public sealed class MainViewModelUpdateTests
{
    [Fact]
    public async Task InitializeShowsTheBannerWhenANewerVersionExists()
    {
        var updates = new FakeUpdateService { Version = "2.1.0" };
        await using var viewModel = await InitializeAsync(updates);
        await WaitUntilAsync(() => viewModel.HasUpdate);

        Assert.True(viewModel.HasUpdate);
        Assert.Equal("2.1.0", viewModel.AvailableUpdateVersion);
        Assert.Equal("새 버전 2.1.0이 있습니다", viewModel.UpdateBannerText);
        Assert.True(viewModel.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task InitializeHidesTheBannerWhenThereIsNoNewerVersion()
    {
        var updates = new FakeUpdateService { Version = null };
        await using var viewModel = await InitializeAsync(updates);
        await updates.Checked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.HasUpdate);
        Assert.False(viewModel.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task InitializeSkipsTheCheckWhenUpdatesAreTurnedOff()
    {
        var updates = new FakeUpdateService { Version = "2.1.0" };
        await using var viewModel = await InitializeAsync(updates, new LauncherSettings { CheckForUpdates = false });

        Assert.Equal(0, updates.CheckCalls);
        Assert.False(viewModel.HasUpdate);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InitializePassesThePrereleaseSettingToTheCheck(bool includePrereleases)
    {
        var updates = new FakeUpdateService();
        await using var viewModel = await InitializeAsync(updates, new LauncherSettings { IncludePrereleases = includePrereleases });
        await updates.Checked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(includePrereleases, updates.LastIncludePrereleases);
    }

    [Fact]
    public async Task UpdateCommandIsDisabledWhileBusy()
    {
        var launcher = new BlockingRepairLauncher();
        var updates = new FakeUpdateService { Version = "2.1.0" };
        await using var viewModel = await InitializeAsync(updates, launcher: launcher);
        await WaitUntilAsync(() => viewModel.HasUpdate);
        Assert.True(viewModel.UpdateCommand.CanExecute(null));

        var repairing = viewModel.RepairFilesAsync();
        Assert.True(viewModel.Busy);
        Assert.False(viewModel.UpdateCommand.CanExecute(null));

        launcher.Gate.SetResult();
        await repairing;
        Assert.True(viewModel.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpdateFailureShowsTheMessageAndFreesTheLauncher()
    {
        var updates = new FakeUpdateService { Version = "2.1.0", ApplyError = new InvalidOperationException("업데이트를 설치하지 못했습니다.") };
        await using var viewModel = await InitializeAsync(updates);
        await WaitUntilAsync(() => viewModel.HasUpdate);

        await viewModel.UpdateAsync();

        Assert.Equal(1, updates.ApplyCalls);
        Assert.Equal("업데이트를 설치하지 못했습니다.", viewModel.Status);
        Assert.False(viewModel.Busy);
    }

    [Fact]
    public async Task UpdateIsRefusedWhileTheGameIsRunning()
    {
        var updates = new FakeUpdateService { Version = "2.1.0" };
        await using var viewModel = await InitializeAsync(updates, launcher: new StartingLauncher());
        await WaitUntilAsync(() => viewModel.HasUpdate);
        await viewModel.LoginAsync();
        await viewModel.LaunchAsync();

        await viewModel.UpdateAsync();

        Assert.Equal(0, updates.ApplyCalls);
        Assert.Equal("게임을 종료한 뒤 업데이트해 주세요.", viewModel.Status);
    }

    private static async Task<MainViewModel> InitializeAsync(
        FakeUpdateService updates, LauncherSettings? settings = null, ILauncherService? launcher = null)
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-update-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        await new AppSettingsManager(paths).SaveAsync(settings ?? new LauncherSettings());
        var viewModel = new MainViewModel(paths, new AppSettingsManager(paths), new DebugLogger(paths),
            new SignedInAuthService(), launcher ?? new BlockingRepairLauncher(), _ => new EmptyContentService(),
            (_, _) => Task.FromResult(ServerStatusInfo.Unavailable("test")), updates);
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 250 && !condition(); attempt++) await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        private int _checkCalls;
        private int _applyCalls;
        public string? Version { get; init; }
        public Exception? ApplyError { get; init; }
        public bool? LastIncludePrereleases { get; private set; }
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CheckCalls => Volatile.Read(ref _checkCalls);
        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public bool IsSupported => true;

        public Task<string?> CheckAsync(bool includePrereleases, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _checkCalls);
            LastIncludePrereleases = includePrereleases;
            Checked.TrySetResult();
            return Task.FromResult(Version);
        }

        public Task DownloadAndApplyAsync(Action<int> progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _applyCalls);
            return ApplyError is null ? Task.CompletedTask : Task.FromException(ApplyError);
        }
    }

    private sealed class EmptyContentService : IContentUpdateService
    {
        public bool HasManagedState(string serverId) => false;

        public Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServerDistribution());

        public Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModuleUpdateResult> RepairAsync(ServerDistribution distribution, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveCacheAsync(ServerDistribution distribution, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ServerDistribution?> TryLoadCacheAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ServerDistribution?>(null);
    }

    private sealed class SignedInAuthService : IAuthService
    {
        public Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MSession?>(new MSession("player", "token", Guid.NewGuid().ToString("N")));

        public Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MSession?>(null);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlockingRepairLauncher : ILauncherService
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ModuleUpdateResult> RepairContentAsync(
            LauncherSettings settings, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            await Gate.Task;
            return new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []);
        }

        public Task<PreparedGameSession> PrepareGameSessionAsync(
            LauncherSettings settings, MSession session, IProgress<LaunchProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose() { }
    }

    private sealed class StartingLauncher : ILauncherService
    {
        public Task<ModuleUpdateResult> RepairContentAsync(
            LauncherSettings settings, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PreparedGameSession> PrepareGameSessionAsync(
            LauncherSettings settings, MSession session, IProgress<LaunchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var process = new Process { StartInfo = new ProcessStartInfo { FileName = "dotnet", ArgumentList = { "--version" } } };
            return Task.FromResult(new PreparedGameSession(process, new ServerDistribution(), "test"));
        }

        public void Dispose() { }
    }
}
