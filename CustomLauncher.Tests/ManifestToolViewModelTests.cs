using System.Text.Json;
using CustomLauncher.ManifestTool;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests;

/// <summary>
/// The window is how the operator actually runs this, so its validation, defaults, merge and change
/// report are covered rather than only the generation library underneath it.
/// </summary>
public sealed class ManifestToolViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cl-tool-ui-{Guid.NewGuid():N}");
    private const string Template = "https://host/d/TOKEN/files/?p=/{path}&dl=1";

    public ManifestToolViewModelTests() => Directory.CreateDirectory(_root);

    private static ManifestToolViewModel CreateViewModel()
    {
        var viewModel = new ManifestToolViewModel(new ToolSettings(), persistSettings: false);
        // The single starting server needs the fields the manifest requires before it can be saved.
        var server = viewModel.SelectedServer!;
        server.Name = "Test";
        server.Address = "play.example.com";
        server.MinecraftVersion = "1.20.1";
        return viewModel;
    }

    private void WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string OutputPath => Path.Combine(_root, "distribution.json");

    private ServerDistribution ReadOutput() =>
        JsonSerializer.Deserialize<ServerDistribution>(File.ReadAllText(OutputPath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            })!;

    private List<DistroModule> ReadModules() => ReadOutput().Servers[0].Modules;

    [Fact]
    public async Task AMissingFolderIsExplainedInsteadOfThrowing()
    {
        var viewModel = CreateViewModel();
        viewModel.UrlTemplate = Template;

        await viewModel.GenerateAsync();

        Assert.Contains("배포 폴더", viewModel.Log);
        Assert.False(File.Exists(OutputPath));
    }

    [Fact]
    public async Task AUrlWithoutThePlaceholderIsRejectedBeforeAnythingIsWritten()
    {
        var viewModel = CreateViewModel();
        viewModel.SourceDirectory = _root;
        viewModel.UrlTemplate = "https://host/files/?dl=1";

        await viewModel.GenerateAsync();

        Assert.Contains("{path}", viewModel.Log);
        Assert.False(File.Exists(OutputPath));
    }

    [Fact]
    public void ChoosingAFolderFillsInTheObviousDefaults()
    {
        var viewModel = CreateViewModel();

        viewModel.SourceDirectory = _root;

        Assert.Equal(OutputPath, viewModel.OutputPath);
    }

    [Fact]
    public async Task GeneratingWritesTheManifestAndReportsTheCount()
    {
        WriteFile("mods/a.jar", "a");
        WriteFile("config/sub/deep.toml", "c");
        var viewModel = CreateViewModel();
        viewModel.SourceDirectory = _root;
        viewModel.UrlTemplate = Template;

        await viewModel.GenerateAsync();

        Assert.Contains("모듈 2개", viewModel.Log);
        var modules = ReadModules();
        Assert.Equal(2, modules.Count);
        Assert.Contains(modules,
            module => module.Url == "https://host/d/TOKEN/files/?p=/mods/a.jar&dl=1");
    }

    [Fact]
    public async Task ASecondRunReportsWhatChanged()
    {
        WriteFile("mods/a.jar", "a");
        var viewModel = CreateViewModel();
        viewModel.SourceDirectory = _root;
        viewModel.UrlTemplate = Template;
        await viewModel.GenerateAsync();

        WriteFile("mods/b.jar", "b");
        WriteFile("mods/a.jar", "a-changed");
        await viewModel.GenerateAsync();

        Assert.Contains("변경", viewModel.Log);
        Assert.Contains("b.jar", viewModel.Log);
        Assert.Equal(2, ReadModules().Count);
    }

    [Fact]
    public async Task RerunningWithNoChangesSaysSo()
    {
        WriteFile("mods/a.jar", "a");
        var viewModel = CreateViewModel();
        viewModel.SourceDirectory = _root;
        viewModel.UrlTemplate = Template;
        await viewModel.GenerateAsync();

        await viewModel.GenerateAsync();

        Assert.Contains("달라진 점이 없습니다", viewModel.Log);
    }

    [Fact]
    public async Task HandEditedMetadataSurvivesWhenMergeIsOn()
    {
        WriteFile("mods/a.jar", "a");
        var viewModel = CreateViewModel();
        viewModel.SourceDirectory = _root;
        viewModel.UrlTemplate = Template;
        await viewModel.GenerateAsync();

        var edited = ReadOutput();
        edited.Servers[0].Modules[0].Type = ModuleType.OptionalMod;
        edited.Servers[0].Modules[0].LoadOrder = 3;
        await GenerateSaveAsync(edited);
        await viewModel.LoadAsync();

        WriteFile("mods/b.jar", "b");
        await viewModel.GenerateAsync();

        var result = ReadModules().Single(module => module.Path.EndsWith("a.jar", StringComparison.Ordinal));
        Assert.Equal(ModuleType.OptionalMod, result.Type);
        Assert.Equal(3, result.LoadOrder);
    }

    private async Task WriteTwoServerManifestAsync()
    {
        WriteFile("src/mods/new.jar", "new");
        var manifest = new ServerDistribution
        {
            DefaultShaderPackId = "shader-1",
            Servers =
            [
                new ServerEntry
                {
                    Id = "alpha",
                    Name = "Alpha",
                    Address = "alpha.example.com",
                    MinecraftVersion = "1.20.1",
                    ModLoader = new ModLoaderSpec { Type = "forge", Version = "47.2.0" },
                    Java = new JavaRequirement { MinVersion = "17", RecommendedVersion = "17" },
                    Modules = [new DistroModule { Id = "alpha-mod", Path = "mods/alpha.jar", Hash = "aa" }],
                },
                new ServerEntry
                {
                    Id = "beta",
                    Name = "Beta",
                    Address = "beta.example.com",
                    Port = 25570,
                    MinecraftVersion = "1.21.1",
                    ModLoader = new ModLoaderSpec { Type = "neoforge", Version = "21.1.0" },
                    Java = new JavaRequirement { MinVersion = "21", RecommendedVersion = "21" },
                    Modules = [new DistroModule { Id = "beta-old", Path = "mods/old.jar", Hash = "bb" }],
                },
            ],
        };
        await ManifestTool.Commands.GenerateCommand.SaveAsync(manifest, OutputPath);
    }

    [Fact]
    public async Task GeneratingReplacesOnlyTheSelectedServersModules()
    {
        var sourceFolder = Path.Combine(_root, "src");
        await WriteTwoServerManifestAsync();
        var viewModel = CreateViewModel();
        viewModel.OutputPath = OutputPath;
        viewModel.SourceDirectory = sourceFolder;
        viewModel.UrlTemplate = Template;
        await viewModel.LoadAsync();
        Assert.Equal(2, viewModel.Servers.Count);

        viewModel.SelectedServer = viewModel.Servers[1];
        await viewModel.GenerateAsync();

        var saved = ReadOutput();
        Assert.Equal(string.Empty, saved.ServerId);
        Assert.Empty(saved.Modules);
        Assert.Equal("shader-1", saved.DefaultShaderPackId);
        var alpha = saved.Servers[0];
        var beta = saved.Servers[1];
        Assert.Equal("alpha-mod", Assert.Single(alpha.Modules).Id);
        Assert.Equal("mods/new.jar", Assert.Single(beta.Modules).Path);
        Assert.Equal(25570, beta.Port);
        Assert.Equal("neoforge", beta.ModLoader.Type);
        Assert.Equal("21.1.0", beta.ModLoader.Version);
        Assert.Equal("21", beta.Java.MinVersion);
        Assert.Equal("21", beta.Java.RecommendedVersion);
        Assert.Contains("[beta]", viewModel.Log);
    }

    [Fact]
    public async Task AnOldSingleServerFileLoadsAsOneServerAndSavesInTheNewShape()
    {
        var legacy = new ServerDistribution
        {
            ServerId = "dogs",
            Modules = [new DistroModule { Id = "m", Path = "mods/m.jar", Hash = "cc", Url = "https://a/m.jar" }],
        };
        await ManifestTool.Commands.GenerateCommand.SaveAsync(legacy, OutputPath);
        var viewModel = CreateViewModel();
        viewModel.OutputPath = OutputPath;

        await viewModel.LoadAsync();

        var server = Assert.Single(viewModel.Servers);
        Assert.Equal("default", server.Id);
        Assert.Equal("dogs", server.Name);
        Assert.Contains("옛 단일 서버", viewModel.Log);

        await viewModel.SaveAsync();
        Assert.Contains("저장하지 않았습니다", viewModel.Log);

        server.Address = "dogs.example.com";
        server.MinecraftVersion = "1.20.1";
        await viewModel.SaveAsync();

        var saved = ReadOutput();
        var entry = Assert.Single(saved.Servers);
        Assert.Equal("m", Assert.Single(entry.Modules).Id);
        Assert.Empty(saved.Modules);
        Assert.Equal(string.Empty, saved.ServerId);
    }

    [Fact]
    public async Task SavingOverAnotherExistingManifestIsRefusedUntilItIsLoaded()
    {
        await WriteTwoServerManifestAsync();
        var other = Path.Combine(_root, "other.json");
        File.Copy(OutputPath, other);
        var before = await File.ReadAllBytesAsync(other);
        var viewModel = CreateViewModel();
        viewModel.OutputPath = OutputPath;
        await viewModel.LoadAsync();
        viewModel.Servers[0].Name = "Changed";

        viewModel.OutputPath = other;
        await viewModel.SaveAsync();

        Assert.Contains("먼저 '불러오기'", viewModel.Log);
        Assert.Equal(before, await File.ReadAllBytesAsync(other));

        await viewModel.LoadAsync();
        await viewModel.SaveAsync();
        Assert.Contains("저장했습니다", viewModel.Log);
    }

    [Fact]
    public async Task AFailedValidationLeavesTheFileUntouched()
    {
        await WriteTwoServerManifestAsync();
        var before = await File.ReadAllBytesAsync(OutputPath);
        var viewModel = CreateViewModel();
        viewModel.OutputPath = OutputPath;
        viewModel.SourceDirectory = Path.Combine(_root, "src");
        viewModel.UrlTemplate = Template;
        await viewModel.LoadAsync();
        viewModel.Servers[1].Id = "alpha";

        await viewModel.SaveAsync();
        Assert.Contains("Duplicate", viewModel.Log);
        Assert.Equal(before, await File.ReadAllBytesAsync(OutputPath));

        viewModel.SelectedServer = viewModel.Servers[1];
        await viewModel.GenerateAsync();
        Assert.Contains("Duplicate", viewModel.Log);
        Assert.Equal(before, await File.ReadAllBytesAsync(OutputPath));
        Assert.Equal("beta-old", Assert.Single(viewModel.Servers[1].Entry.Modules).Id);
    }

    [Fact]
    public void NewServersGetUniqueIdsAndTheLastOneCannotBeDeleted()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.CanDeleteServer);

        viewModel.AddServer();
        viewModel.AddServer();
        viewModel.Servers[1].Id = "server-3";
        viewModel.AddServer();

        Assert.Equal(["default", "server-3", "server-2", "server-1"], viewModel.Servers.Select(server => server.Id));
        var added = viewModel.SelectedServer!.Entry;
        Assert.Equal(25565, added.Port);
        Assert.Equal("vanilla", added.ModLoader.Type);

        while (viewModel.Servers.Count > 1) viewModel.RemoveServer();
        Assert.False(viewModel.CanDeleteServer);
        viewModel.RemoveServer();
        Assert.Single(viewModel.Servers);
    }

    [Fact]
    public void AnOldSettingsFileWithAServerIdStillLoads()
    {
        var settings = JsonSerializer.Deserialize<ToolSettings>(
            "{\"serverId\":\"dogs\",\"urlTemplate\":\"https://h/{path}\"}",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Equal("https://h/{path}", settings!.UrlTemplate);
    }

    private Task GenerateSaveAsync(ServerDistribution distribution) =>
        ManifestTool.Commands.GenerateCommand.SaveAsync(distribution, OutputPath);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
