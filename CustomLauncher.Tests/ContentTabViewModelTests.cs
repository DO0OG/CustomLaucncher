using CustomLauncher.Core;
using CustomLauncher.Models;
using CustomLauncher.Shared.Models;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Tests;

/// <summary>
/// These tests exist because the previous revision let command failures escape as unhandled
/// exceptions and dropped every status enum the core returned.
/// </summary>
public sealed class ContentTabViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cl-tabs-{Guid.NewGuid():N}");

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteZip(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);
        return path;
    }

    [Fact]
    public async Task ShaderApplyReportsMissingOptionsFileInsteadOfClaimingSuccess()
    {
        var packs = Dir("shaderpacks");
        WriteZip(packs, "fancy.zip");
        var manager = new ShaderPackManager(packs, Path.Combine(_root, "optionsshaders.txt"), "shaderPack",
            new OptionsTextEditor());
        var viewModel = new ShaderPackViewModel(manager);

        await viewModel.RefreshAsync();
        viewModel.SelectedPack = viewModel.Packs.Single();
        await viewModel.ApplyAsync();

        Assert.Contains("게임 설정 파일이 아직 없습니다", viewModel.Message);
    }

    [Fact]
    public async Task ShaderApplyReportsGameRunning()
    {
        var packs = Dir("shaderpacks");
        WriteZip(packs, "fancy.zip");
        var optionsFile = Path.Combine(_root, "optionsshaders.txt");
        File.WriteAllText(optionsFile, "shaderPack:OFF\n");
        var manager = new ShaderPackManager(packs, optionsFile, "shaderPack", new OptionsTextEditor(() => true));
        var viewModel = new ShaderPackViewModel(manager);

        await viewModel.RefreshAsync();
        await viewModel.ApplyAsync();

        Assert.Contains("게임이 실행 중", viewModel.Message);
    }

    [Fact]
    public async Task ShaderApplyReportsSuccessAndWritesTheFile()
    {
        var packs = Dir("shaderpacks");
        WriteZip(packs, "fancy.zip");
        var optionsFile = Path.Combine(_root, "optionsshaders.txt");
        File.WriteAllText(optionsFile, "shaderPack:OFF\nother:keep\n");
        var manager = new ShaderPackManager(packs, optionsFile, "shaderPack", new OptionsTextEditor());
        var viewModel = new ShaderPackViewModel(manager);

        await viewModel.RefreshAsync();
        viewModel.SelectedPack = viewModel.Packs.Single();
        await viewModel.ApplyAsync();

        var written = File.ReadAllText(optionsFile);
        Assert.Contains("shaderPack:fancy.zip", written);
        Assert.Contains("other:keep", written);
        Assert.Contains("적용", viewModel.Message);
    }

    [Fact]
    public async Task MovingARequiredResourcePackReportsInsteadOfThrowing()
    {
        var packs = Dir("resourcepacks");
        WriteZip(packs, "server.zip");
        WriteZip(packs, "mine.zip");
        var manager = new ResourcePackManager(packs, Path.Combine(_root, "options.txt"),
            new OptionsTextEditor(), ["server.zip"]);
        var viewModel = new ResourcePackViewModel(manager);
        await viewModel.RefreshAsync();

        viewModel.SelectedPack = viewModel.Packs.Single(pack => pack.IsServerManaged);
        // Would previously throw out of an async void command and terminate the app.
        await viewModel.MoveAsync(1);

        Assert.Contains("must remain at the top", viewModel.Message);
        Assert.Equal("server.zip", viewModel.Packs[0].FileName);
    }

    [Fact]
    public async Task DisablingARequiredResourcePackIsRefusedAndTheCheckboxReverts()
    {
        var packs = Dir("resourcepacks");
        WriteZip(packs, "server.zip");
        var manager = new ResourcePackManager(packs, Path.Combine(_root, "options.txt"),
            new OptionsTextEditor(), ["server.zip"]);
        var viewModel = new ResourcePackViewModel(manager);
        await viewModel.RefreshAsync();

        var item = viewModel.Packs.Single();
        Assert.True(item.IsEnabled);
        item.IsEnabled = false;

        Assert.True(item.IsEnabled);
        Assert.Contains("cannot be disabled", viewModel.Message);
    }

    [Fact]
    public async Task EnablingAndOrderingIsWrittenToOptions()
    {
        var packs = Dir("resourcepacks");
        WriteZip(packs, "a.zip");
        WriteZip(packs, "b.zip");
        var optionsFile = Path.Combine(_root, "options.txt");
        File.WriteAllText(optionsFile, "resourcePacks:[]\nfov:0.5\n");
        var manager = new ResourcePackManager(packs, optionsFile, new OptionsTextEditor());
        var viewModel = new ResourcePackViewModel(manager);
        await viewModel.RefreshAsync();

        foreach (var item in viewModel.Packs) item.IsEnabled = true;
        await viewModel.MoveToAsync(viewModel.Packs.Single(pack => pack.FileName == "b.zip").Id, 0);
        await viewModel.ApplyAsync();

        var written = File.ReadAllText(optionsFile);
        Assert.Contains("resourcePacks:[\"file/b.zip\",\"file/a.zip\"]", written);
        Assert.Contains("fov:0.5", written);
    }

    [Fact]
    public async Task DropTargetHighlightsExactlyOneRow()
    {
        var packs = Dir("resourcepacks");
        WriteZip(packs, "a.zip");
        WriteZip(packs, "b.zip");
        var manager = new ResourcePackManager(packs, Path.Combine(_root, "options.txt"), new OptionsTextEditor());
        var viewModel = new ResourcePackViewModel(manager);
        await viewModel.RefreshAsync();

        viewModel.SetDropTarget(1);
        Assert.False(viewModel.Packs[0].IsDropTarget);
        Assert.True(viewModel.Packs[1].IsDropTarget);

        viewModel.SetDropTarget(null);
        Assert.All(viewModel.Packs, pack => Assert.False(pack.IsDropTarget));
    }

    [Fact]
    public async Task ManifestFailureIsShownAsAMessageAndNotThrown()
    {
        var viewModel = new ModuleManagementViewModel(
            new ThrowingContentService(),
            new DropInModManager(Dir("mods")),
            new ModuleManagementViewModel.ModuleSelectionStore(() => [], _ => Task.CompletedTask));

        var exception = await Record.ExceptionAsync(viewModel.RefreshAsync);

        Assert.Null(exception);
        Assert.Contains("네트워크 오류", viewModel.Message);
    }

    [Fact]
    public async Task FailedUpdateIsReportedWithTheUnderlyingReason()
    {
        var content = new StubContentService(new ModuleUpdateResult(ModuleUpdateStatus.Failed, [], "해시 불일치"));
        var viewModel = new ModuleManagementViewModel(content, new DropInModManager(Dir("mods")),
            new ModuleManagementViewModel.ModuleSelectionStore(() => [], _ => Task.CompletedTask));

        await viewModel.RefreshAsync();
        await viewModel.UpdateAsync();

        Assert.Contains("해시 불일치", viewModel.Message);
        Assert.Equal(0, content.CacheSaves);
    }

    [Fact]
    public async Task DisabledOptionalModulesAreExcludedFromTheUpdate()
    {
        var content = new StubContentService(new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []));
        var disabled = new List<string> { "optional" };
        var viewModel = new ModuleManagementViewModel(content, new DropInModManager(Dir("mods")),
            new ModuleManagementViewModel.ModuleSelectionStore(() => disabled, _ => Task.CompletedTask));

        await viewModel.RefreshAsync();
        await viewModel.UpdateAsync();

        Assert.NotNull(content.LastUpdated);
        Assert.DoesNotContain(content.LastUpdated!.Modules, module => module.Id == "optional");
        Assert.Contains(content.LastUpdated.Modules, module => module.Id == "core");
    }

    [Fact]
    public async Task TogglingAnOptionalModulePersistsTheChoice()
    {
        var content = new StubContentService(new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, []));
        var disabled = new List<string>();
        var saves = 0;
        var viewModel = new ModuleManagementViewModel(content, new DropInModManager(Dir("mods")),
            new ModuleManagementViewModel.ModuleSelectionStore(() => disabled, _ => { saves++; return Task.CompletedTask; }));

        await viewModel.RefreshAsync();
        var optional = viewModel.Modules.Single(module => module.Id == "optional");
        optional.IsEnabled = false;

        Assert.Contains("optional", disabled);
        Assert.Equal(1, saves);
        // The child is forced off because its parent is off.
        Assert.False(viewModel.Modules.Single(module => module.Id == "child").IsEffectivelyEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class ThrowingContentService : IContentUpdateService
    {
        public bool HasManagedState => false;
        public Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken) =>
            throw new HttpRequestException("연결할 수 없습니다.");
        public Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task SaveCacheAsync(ServerDistribution distribution, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<ServerDistribution?> TryLoadCacheAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ServerDistribution?>(null);
    }

    private sealed class StubContentService(ModuleUpdateResult result) : IContentUpdateService
    {
        public ServerDistribution? LastUpdated { get; private set; }
        public int CacheSaves { get; private set; }
        public bool HasManagedState => true;

        public Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServerDistribution
            {
                ServerId = "test",
                Modules =
                [
                    NewModule("core", ModuleType.RequiredMod, null),
                    NewModule("optional", ModuleType.OptionalMod, null),
                    NewModule("child", ModuleType.OptionalMod, "optional"),
                ],
            });

        public Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken)
        {
            LastUpdated = distribution;
            return Task.FromResult(result);
        }

        public Task SaveCacheAsync(ServerDistribution distribution, CancellationToken cancellationToken)
        {
            CacheSaves++;
            return Task.CompletedTask;
        }

        public Task<ServerDistribution?> TryLoadCacheAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ServerDistribution?>(null);

        private static DistroModule NewModule(string id, ModuleType type, string? parent) => new()
        {
            Id = id,
            Path = $"mods/{id}.jar",
            Url = $"https://example.com/{id}.jar",
            Hash = new string('b', 64),
            Type = type,
            ParentId = parent,
            IsServerManaged = true,
        };
    }
}

public sealed class JavaSettingsViewModelTests
{
    private sealed class FixedMemory(long bytes) : ISystemMemoryProvider
    {
        public long GetTotalMemoryBytes() => bytes;
    }

    [Fact]
    public void RamBoundsFollowTheMachineInsteadOfAFixedRange()
    {
        var viewModel = new JavaSettingsViewModel(new JavaConfig(), new FixedMemory(8L * 1024 * 1024 * 1024));

        // 65536 was the previous hard-coded ceiling; an 8 GiB machine must not offer it.
        Assert.True(viewModel.RamMaximumBound < 65536);
        Assert.Equal(RamCalculator.Calculate(8L * 1024 * 1024 * 1024).MaximumMb, viewModel.RamMaximumBound);
        Assert.Contains("권장", viewModel.RamAdviceText);
    }

    [Fact]
    public void HeapArgumentsRaiseAWarning()
    {
        var viewModel = new JavaSettingsViewModel(new JavaConfig(), new FixedMemory(8L * 1024 * 1024 * 1024))
        {
            JvmArgumentsText = "-XX:+UseG1GC"
        };
        Assert.False(viewModel.HasArgumentWarning);

        viewModel.JvmArgumentsText = "-XX:+UseG1GC\n-Xmx8G";

        Assert.True(viewModel.HasArgumentWarning);
        Assert.Contains("-Xmx", viewModel.ArgumentWarning);
    }

    [Fact]
    public void ApplyToCopiesEditsBackIntoTheModel()
    {
        var config = new JavaConfig();
        var viewModel = new JavaSettingsViewModel(config, new FixedMemory(16L * 1024 * 1024 * 1024))
        {
            ExecutablePath = "  /usr/bin/java  ",
            MinRamMb = 1024,
            MaxRamMb = 6144,
            AutoInstallEnabled = true,
            JvmArgumentsText = "-XX:+UseG1GC\n\n-Dfoo=bar",
        };

        var target = new JavaConfig();
        viewModel.ApplyTo(target);

        Assert.Equal("/usr/bin/java", target.ExecutablePath);
        Assert.Equal(1024, target.MinRamMb);
        Assert.Equal(6144, target.MaxRamMb);
        Assert.True(target.AutoInstallEnabled);
        Assert.Equal(["-XX:+UseG1GC", "-Dfoo=bar"], target.CustomJvmArguments);
    }
}
