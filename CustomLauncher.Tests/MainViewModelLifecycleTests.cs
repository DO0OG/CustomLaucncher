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
