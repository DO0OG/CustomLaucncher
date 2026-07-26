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

    private ManifestToolViewModel CreateViewModel() => new(new ToolSettings(), persistSettings: false);

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

        Assert.Equal(Path.GetFileName(_root), viewModel.ServerId);
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
        var distribution = ReadOutput();
        Assert.Equal(2, distribution.Modules.Count);
        Assert.Contains(distribution.Modules,
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
        Assert.Equal(2, ReadOutput().Modules.Count);
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
        edited.Modules[0].Type = ModuleType.OptionalMod;
        edited.Modules[0].LoadOrder = 3;
        await GenerateSaveAsync(edited);

        WriteFile("mods/b.jar", "b");
        await viewModel.GenerateAsync();

        var result = ReadOutput().Modules.Single(module => module.Path.EndsWith("a.jar", StringComparison.Ordinal));
        Assert.Equal(ModuleType.OptionalMod, result.Type);
        Assert.Equal(3, result.LoadOrder);
    }

    private Task GenerateSaveAsync(ServerDistribution distribution) =>
        ManifestTool.Commands.GenerateCommand.SaveAsync(distribution, OutputPath);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
