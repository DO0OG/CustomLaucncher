using CustomLauncher.Core;
using CustomLauncher.Models;

namespace CustomLauncher.Tests.Core;

public sealed class AppSettingsManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "customlauncher-tests-" + Guid.NewGuid());

    [Fact]
    public async Task MigratesEachLegacyFieldIndependently()
    {
        var config = Path.Combine(_root, ".config", "CustomLauncher");
        Directory.CreateDirectory(config);
        await File.WriteAllLinesAsync(Path.Combine(config, LauncherConfig.LegacySettingsFileName),
            ["1600x900", "/games/custom", "broken"]);
        var paths = new AppPaths(PlatformKind.Linux, _root, new Dictionary<string, string?>());
        var settings = await new AppSettingsManager(paths).LoadAsync();
        Assert.Equal(1600, settings.Resolution.Width);
        Assert.Equal("/games/custom", settings.InstallPath);
        Assert.True(settings.Java.MaxRamMb >= settings.Java.MinRamMb);
        Assert.True(File.Exists(paths.SettingsFile));
    }

    [Fact]
    public async Task JsonDoesNotContainSecrets()
    {
        var paths = new AppPaths(PlatformKind.Linux, _root, new Dictionary<string, string?>());
        var manager = new AppSettingsManager(paths);
        await manager.SaveAsync(await manager.LoadAsync());
        var json = await File.ReadAllTextAsync(paths.SettingsFile);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectedAccountIdRoundTrips()
    {
        var paths = new AppPaths(PlatformKind.Linux, _root, new Dictionary<string, string?>());
        var manager = new AppSettingsManager(paths);
        await manager.SaveAsync(new LauncherSettings { SelectedAccountId = "fake-home-id-1" });
        Assert.Equal("fake-home-id-1", (await manager.LoadAsync()).SelectedAccountId);
    }

    [Fact]
    public async Task SchemaThreeSettingsWithoutAnAccountIdLoadAsNull()
    {
        var paths = new AppPaths(PlatformKind.Linux, _root, new Dictionary<string, string?>());
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.SettingsFile, "{\"schemaVersion\":3,\"selectedServerId\":\"alpha\"}");
        var loaded = await new AppSettingsManager(paths).LoadAsync();
        Assert.Null(loaded.SelectedAccountId);
        Assert.Equal("alpha", loaded.SelectedServerId);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
