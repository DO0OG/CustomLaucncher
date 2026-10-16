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
        Assert.Equal(distribution.Servers[0].Id, result.Distribution.ServerId);
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
    public async Task PrepareAsync_OfflineStateIsJudgedPerSelectedServer()
    {
        var calls = new List<string>();
        var cached = TwoServers();
        var content = new FakeContent(calls, cached, new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []))
        {
            FetchException = new HttpRequestException("offline"),
            CachedDistribution = cached,
            HasManagedStateValue = true,
            ManagedServers = new HashSet<string> { "a" },
        };
        var selected = "a";
        var preparer = new GameSessionPreparer(content, new FakeLoader(calls, "v"), new FakeRuntime(calls),
            selectedServerId: () => selected);

        var result = await preparer.PrepareAsync(new MLaunchOption());
        Assert.Equal("a", result.Distribution.ServerId);

        selected = "b";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => preparer.PrepareAsync(new MLaunchOption()));
        Assert.Contains("검증된 로컬 콘텐츠 상태가 없어", exception.Message);
    }

    [Fact]
    public async Task PrepareAsync_UsesSelectedServerFolderAsGameDirectory()
    {
        var calls = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), "launcher-prepare-" + Guid.NewGuid().ToString("N"));
        try
        {
            var content = new FakeContent(calls, TwoServers(), new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []));
            var option = new MLaunchOption();

            await new GameSessionPreparer(content, new FakeLoader(calls, "v"), new FakeRuntime(calls),
                    gameDirectory: root, selectedServerId: () => "b")
                .PrepareAsync(option);

            var serverDirectory = Path.Combine(Path.GetFullPath(root), "instances", "b");
            Assert.True(Directory.Exists(serverDirectory));
            Assert.Equal(serverDirectory, option.Path!.BasePath);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), "libraries"), option.Path.Library);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
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

    private static ServerDistribution TwoServers() => new()
    {
        Servers =
        [
            new ServerEntry { Id = "a", Name = "A", Address = "a.example.com", MinecraftVersion = "1.20.1", Java = new JavaRequirement { MinVersion = "17" } },
            new ServerEntry { Id = "b", Name = "B", Address = "b.example.com", MinecraftVersion = "1.20.1", Java = new JavaRequirement { MinVersion = "17" } },
        ],
    };

    private static ServerDistribution Distribution() => new()
    {
        Servers =
        [
            new ServerEntry
            {
                Id = "test",
                Name = "Test",
                Address = "play.example.com",
                MinecraftVersion = "1.20.1",
                Java = new JavaRequirement { MinVersion = "17", RecommendedVersion = "21" },
            },
        ],
    };

    private sealed class FakeContent(List<string> calls, ServerDistribution distribution, ModuleUpdateResult update)
        : IContentUpdateService
    {
        public Exception? FetchException { get; init; }
        public ServerDistribution? CachedDistribution { get; init; }
        public bool HasManagedStateValue { get; init; }
        public IReadOnlySet<string>? ManagedServers { get; init; }
        public bool HasManagedState(string serverId) =>
            HasManagedStateValue && (ManagedServers is null || ManagedServers.Contains(serverId));
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
        public Task<string> EnsureInstalledAsync(ModLoaderConfiguration configuration, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
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
