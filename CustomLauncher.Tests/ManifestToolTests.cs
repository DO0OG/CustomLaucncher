using System.Security.Cryptography;
using CustomLauncher.ManifestTool.Commands;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests;

/// <summary>
/// The QA matrix claims manifest tooling is covered on every pull request; these are the tests that
/// make that true. Merge preservation in particular is a documented fail condition: regenerating a
/// manifest must not discard the operator's hand-set ids, types, parents, and load orders.
/// </summary>
public sealed class ManifestToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cl-manifest-{Guid.NewGuid():N}");

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private GenerateOptions Options(string? existing = null) => new()
    {
        SourceDirectory = _root,
        OutputPath = Path.Combine(_root, "distribution.json"),
        ExistingPath = existing,
        ServerId = "demo",
        BaseUrl = "https://cdn.example.com/files",
    };

    [Fact]
    public async Task HashesMatchAnIndependentSha256()
    {
        WriteFile("mods/a.jar", "mod-a");

        var distribution = await GenerateCommand.GenerateAsync(Options());

        var expected = Convert.ToHexString(SHA256.HashData("mod-a"u8.ToArray())).ToLowerInvariant();
        Assert.Equal(expected, distribution.Modules.Single().Hash);
    }

    [Fact]
    public async Task PathsAndUrlsUseForwardSlashesOnEveryPlatform()
    {
        WriteFile("mods/nested/deep.jar", "x");

        var module = (await GenerateCommand.GenerateAsync(Options())).Modules.Single();

        Assert.Equal("mods/nested/deep.jar", module.Path);
        Assert.Equal("https://cdn.example.com/files/mods/nested/deep.jar", module.Url);
    }

    [Fact]
    public async Task GeneratedManifestPassesTheLauncherValidator()
    {
        WriteFile("mods/a.jar", "mod-a");
        WriteFile("mods/b.jar", "mod-b");

        var distribution = await GenerateCommand.GenerateAsync(Options());

        // The launcher refuses manifests it cannot verify; the tool must not emit one.
        CustomLauncher.Core.ModuleValidation.ValidateDistribution(distribution);
    }

    [Fact]
    public async Task RegeneratingPreservesHandEditedMetadataAndPicksUpNewFiles()
    {
        WriteFile("mods/a.jar", "mod-a");
        WriteFile("mods/b.jar", "mod-b");
        var first = await GenerateCommand.GenerateAsync(Options());

        var edited = first.Modules.Single(module => module.Path.EndsWith("a.jar", StringComparison.Ordinal));
        edited.Id = "handpicked-id";
        edited.Type = ModuleType.OptionalMod;
        edited.LoadOrder = 7;
        edited.IsServerManaged = false;
        var existingPath = Path.Combine(_root, "existing.json");
        await File.WriteAllTextAsync(existingPath,
            System.Text.Json.JsonSerializer.Serialize(first, ManifestJson.Options));

        WriteFile("mods/c.jar", "mod-c");
        var second = await GenerateCommand.GenerateAsync(Options(existingPath));

        var preserved = second.Modules.Single(module => module.Path.EndsWith("a.jar", StringComparison.Ordinal));
        Assert.Equal("handpicked-id", preserved.Id);
        Assert.Equal(ModuleType.OptionalMod, preserved.Type);
        Assert.Equal(7, preserved.LoadOrder);
        Assert.False(preserved.IsServerManaged);
        Assert.Contains(second.Modules, module => module.Path.EndsWith("c.jar", StringComparison.Ordinal));
        Assert.Equal(3, second.Modules.Count);
    }

    [Fact]
    public async Task ChangedContentUpdatesTheHashEvenWhenMetadataIsPreserved()
    {
        WriteFile("mods/a.jar", "before");
        var first = await GenerateCommand.GenerateAsync(Options());
        var existingPath = Path.Combine(_root, "existing.json");
        await File.WriteAllTextAsync(existingPath,
            System.Text.Json.JsonSerializer.Serialize(first, ManifestJson.Options));

        WriteFile("mods/a.jar", "after");
        var second = await GenerateCommand.GenerateAsync(Options(existingPath));

        Assert.NotEqual(first.Modules.Single().Hash, second.Modules.Single().Hash);
    }

    [Fact]
    public async Task DiffReportsAdditionsRemovalsAndChanges()
    {
        WriteFile("mods/a.jar", "mod-a");
        WriteFile("mods/b.jar", "mod-b");
        var before = await GenerateCommand.GenerateAsync(Options());

        File.Delete(Path.Combine(_root, "mods", "b.jar"));
        WriteFile("mods/a.jar", "mod-a-changed");
        WriteFile("mods/c.jar", "mod-c");
        var after = await GenerateCommand.GenerateAsync(Options());

        var changes = DiffCommand.Compare(before, after);

        Assert.Contains(changes, change => change.StartsWith('+') && change.Contains("c.jar", StringComparison.Ordinal));
        Assert.Contains(changes, change => change.StartsWith('-') && change.Contains("b.jar", StringComparison.Ordinal));
        Assert.Contains(changes, change => change.StartsWith('~') && change.Contains("a.jar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiffOnIdenticalManifestsReportsNothing()
    {
        WriteFile("mods/a.jar", "mod-a");
        var distribution = await GenerateCommand.GenerateAsync(Options());

        Assert.Empty(DiffCommand.Compare(distribution, distribution));
    }

    [Fact]
    public async Task TheOutputFileIsNotScannedAsAModule()
    {
        WriteFile("mods/a.jar", "mod-a");
        var options = Options();
        await GenerateCommand.ExecuteAsync([_root, "--output", options.OutputPath, "--base-url", options.BaseUrl!]);

        var second = await GenerateCommand.GenerateAsync(options);

        Assert.DoesNotContain(second.Modules, module => module.Path.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingDirectoryFailsLoudly() =>
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            GenerateCommand.GenerateAsync(Options() with { SourceDirectory = Path.Combine(_root, "nope") }));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
