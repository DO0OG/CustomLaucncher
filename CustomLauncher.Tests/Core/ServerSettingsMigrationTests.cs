using CustomLauncher.Core;
using CustomLauncher.Models;

namespace CustomLauncher.Tests.Core;

public sealed class ServerSettingsMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "customlauncher-settings-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public ServerSettingsMigrationTests()
    {
        _paths = new AppPaths(PlatformKind.Linux, _root, new Dictionary<string, string?>());
    }

    [Fact]
    public async Task SchemaTwoDisabledModulesMoveToDefaultServer()
    {
        await WriteSettingsAsync("""{ "schemaVersion": 2, "disabledOptionalModuleIds": ["a", "b"] }""");

        var settings = await new AppSettingsManager(_paths).LoadAsync();

        Assert.Equal(["a", "b"], settings.DisabledOptionalModuleIdsByServer["default"]);
        Assert.Null(settings.DisabledOptionalModuleIds);

        await new AppSettingsManager(_paths).SaveAsync(settings);
        var json = await File.ReadAllTextAsync(_paths.SettingsFile);
        Assert.DoesNotContain("\"disabledOptionalModuleIds\"", json);
        Assert.Contains("\"disabledOptionalModuleIdsByServer\"", json);
        Assert.Contains("\"schemaVersion\": 3", json);
    }

    [Fact]
    public async Task ExistingDefaultServerEntryIsNotOverwrittenByLegacyList()
    {
        await WriteSettingsAsync("""
            {
              "schemaVersion": 3,
              "disabledOptionalModuleIds": ["old"],
              "disabledOptionalModuleIdsByServer": { "default": ["new"] }
            }
            """);

        var settings = await new AppSettingsManager(_paths).LoadAsync();

        Assert.Equal(["new"], settings.DisabledOptionalModuleIdsByServer["default"]);
    }

    [Fact]
    public async Task SelectedServerIdRoundTrips()
    {
        await new AppSettingsManager(_paths).SaveAsync(new LauncherSettings { SelectedServerId = "second" });

        var loaded = await new AppSettingsManager(_paths).LoadAsync();

        Assert.Equal("second", loaded.SelectedServerId);
    }

    [Fact]
    public void DisabledModulesForReturnsTheStoredInstance()
    {
        var settings = new LauncherSettings();

        var disabled = settings.DisabledModulesFor("x");

        Assert.Same(disabled, settings.DisabledModulesFor("x"));
        Assert.Same(disabled, settings.DisabledOptionalModuleIdsByServer["x"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task WriteSettingsAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsFile)!);
        await File.WriteAllTextAsync(_paths.SettingsFile, json);
    }
}
