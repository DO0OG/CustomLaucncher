using CmlLib.Core;
using CmlLib.Core.ModLoaders.QuiltMC;
using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class ModLoaderInstallerTests
{
    [Fact]
    public async Task ForgeAlreadyInstalled_DoesNotReinstall()
    {
        var backend = new FakeBackend(["1.20.1-forge-47.3.0"]);
        var installer = new ModLoaderInstaller(backend);
        Assert.Equal("1.20.1-forge-47.3.0", await installer.EnsureInstalledAsync(Config("forge"), null, default));
        Assert.Equal(0, backend.ForgeInstalls);
    }

    [Fact]
    public async Task ForgeInstall_UsesExactInstallerReturnValue()
    {
        var backend = new FakeBackend([]) { ForgeResult = "forge-returned-id" };
        var installer = new ModLoaderInstaller(backend);
        Assert.Equal("forge-returned-id", await installer.EnsureInstalledAsync(Config("forge"), null, default));
        Assert.Equal(1, backend.ForgeInstalls);
    }

    [Fact]
    public async Task FabricAlreadyInstalledAndInstallUseBackendNames()
    {
        var backend = new FakeBackend(["fabric-official-id"]);
        var installer = new ModLoaderInstaller(backend);
        Assert.Equal("fabric-official-id", await installer.EnsureInstalledAsync(Config("fabric"), null, default));
        Assert.Equal(0, backend.FabricInstalls);

        backend = new FakeBackend([]) { FabricResult = "fabric-returned-id" };
        installer = new ModLoaderInstaller(backend);
        Assert.Equal("fabric-returned-id", await installer.EnsureInstalledAsync(Config("fabric"), null, default));
        Assert.Equal(1, backend.FabricInstalls);
    }

    [Fact]
    public async Task QuiltAlreadyInstalledAndInstallUseBackendNames()
    {
        var backend = new FakeBackend(["quilt-official-id"]);
        var installer = new ModLoaderInstaller(backend);
        Assert.Equal("quilt-official-id", await installer.EnsureInstalledAsync(Config("quilt"), null, default));
        Assert.Equal(0, backend.QuiltInstalls);

        backend = new FakeBackend([]) { QuiltResult = "quilt-returned-id" };
        installer = new ModLoaderInstaller(backend);
        Assert.Equal("quilt-returned-id", await installer.EnsureInstalledAsync(Config("quilt"), null, default));
        Assert.Equal(1, backend.QuiltInstalls);
    }

    [Fact]
    public async Task NeoForgeAlreadyInstalledAndInstallUseBackendNames()
    {
        var backend = new FakeBackend(["neoforge-47.3.0"]);
        var installer = new ModLoaderInstaller(backend);
        Assert.Equal("neoforge-47.3.0", await installer.EnsureInstalledAsync(Config("neoforge"), null, default));
        Assert.Equal(0, backend.NeoForgeInstalls);

        backend = new FakeBackend([]) { NeoForgeResult = "neoforge-returned-id" };
        installer = new ModLoaderInstaller(backend);
        Assert.Equal("neoforge-returned-id", await installer.EnsureInstalledAsync(Config("neoforge"), null, default));
        Assert.Equal(1, backend.NeoForgeInstalls);
    }

    [Fact]
    public async Task NeoForgeDoesNotReuseVersionThatOnlyPrefixMatches()
    {
        var backend = new FakeBackend(["neoforge-21.1.100"]);
        var installer = new ModLoaderInstaller(backend);
        var config = new ModLoaderConfiguration("neoforge", "1.21.1", "21.1.10", "");
        Assert.Equal("neoforge-returned", await installer.EnsureInstalledAsync(config, null, default));
        Assert.Equal(1, backend.NeoForgeInstalls);
    }

    [Fact]
    public async Task ForgeIgnoresInstalledNeoForgeNames()
    {
        var backend = new FakeBackend(["1.20.1-neoforge-47.3.0"]);
        var installer = new ModLoaderInstaller(backend);
        Assert.Equal("forge-returned", await installer.EnsureInstalledAsync(Config("forge"), null, default));
        Assert.Equal(1, backend.ForgeInstalls);
    }

    [Fact]
    public async Task UnknownTypeThrows()
    {
        var installer = new ModLoaderInstaller(new FakeBackend([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.EnsureInstalledAsync(Config("unknown"), null, default));
    }

    [Fact]
    public void CmlBackendQuiltVersionName_MatchesQuiltInstaller()
    {
        using var http = new HttpClient();
        var path = new MinecraftPath(Path.Combine(Path.GetTempPath(), "unused-quilt-name-check"));
        var backend = new CmlModLoaderBackend(new MinecraftLauncher(path), path, http);
        Assert.Equal(QuiltInstaller.GetVersionName("1.20.1", "0.26.0"), backend.GetQuiltVersionName("1.20.1", "0.26.0"));
    }

    [Fact]
    public async Task NoneReturnsMinecraftVersionWithoutBackendCalls()
    {
        var backend = new FakeBackend([]);
        var installer = new ModLoaderInstaller(backend);
        Assert.Equal("1.20.1", await installer.EnsureInstalledAsync(Config("none"), null, default));
        Assert.Equal(0, backend.VersionScans);
    }

    private static ModLoaderConfiguration Config(string type) => new(type, "1.20.1", "47.3.0", "0.15.7");

    private sealed class FakeBackend(IReadOnlyCollection<string> installed) : IModLoaderBackend
    {
        public int VersionScans { get; private set; }
        public int ForgeInstalls { get; private set; }
        public int FabricInstalls { get; private set; }
        public int QuiltInstalls { get; private set; }
        public int NeoForgeInstalls { get; private set; }
        public string QuiltResult { get; init; } = "quilt-returned";
        public string NeoForgeResult { get; init; } = "neoforge-returned";
        public string ForgeResult { get; init; } = "forge-returned";
        public string FabricResult { get; init; } = "fabric-returned";
        public Task<IReadOnlyCollection<string>> GetInstalledVersionNamesAsync(CancellationToken cancellationToken)
        { VersionScans++; return Task.FromResult(installed); }
        public string GetFabricVersionName(string minecraftVersion, string fabricVersion) => "fabric-official-id";
        public string GetQuiltVersionName(string minecraftVersion, string quiltVersion) => "quilt-official-id";
        public Task<string> InstallQuiltAsync(string minecraftVersion, string quiltVersion,
            IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { QuiltInstalls++; return Task.FromResult(QuiltResult); }
        public Task<string> InstallNeoForgeAsync(string minecraftVersion, string neoForgeVersion,
            IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { NeoForgeInstalls++; return Task.FromResult(NeoForgeResult); }
        public Task<string> InstallForgeAsync(string minecraftVersion, string forgeVersion,
            IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { ForgeInstalls++; return Task.FromResult(ForgeResult); }
        public Task<string> InstallFabricAsync(string minecraftVersion, string fabricVersion,
            IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { FabricInstalls++; return Task.FromResult(FabricResult); }
    }
}
