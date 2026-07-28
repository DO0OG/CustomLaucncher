using CmlLib.Core.Auth;
using CustomLauncher.Core;
using CustomLauncher.Models;
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
