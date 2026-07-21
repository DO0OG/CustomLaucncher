using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class ModLoaderInstallerTests
{
    [Fact]
    public async Task ForgeAlreadyInstalled_DoesNotReinstall()
    {
        var backend = new FakeBackend(["1.20.1-forge-47.3.0"]);
        var installer = new ModLoaderInstaller(backend, Config("forge"));
        Assert.Equal("1.20.1-forge-47.3.0", await installer.EnsureInstalledAsync(null, default));
        Assert.Equal(0, backend.ForgeInstalls);
    }

    [Fact]
    public async Task ForgeInstall_UsesExactInstallerReturnValue()
    {
        var backend = new FakeBackend([]) { ForgeResult = "forge-returned-id" };
        var installer = new ModLoaderInstaller(backend, Config("forge"));
        Assert.Equal("forge-returned-id", await installer.EnsureInstalledAsync(null, default));
        Assert.Equal(1, backend.ForgeInstalls);
    }

    [Fact]
    public async Task FabricAlreadyInstalledAndInstallUseBackendNames()
    {
        var backend = new FakeBackend(["fabric-official-id"]);
        var installer = new ModLoaderInstaller(backend, Config("fabric"));
        Assert.Equal("fabric-official-id", await installer.EnsureInstalledAsync(null, default));
        Assert.Equal(0, backend.FabricInstalls);

        backend = new FakeBackend([]) { FabricResult = "fabric-returned-id" };
        installer = new ModLoaderInstaller(backend, Config("fabric"));
        Assert.Equal("fabric-returned-id", await installer.EnsureInstalledAsync(null, default));
        Assert.Equal(1, backend.FabricInstalls);
    }

    [Fact]
    public async Task NoneReturnsMinecraftVersionWithoutBackendCalls()
    {
        var backend = new FakeBackend([]);
        var installer = new ModLoaderInstaller(backend, Config("none"));
        Assert.Equal("1.20.1", await installer.EnsureInstalledAsync(null, default));
        Assert.Equal(0, backend.VersionScans);
    }

    private static ModLoaderConfiguration Config(string type) => new(type, "1.20.1", "47.3.0", "0.15.7");

    private sealed class FakeBackend(IReadOnlyCollection<string> installed) : IModLoaderBackend
    {
        public int VersionScans { get; private set; }
        public int ForgeInstalls { get; private set; }
        public int FabricInstalls { get; private set; }
        public string ForgeResult { get; init; } = "forge-returned";
        public string FabricResult { get; init; } = "fabric-returned";
        public Task<IReadOnlyCollection<string>> GetInstalledVersionNamesAsync(CancellationToken cancellationToken)
        { VersionScans++; return Task.FromResult(installed); }
        public string GetFabricVersionName(string minecraftVersion, string fabricVersion) => "fabric-official-id";
        public Task<string> InstallForgeAsync(string minecraftVersion, string forgeVersion,
            IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { ForgeInstalls++; return Task.FromResult(ForgeResult); }
        public Task<string> InstallFabricAsync(string minecraftVersion, string fabricVersion,
            IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
        { FabricInstalls++; return Task.FromResult(FabricResult); }
    }
}
