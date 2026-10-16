using CustomLauncher.Core;

namespace CustomLauncher.Tests.Core;

public sealed class ContentPackManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "launcher-pack-tests-" + Guid.NewGuid().ToString("N"));

    public ContentPackManagerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task DropIn_AddDelete_PreservesServerManagedFiles()
    {
        var packs = Path.Combine(_directory, "shaderpacks");
        Directory.CreateDirectory(packs);
        await File.WriteAllTextAsync(Path.Combine(packs, "server.zip"), "server");
        var source = Path.Combine(_directory, "user.zip");
        await File.WriteAllTextAsync(source, "user");
        var manager = new ShaderPackManager(packs, Path.Combine(_directory, "options.txt"), "shader", new OptionsTextEditor(), ["server.zip"]);

        await manager.AddAsync(source);
        await manager.DeleteAsync("user.zip");

        Assert.True(File.Exists(Path.Combine(packs, "server.zip")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteAsync("server.zip"));
    }

    [Fact]
    public async Task DropIn_ToggleRenamesJarAndReportsEnabledState()
    {
        var mods = Path.Combine(_directory, "mods");
        Directory.CreateDirectory(mods);
        await File.WriteAllTextAsync(Path.Combine(mods, "user.jar"), "user");
        var manager = new DropInModManager(mods);

        var disabled = await manager.SetEnabledAsync("user.jar", false);

        Assert.Equal("user.jar.disabled", disabled.FileName);
        Assert.False(File.Exists(Path.Combine(mods, "user.jar")));
        Assert.False(Assert.Single(await manager.ScanAsync()).IsEnabled);

        var enabled = await manager.SetEnabledAsync("user.jar.disabled", true);

        Assert.Equal("user.jar", enabled.FileName);
        Assert.True(File.Exists(Path.Combine(mods, "user.jar")));
        var relisted = Assert.Single(await manager.ScanAsync());
        Assert.Equal("user.jar", relisted.FileName);
        Assert.True(relisted.IsEnabled);
    }

    [Fact]
    public async Task DropIn_ServerManagedJarCannotBeDisabled()
    {
        var mods = Path.Combine(_directory, "mods-managed");
        Directory.CreateDirectory(mods);
        await File.WriteAllTextAsync(Path.Combine(mods, "server.jar"), "server");
        var manager = new DropInModManager(mods, ["server.jar"]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SetEnabledAsync("server.jar", false));

        Assert.True(File.Exists(Path.Combine(mods, "server.jar")));
        Assert.False(File.Exists(Path.Combine(mods, "server.jar.disabled")));
    }

    [Fact]
    public async Task ResourcePacks_EnforceRequiredBoundaryAndSerializeOrder()
    {
        var packs = Path.Combine(_directory, "resourcepacks");
        Directory.CreateDirectory(packs);
        await File.WriteAllTextAsync(Path.Combine(packs, "required.zip"), "r");
        await File.WriteAllTextAsync(Path.Combine(packs, "optional.zip"), "o");
        var options = Path.Combine(_directory, "options.txt");
        await File.WriteAllTextAsync(options, "resourcePacks:[]\nother:value\n");
        var manager = new ResourcePackManager(packs, options, new OptionsTextEditor(), ["required.zip"]);
        await manager.RefreshAsync();
        manager.SetEnabled("optional.zip", true);

        Assert.Throws<InvalidOperationException>(() => manager.Reorder("required.zip", 1));
        manager.Reorder("optional.zip", 0);
        await manager.ApplyAsync();

        var text = await File.ReadAllTextAsync(options);
        Assert.Contains("resourcePacks:[\"file/required.zip\",\"file/optional.zip\"]", text);
        Assert.Contains("other:value", text);
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
