using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests;

public sealed class GameSessionPreparerTests
{
    [Theory]
    [InlineData(ModuleUpdateStatus.Updated)]
    [InlineData(ModuleUpdateStatus.UpToDate)]
    public async Task PrepareAsync_RunsStagesInOrderAndUsesInstallerVersion(ModuleUpdateStatus status)
    {
        var calls = new List<string>();
        var distribution = Distribution();
        var content = new FakeContent(calls, distribution, new ModuleUpdateResult(status, []));
        var loader = new FakeLoader(calls, "installer-returned-version");
        var runtime = new FakeRuntime(calls);

        var result = await new GameSessionPreparer(content, loader, runtime)
            .PrepareAsync(new MLaunchOption());

        Assert.Equal("installer-returned-version", result.TargetVersionName);
        Assert.Equal(distribution, result.Distribution);
        Assert.Equal(["fetch", "update", "cache", "loader", "install:installer-returned-version", "create:installer-returned-version"], calls);
    }

    [Fact]
    public async Task PrepareAsync_UpdateFailureStopsLaunchAndPreservesError()
    {
        var calls = new List<string>();
        var content = new FakeContent(calls, Distribution(),
            new ModuleUpdateResult(ModuleUpdateStatus.Failed, [], "exact update error"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GameSessionPreparer(content, new FakeLoader(calls, "unused"), new FakeRuntime(calls))
                .PrepareAsync(new MLaunchOption()));

        Assert.Contains("exact update error", exception.Message);
        Assert.Equal(["fetch", "update"], calls);
    }

    [Fact]
    public async Task PrepareAsync_CancelledUpdateStopsQuietly()
    {
        var calls = new List<string>();
        var content = new FakeContent(calls, Distribution(),
            new ModuleUpdateResult(ModuleUpdateStatus.Cancelled, []));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GameSessionPreparer(content, new FakeLoader(calls, "unused"), new FakeRuntime(calls))
                .PrepareAsync(new MLaunchOption()));
        Assert.Equal(["fetch", "update"], calls);
    }

    [Fact]
    public async Task PrepareAsync_UsesVerifiedCacheWhenNetworkIsUnavailable()
    {
        var calls = new List<string>();
        var cached = Distribution();
        var content = new FakeContent(calls, cached, new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []))
        {
            FetchException = new HttpRequestException("offline"),
            CachedDistribution = cached,
            HasManagedStateValue = true,
        };

        var result = await new GameSessionPreparer(content, new FakeLoader(calls, "forge-cached"), new FakeRuntime(calls))
            .PrepareAsync(new MLaunchOption());

        Assert.NotNull(result.Warning);
        Assert.Equal(["fetch", "load-cache", "loader", "install:forge-cached", "create:forge-cached"], calls);
    }

    [Fact]
    public async Task PrepareAsync_FirstOfflineRunIsRejected()
    {
        var calls = new List<string>();
        var content = new FakeContent(calls, Distribution(), new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []))
        {
            FetchException = new HttpRequestException("offline"),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GameSessionPreparer(content, new FakeLoader(calls, "unused"), new FakeRuntime(calls))
                .PrepareAsync(new MLaunchOption()));
        Assert.Contains("최초 콘텐츠 동기화", exception.Message);
        Assert.Equal(["fetch", "load-cache"], calls);
    }

    [Fact]
    public async Task PrepareAsync_CallerCancellationNeverFallsBackOffline()
    {
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var content = new FakeContent(calls, Distribution(), new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []))
        {
            FetchException = new OperationCanceledException(cancellation.Token),
            CachedDistribution = Distribution(),
            HasManagedStateValue = true,
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GameSessionPreparer(content, new FakeLoader(calls, "unused"), new FakeRuntime(calls))
                .PrepareAsync(new MLaunchOption(), cancellationToken: cancellation.Token));
        Assert.Equal(["fetch"], calls);
    }

    private static ServerDistribution Distribution() => new()
    {
        ServerId = "test",
        Java = new JavaRequirement { MinVersion = "17", RecommendedVersion = "21" },
    };

    private sealed class FakeContent(List<string> calls, ServerDistribution distribution, ModuleUpdateResult update)
        : IContentUpdateService
    {
        public Exception? FetchException { get; init; }
        public ServerDistribution? CachedDistribution { get; init; }
        public bool HasManagedStateValue { get; init; }
        public bool HasManagedState => HasManagedStateValue;
        public Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken)
        {
            calls.Add("fetch");
            return FetchException is null ? Task.FromResult(distribution) : Task.FromException<ServerDistribution>(FetchException);
        }
        public Task<ModuleUpdateResult> UpdateAsync(ServerDistribution value, CancellationToken cancellationToken)
        { calls.Add("update"); return Task.FromResult(update); }
        public Task SaveCacheAsync(ServerDistribution value, CancellationToken cancellationToken)
        { calls.Add("cache"); return Task.CompletedTask; }
        public Task<ServerDistribution?> TryLoadCacheAsync(CancellationToken cancellationToken)
        { calls.Add("load-cache"); return Task.FromResult(CachedDistribution); }
    }

    private sealed class FakeLoader(List<string> calls, string version) : IModLoaderInstaller
    {
        public Task<string> EnsureInstalledAsync(IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { calls.Add("loader"); return Task.FromResult(version); }
    }

    private sealed class FakeRuntime(List<string> calls) : IGameRuntime
    {
        public Task InstallAsync(string versionName, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { calls.Add("install:" + versionName); return Task.CompletedTask; }
        public Task<Process> CreateProcessAsync(string versionName, MLaunchOption option, CancellationToken cancellationToken)
        { calls.Add("create:" + versionName); return Task.FromResult(new Process()); }
    }
}
