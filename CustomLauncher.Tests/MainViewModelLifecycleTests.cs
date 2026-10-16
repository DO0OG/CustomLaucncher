using System.Diagnostics;
using CmlLib.Core.Auth;
using CustomLauncher.Core;
using CustomLauncher.Models;
using CustomLauncher.Shared.Models;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Tests;

public sealed class MainViewModelLifecycleTests
{
    [Fact]
    public async Task CancellingLoginDoesNotCancelLaterOperations()
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-lifecycle-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        var auth = new BlockingAuthService();
        await using var viewModel = new MainViewModel(
            paths, new AppSettingsManager(paths), new DebugLogger(paths), auth, new StubLauncherService());

        var firstLogin = viewModel.LoginAsync();
        await auth.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelCurrentOperation();
        await firstLogin;

        await viewModel.LoginAsync();
        await viewModel.SaveSettingsAsync();

        Assert.Equal(2, auth.AuthenticateCalls);
        Assert.True(File.Exists(paths.SettingsFile));
        Assert.False(viewModel.Busy);
        Assert.Contains("로그인", viewModel.Status);
    }

    [Fact]
    public async Task LogoutClearsSessionAndSwapsBackToTheSignInAction()
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-logout-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        var auth = new SignedInAuthService();
        await using var viewModel = new MainViewModel(
            paths, new AppSettingsManager(paths), new DebugLogger(paths), auth, new StubLauncherService());

        await viewModel.LoginAsync();
        Assert.True(viewModel.IsAuthenticated);

        await viewModel.LogoutAsync();

        Assert.Equal(1, auth.SignOutCalls);
        Assert.False(viewModel.IsAuthenticated);
        Assert.True(viewModel.IsSignedOut);
        Assert.Equal("로그인하지 않음", viewModel.Account);
        Assert.False(viewModel.Busy);
    }

    [Fact]
    public async Task LogoutDropsTheSessionEvenWhenClearingCredentialsFails()
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-logout-fail-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        var auth = new SignedInAuthService { SignOutError = new InvalidOperationException("cache locked") };
        await using var viewModel = new MainViewModel(
            paths, new AppSettingsManager(paths), new DebugLogger(paths), auth, new StubLauncherService());

        await viewModel.LoginAsync();
        await viewModel.LogoutAsync();

        // Staying "signed in" after a failed sign-out would show an account the launcher can no
        // longer vouch for, so the session is dropped and the failure is surfaced instead.
        Assert.True(viewModel.IsSignedOut);
        Assert.Contains("오류", viewModel.Status);
    }

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    public async Task LauncherClosesOnlyAfterTheGameStartedAndOnlyWhenAsked(bool closeEnabled, bool startSucceeds, int expectedRequests)
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-close-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        var launcher = new StartingLauncherService { Fail = !startSucceeds };
        await using var viewModel = new MainViewModel(
            paths, new AppSettingsManager(paths), new DebugLogger(paths), new SignedInAuthService(), launcher);
        viewModel.Settings.CloseLauncherOnGameStart = closeEnabled;
        var requests = 0;
        viewModel.CloseRequested += (_, _) => requests++;

        await viewModel.LoginAsync();
        await viewModel.LaunchAsync();

        Assert.Equal(expectedRequests, requests);
    }

    [Fact]
    public void AbnormalExitMessageNamesTheNewestCrashReportAndLog()
    {
        var gameDir = Path.Combine(Path.GetTempPath(), $"launcher-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(gameDir, "crash-reports"));
        Directory.CreateDirectory(Path.Combine(gameDir, "logs"));
        var older = Path.Combine(gameDir, "crash-reports", "crash-old.txt");
        var newer = Path.Combine(gameDir, "crash-reports", "crash-new.txt");
        File.WriteAllText(older, "a");
        File.WriteAllText(newer, "b");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        var log = Path.Combine(gameDir, "logs", "latest.log");
        File.WriteAllText(log, "c");

        var withFiles = MainViewModel.DescribeAbnormalExit(1, gameDir);
        var bare = MainViewModel.DescribeAbnormalExit(1, Path.Combine(gameDir, "missing"));

        Assert.Contains("(코드 1)", withFiles);
        Assert.Contains(newer, withFiles);
        Assert.DoesNotContain(older, withFiles);
        Assert.Contains(log, withFiles);
        Assert.Equal("게임이 비정상 종료되었습니다(코드 1).", bare);
    }

    [Fact]
    public void ProgressStatusShowsFileAndMegabyteCountsWhenKnown()
    {
        var both = new LaunchProgress("게임 파일 설치", 0.5, "a.jar")
        {
            FilesDone = 3,
            FilesTotal = 10,
            BytesDone = 5 * 1024 * 1024,
            BytesTotal = 20 * 1024 * 1024,
        };
        var filesOnly = new LaunchProgress("게임 파일 설치", 0.5) { FilesDone = 3, FilesTotal = 10 };

        Assert.Equal("게임 파일 설치 3/10 파일, 5.0/20.0 MB", both.ToStatusText());
        Assert.Equal("게임 파일 설치 3/10 파일", filesOnly.ToStatusText());
        Assert.Equal("Java: x", new LaunchProgress("Java", 0, "x").ToStatusText());
        Assert.Equal("Java", new LaunchProgress("Java", 0).ToStatusText());
    }

    [Fact]
    public async Task InitializeShowsCachedServersAndRestoresTheSavedSelection()
    {
        var (paths, content) = await PrepareServerTestAsync(selectedId: "beta");
        await using var viewModel = CreateServerViewModel(paths, content);

        await viewModel.InitializeAsync();

        Assert.Equal(["alpha", "beta"], viewModel.Servers.Select(server => server.Id));
        Assert.True(viewModel.HasMultipleServers);
        Assert.Equal("beta", viewModel.SelectedServer?.Id);
        Assert.Equal("Beta", viewModel.ServerName);
        Assert.Equal("beta.example.com:25570", viewModel.ServerEndpoint);
    }

    [Fact]
    public async Task InitializeFallsBackToTheFirstServerWhenTheSavedIdIsGone()
    {
        var (paths, content) = await PrepareServerTestAsync(selectedId: "removed");
        await using var viewModel = CreateServerViewModel(paths, content);

        await viewModel.InitializeAsync();

        Assert.Equal("alpha", viewModel.SelectedServer?.Id);
    }

    [Fact]
    public async Task SwitchingServersSavesTheChoiceWithoutFetchingTheManifestAgain()
    {
        var (paths, content) = await PrepareServerTestAsync(selectedId: "alpha");
        await using var viewModel = CreateServerViewModel(paths, content);
        await viewModel.InitializeAsync();
        Assert.Equal(1, content.FetchCalls);

        viewModel.SelectedServer = viewModel.Servers[1];

        Assert.Equal("beta", viewModel.Settings.SelectedServerId);
        Assert.Equal("Beta", viewModel.ServerName);
        for (var attempt = 0; attempt < 100 && (await new AppSettingsManager(paths).LoadAsync()).SelectedServerId != "beta"; attempt++)
            await Task.Delay(20);
        Assert.Equal("beta", (await new AppSettingsManager(paths).LoadAsync()).SelectedServerId);
        Assert.Equal(1, content.FetchCalls);
    }

    [Fact]
    public async Task SwitchingServersPollsTheNewServersStatusUrl()
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-servers-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        await new AppSettingsManager(paths).SaveAsync(new LauncherSettings { SelectedServerId = "alpha" });
        var second = Server("beta");
        second.StatusUrl = "https://status.example/b";
        var list = Distribution(Server("alpha"), second);
        var content = new FakeContentService { Cached = list, Fetched = list };
        var urls = new List<string?>();
        await using var viewModel = CreateServerViewModel(paths, content, urls);
        await viewModel.InitializeAsync();

        viewModel.SelectedServer = viewModel.Servers[1];

        for (var attempt = 0; attempt < 250 && !HasUrl(urls, "https://status.example/b"); attempt++)
            await Task.Delay(20);
        Assert.True(HasUrl(urls, "https://status.example/b"));
    }

    private static bool HasUrl(List<string?> urls, string url)
    {
        lock (urls) return urls.Contains(url);
    }

    [Fact]
    public async Task InitializeKeepsTheCachedListWhenTheStartupFetchFails()
    {
        var (paths, content) = await PrepareServerTestAsync(selectedId: "alpha");
        content.FetchError = new HttpRequestException("offline");
        await using var viewModel = CreateServerViewModel(paths, content);

        await viewModel.InitializeAsync();

        Assert.Equal(1, content.FetchCalls);
        Assert.Equal(2, viewModel.Servers.Count);
        Assert.Equal("alpha", viewModel.SelectedServer?.Id);
    }

    [Fact]
    public async Task StartupFetchReplacesTheListAndKeepsTheSelectedId()
    {
        var (paths, content) = await PrepareServerTestAsync(selectedId: "beta");
        content.Fetched = Distribution(Server("alpha"), Server("beta"), Server("gamma"));
        await using var viewModel = CreateServerViewModel(paths, content);

        await viewModel.InitializeAsync();

        Assert.Equal(3, viewModel.Servers.Count);
        Assert.Equal("beta", viewModel.SelectedServer?.Id);
        Assert.Equal(0, content.SaveCacheCalls);
    }

    private static async Task<(AppPaths Paths, FakeContentService Content)> PrepareServerTestAsync(string selectedId)
    {
        var home = Path.Combine(Path.GetTempPath(), $"launcher-servers-{Guid.NewGuid():N}");
        var paths = new AppPaths(PlatformKind.Linux, home, new Dictionary<string, string?>());
        await new AppSettingsManager(paths).SaveAsync(new LauncherSettings { SelectedServerId = selectedId });
        var list = Distribution(Server("alpha"), Server("beta"));
        return (paths, new FakeContentService { Cached = list, Fetched = list });
    }

    private static MainViewModel CreateServerViewModel(
        AppPaths paths, FakeContentService content, List<string?>? statusUrls = null) =>
        new(paths, new AppSettingsManager(paths), new DebugLogger(paths), new SignedInAuthService(),
            new StubLauncherService(), _ => content,
            (url, _) =>
            {
                if (statusUrls is not null) lock (statusUrls) statusUrls.Add(url);
                return Task.FromResult(ServerStatusInfo.Unavailable("test"));
            });

    private static ServerDistribution Distribution(params ServerEntry[] servers) => new() { Servers = [.. servers] };

    private static ServerEntry Server(string id) => new()
    {
        Id = id,
        Name = char.ToUpperInvariant(id[0]) + id[1..],
        Address = $"{id}.example.com",
        Port = id == "beta" ? 25570 : 25565,
        MinecraftVersion = "1.20.1",
    };

    private sealed class FakeContentService : IContentUpdateService
    {
        public ServerDistribution? Cached { get; init; }
        public ServerDistribution Fetched { get; set; } = new();
        public Exception? FetchError { get; set; }
        public int FetchCalls { get; private set; }
        public int SaveCacheCalls { get; private set; }
        public bool HasManagedState(string serverId) => false;

        public Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken)
        {
            FetchCalls++;
            return FetchError is null ? Task.FromResult(Fetched) : Task.FromException<ServerDistribution>(FetchError);
        }

        public Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveCacheAsync(ServerDistribution distribution, CancellationToken cancellationToken)
        {
            SaveCacheCalls++;
            return Task.CompletedTask;
        }

        public Task<ServerDistribution?> TryLoadCacheAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Cached);
    }

    private sealed class StartingLauncherService : ILauncherService
    {
        public bool Fail { get; init; }

        public Task<PreparedGameSession> PrepareGameSessionAsync(
            LauncherSettings settings,
            MSession session,
            IProgress<LaunchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Fail) return Task.FromException<PreparedGameSession>(new InvalidOperationException("prepare failed"));
            var process = new Process { StartInfo = new ProcessStartInfo { FileName = "dotnet", ArgumentList = { "--version" } } };
            return Task.FromResult(new PreparedGameSession(process, new ServerDistribution(), "test"));
        }

        public void Dispose() { }
    }

    private sealed class SignedInAuthService : IAuthService
    {
        public int SignOutCalls { get; private set; }
        public Exception? SignOutError { get; init; }

        public Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MSession?>(new MSession("player", "token", Guid.NewGuid().ToString("N")));

        public Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MSession?>(null);

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignOutCalls++;
            return SignOutError is null ? Task.CompletedTask : Task.FromException(SignOutError);
        }
    }

    private sealed class BlockingAuthService : IAuthService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int AuthenticateCalls { get; private set; }

        public async Task<MSession?> AuthenticateAsync(CancellationToken cancellationToken = default)
        {
            AuthenticateCalls++;
            if (AuthenticateCalls == 1)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return null;
        }

        public Task<MSession?> TryRestoreAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MSession?>(null);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubLauncherService : ILauncherService
    {
        public Task<PreparedGameSession> PrepareGameSessionAsync(
            LauncherSettings settings,
            MSession session,
            IProgress<LaunchProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose() { }
    }
}
