using System.Collections.ObjectModel;
using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.ViewModels;

public sealed class ModuleManagementViewModel
{
    private readonly ModuleManager _manager;
    public ObservableCollection<DistroModule> Modules { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public string Status { get; private set; } = "업데이트 확인 전";

    public ModuleManagementViewModel(ModuleManager manager)
    {
        _manager = manager;
        RefreshCommand = new AsyncCommand(RefreshAsync);
    }

    public async Task RefreshAsync()
    {
        var distribution = await _manager.FetchDistributionAsync(LauncherConfig.ManifestUrl);
        var result = await _manager.UpdateAsync(distribution);
        Modules.Clear();
        foreach (var module in distribution.Modules) Modules.Add(module);
        Status = result.Status.ToString();
    }
}

public sealed class ShaderPackViewModel : ViewModelBase
{
    private readonly ShaderPackManager _manager;
    private ShaderPackInfo? _selectedPack;
    public ObservableCollection<ShaderPackInfo> Packs { get; } = [];
    public ShaderPackInfo? SelectedPack { get => _selectedPack; set => SetProperty(ref _selectedPack, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplyCommand { get; }

    public ShaderPackViewModel(ShaderPackManager manager)
    {
        _manager = manager;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        ApplyCommand = new AsyncCommand(ApplyAsync);
    }

    public async Task RefreshAsync()
    {
        Packs.Clear();
        foreach (var pack in await _manager.GetPacksAsync()) Packs.Add(pack);
        SelectedPack = Packs.FirstOrDefault(pack => pack.IsSelected);
    }

    public async Task ApplyAsync()
    {
        _manager.SetSelected(SelectedPack?.FileName);
        await _manager.ApplyAsync();
        await RefreshAsync();
    }
}

public sealed class ResourcePackViewModel : ViewModelBase
{
    private readonly ResourcePackManager _manager;
    private ResourcePackInfo? _selectedPack;
    public ObservableCollection<ResourcePackInfo> Packs { get; } = [];
    public ResourcePackInfo? SelectedPack { get => _selectedPack; set => SetProperty(ref _selectedPack, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand MoveUpCommand { get; }
    public AsyncCommand MoveDownCommand { get; }

    public ResourcePackViewModel(ResourcePackManager manager)
    {
        _manager = manager;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        ApplyCommand = new AsyncCommand(ApplyAsync);
        MoveUpCommand = new AsyncCommand(() => MoveAsync(-1));
        MoveDownCommand = new AsyncCommand(() => MoveAsync(1));
    }

    public async Task RefreshAsync()
    {
        await _manager.RefreshAsync();
        Packs.Clear();
        foreach (var pack in _manager.Packs) Packs.Add(pack);
    }

    public async Task ApplyAsync() => await _manager.ApplyAsync();

    private async Task MoveAsync(int offset)
    {
        if (SelectedPack is null) return;
        _manager.Reorder(SelectedPack.Id, SelectedPack.Order + offset);
        var id = SelectedPack.Id;
        Packs.Clear();
        foreach (var pack in _manager.Packs) Packs.Add(pack);
        SelectedPack = Packs.First(pack => pack.Id == id);
        await _manager.ApplyAsync();
    }
}
