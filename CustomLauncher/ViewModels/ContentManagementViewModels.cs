using System.Collections.ObjectModel;
using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.ViewModels;

/// <summary>
/// Shared plumbing for the content tabs: a single busy flag, a user-visible message, cancellation
/// for the in-flight operation, and error routing. Commands must never throw out of
/// <see cref="AsyncCommand.Execute"/>, so every failure lands in <see cref="Message"/>.
/// </summary>
public abstract class ContentTabViewModel : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _operation;
    private RelayCommand? _cancelCommand;
    private string _message = string.Empty;
    private bool _busy;
    private double _progress;

    public string Message { get => _message; protected set => SetProperty(ref _message, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public double Progress { get => _progress; protected set => SetProperty(ref _progress, value); }
    public bool CanCancel => _operation is not null;
    public RelayCommand CancelCommand => _cancelCommand ??= new RelayCommand(Cancel, () => CanCancel);

    protected void Cancel() => _operation?.Cancel();

    protected void ReportError(Exception exception) =>
        Message = exception switch
        {
            OperationCanceledException => "작업을 취소했습니다.",
            HttpRequestException => $"네트워크 오류: {exception.Message}",
            _ => exception.Message,
        };

    /// <summary>
    /// Runs <paramref name="work"/> with a fresh cancellation scope and busy state. Failures are
    /// turned into <see cref="Message"/> rather than propagating: these methods are invoked from
    /// <c>async void</c> command handlers where an escaping exception would kill the process.
    /// </summary>
    protected async Task RunAsync(Func<CancellationToken, Task> work)
    {
        using var operation = new CancellationTokenSource();
        _operation = operation;
        Busy = true;
        RaiseOperationState();
        try
        {
            await work(operation.Token);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            _operation = null;
            Busy = false;
            Progress = 0;
            RaiseOperationState();
        }
    }

    private void RaiseOperationState()
    {
        RaisePropertyChanged(nameof(CanCancel));
        CancelCommand.NotifyCanExecuteChanged();
        OnBusyChanged();
    }

    /// <summary>Lets derived tabs refresh their own command states when busy toggles.</summary>
    protected virtual void OnBusyChanged() { }

    /// <summary>Turns an options-file edit result into something the user can act on.</summary>
    protected static string DescribeOptionsResult(OptionsEditStatus status, string appliedMessage) => status switch
    {
        OptionsEditStatus.Applied => appliedMessage,
        OptionsEditStatus.MissingFile =>
            "게임 설정 파일이 아직 없습니다. 게임을 한 번 실행한 뒤 다시 적용해 주세요.",
        OptionsEditStatus.GameRunning =>
            "게임이 실행 중이라 설정을 적용할 수 없습니다. 게임을 종료한 뒤 다시 시도해 주세요.",
        _ => "알 수 없는 결과입니다.",
    };

    public virtual void Dispose()
    {
        _operation?.Cancel();
        GC.SuppressFinalize(this);
    }
}

/// <summary>A manifest module as shown in the mod list, with its user-toggleable state.</summary>
public sealed class ModuleListItem(DistroModule module, int depth, bool isEnabled) : ViewModelBase
{
    private bool _isEnabled = isEnabled;
    private bool _isEffectivelyEnabled = true;

    public DistroModule Module { get; } = module;
    public string Id => Module.Id;
    public string DisplayName => new string(' ', Depth * 4) + Path.GetFileName(Module.Path);
    public int Depth { get; } = depth;
    public bool IsOptional => ModuleSelection.IsOptional(Module);
    public string TypeLabel => Module.Type switch
    {
        ModuleType.RequiredMod => "필수",
        ModuleType.OptionalMod => "선택",
        ModuleType.DropInMod => "드롭인",
        ModuleType.ShaderPack => "셰이더",
        ModuleType.ResourcePack => "리소스팩",
        _ => string.Empty,
    };

    /// <summary>The user's own choice. Only meaningful when <see cref="IsOptional"/>.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (SetProperty(ref _isEnabled, value)) EnabledChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>False when an ancestor is disabled, which forces this module off regardless of choice.</summary>
    public bool IsEffectivelyEnabled { get => _isEffectivelyEnabled; set => SetProperty(ref _isEffectivelyEnabled, value); }

    public event EventHandler? EnabledChanged;
}

/// <summary>A user-supplied jar in the mods folder.</summary>
public sealed record DropInModItem(string FileName, bool IsServerManaged);

public sealed class ModuleManagementViewModel : ContentTabViewModel
{
    private readonly IContentUpdateService _content;
    private readonly DropInModManager _dropIns;
    private readonly ModuleSelectionStore _settings;
    private ServerDistribution? _distribution;
    private DropInModItem? _selectedDropIn;
    private bool _suppressToggle;

    /// <summary>Reads and persists the disabled-module set without coupling this VM to the settings file.</summary>
    public sealed record ModuleSelectionStore(Func<List<string>> GetDisabled, Func<CancellationToken, Task> SaveAsync);

    public ModuleManagementViewModel(
        IContentUpdateService content,
        DropInModManager dropIns,
        ModuleSelectionStore settings)
    {
        _content = content;
        _dropIns = dropIns;
        _settings = settings;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !Busy, ReportError);
        UpdateCommand = new AsyncCommand(UpdateAsync, () => !Busy && _distribution is not null, ReportError);
        AddDropInCommand = new AsyncCommand(AddDropInAsync, () => !Busy && FilePicker is not null, ReportError);
        DeleteDropInCommand = new AsyncCommand(DeleteDropInAsync,
            () => !Busy && _selectedDropIn is { IsServerManaged: false }, ReportError);
    }

    public ObservableCollection<ModuleListItem> Modules { get; } = [];
    public ObservableCollection<DropInModItem> DropIns { get; } = [];

    // Empty-state placeholders: an empty box with no explanation reads as a broken screen.
    public bool ModulesEmpty => Modules.Count == 0;
    public bool DropInsEmpty => DropIns.Count == 0;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand UpdateCommand { get; }
    public AsyncCommand AddDropInCommand { get; }
    public AsyncCommand DeleteDropInCommand { get; }

    /// <summary>Supplied by the view; returns the chosen jar path or null when cancelled.</summary>
    public Func<Task<string?>>? FilePicker { get; set; }

    public DropInModItem? SelectedDropIn
    {
        get => _selectedDropIn;
        set { if (SetProperty(ref _selectedDropIn, value)) DeleteDropInCommand.NotifyCanExecuteChanged(); }
    }

    public async Task RefreshAsync() => await RunAsync(async token =>
    {
        Message = "매니페스트를 확인하는 중...";
        _distribution = await _content.FetchAsync(token);
        BuildModuleList();
        await RefreshDropInsAsync(token);
        Message = $"모듈 {Modules.Count}개를 확인했습니다.";
    });

    public async Task UpdateAsync() => await RunAsync(async token =>
    {
        if (_distribution is null)
        {
            Message = "먼저 매니페스트를 확인해 주세요.";
            return;
        }

        Message = "콘텐츠를 동기화하는 중...";
        var selected = ModuleSelection.Filter(_distribution, _settings.GetDisabled());
        var result = await _content.UpdateAsync(selected, token);
        Message = result.Status switch
        {
            ModuleUpdateStatus.Updated => $"모듈 {result.UpdatedModuleIds.Count}개를 갱신했습니다.",
            ModuleUpdateStatus.UpToDate => "이미 최신 상태입니다.",
            ModuleUpdateStatus.Cancelled => "동기화를 취소했습니다.",
            ModuleUpdateStatus.Failed => $"동기화에 실패했습니다: {result.Error}",
            _ => "알 수 없는 동기화 결과입니다.",
        };
        if (result.Status is ModuleUpdateStatus.Updated or ModuleUpdateStatus.UpToDate)
            await _content.SaveCacheAsync(selected, token);
        await RefreshDropInsAsync(token);
    });

    private async Task AddDropInAsync() => await RunAsync(async token =>
    {
        if (FilePicker is null) return;
        var path = await FilePicker();
        if (string.IsNullOrWhiteSpace(path)) return;
        var added = await _dropIns.AddAsync(path, token);
        Message = $"'{added.FileName}'을(를) 추가했습니다.";
        await RefreshDropInsAsync(token);
    });

    private async Task DeleteDropInAsync() => await RunAsync(async token =>
    {
        if (SelectedDropIn is null) return;
        var name = SelectedDropIn.FileName;
        await _dropIns.DeleteAsync(name, token);
        Message = $"'{name}'을(를) 삭제했습니다.";
        await RefreshDropInsAsync(token);
    });

    private async Task RefreshDropInsAsync(CancellationToken token)
    {
        var selectedName = SelectedDropIn?.FileName;
        var scanned = await _dropIns.ScanAsync(token);
        // The scanner only sees files; the manifest is what tells us which jars the server owns.
        // Without this, a required mod would appear deletable in the drop-in list.
        var serverOwned = ServerOwnedModFileNames();
        DropIns.Clear();
        foreach (var file in scanned)
            DropIns.Add(new DropInModItem(file.FileName, file.IsServerManaged || serverOwned.Contains(file.FileName)));
        SelectedDropIn = DropIns.FirstOrDefault(item => item.FileName == selectedName);
        RaisePropertyChanged(nameof(DropInsEmpty));
    }

    private HashSet<string> ServerOwnedModFileNames()
    {
        var owned = new HashSet<string>(ModuleValidation.PathComparer);
        if (_distribution is null) return owned;
        foreach (var module in _distribution.Modules.Where(module => module.IsServerManaged))
        {
            var name = Path.GetFileName(module.Path.Replace('\\', '/'));
            if (!string.IsNullOrEmpty(name)) owned.Add(name);
        }
        return owned;
    }

    /// <summary>Flattens the parent/child manifest into an indented, ordered list.</summary>
    private void BuildModuleList()
    {
        foreach (var existing in Modules) existing.EnabledChanged -= OnModuleEnabledChanged;
        Modules.Clear();
        if (_distribution is null) return;
        var disabled = _settings.GetDisabled();
        var byParent = _distribution.Modules
            .GroupBy(module => module.ParentId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        void AddChildren(string parentId, int depth)
        {
            if (!byParent.TryGetValue(parentId, out var children)) return;
            var ordered = children
                .OrderBy(module => module.Type)
                .ThenBy(module => module.LoadOrder ?? int.MaxValue)
                .ThenBy(module => module.Id, StringComparer.Ordinal);
            foreach (var module in ordered)
            {
                var item = new ModuleListItem(module, depth, !disabled.Contains(module.Id));
                item.EnabledChanged += OnModuleEnabledChanged;
                Modules.Add(item);
                AddChildren(module.Id, depth + 1);
            }
        }

        AddChildren(string.Empty, 0);
        RecalculateEffectiveState();
        RaisePropertyChanged(nameof(ModulesEmpty));
    }

    private async void OnModuleEnabledChanged(object? sender, EventArgs e)
    {
        if (_suppressToggle || sender is not ModuleListItem item) return;
        try
        {
            var disabled = _settings.GetDisabled();
            if (item.IsEnabled) disabled.Remove(item.Id);
            else if (!disabled.Contains(item.Id)) disabled.Add(item.Id);
            RecalculateEffectiveState();
            await _settings.SaveAsync(CancellationToken.None);
            Message = "선택형 모듈 설정을 저장했습니다. '업데이트 적용'을 눌러 반영하세요.";
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    /// <summary>Greys out modules whose ancestor is switched off.</summary>
    private void RecalculateEffectiveState()
    {
        if (_distribution is null) return;
        var effective = ModuleSelection.ResolveDisabled(_distribution, _settings.GetDisabled());
        _suppressToggle = true;
        try
        {
            foreach (var item in Modules)
            {
                item.IsEffectivelyEnabled = !effective.Contains(item.Id);
                if (item.IsOptional && !item.IsEffectivelyEnabled) item.IsEnabled = false;
            }
        }
        finally { _suppressToggle = false; }
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        AddDropInCommand.NotifyCanExecuteChanged();
        DeleteDropInCommand.NotifyCanExecuteChanged();
    }

    public override void Dispose()
    {
        foreach (var item in Modules) item.EnabledChanged -= OnModuleEnabledChanged;
        base.Dispose();
    }
}

public sealed class ShaderPackViewModel : ContentTabViewModel
{
    private readonly ShaderPackManager _manager;
    private ShaderPackInfo? _selectedPack;

    public ShaderPackViewModel(ShaderPackManager manager)
    {
        _manager = manager;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !Busy, ReportError);
        ApplyCommand = new AsyncCommand(ApplyAsync, () => !Busy, ReportError);
        AddCommand = new AsyncCommand(AddAsync, () => !Busy && FilePicker is not null, ReportError);
        DeleteCommand = new AsyncCommand(DeleteAsync,
            () => !Busy && _selectedPack is { IsServerManaged: false }, ReportError);
    }

    public ObservableCollection<ShaderPackInfo> Packs { get; } = [];
    public bool IsEmpty => Packs.Count == 0;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public Func<Task<string?>>? FilePicker { get; set; }

    public ShaderPackInfo? SelectedPack
    {
        get => _selectedPack;
        set { if (SetProperty(ref _selectedPack, value)) DeleteCommand.NotifyCanExecuteChanged(); }
    }

    public async Task RefreshAsync() => await RunAsync(async token =>
    {
        await ReloadAsync(token);
        if (Packs.Count == 0) Message = "셰이더팩 폴더가 비어 있습니다.";
    });

    public async Task ApplyAsync() => await RunAsync(async token =>
    {
        _manager.SetSelected(SelectedPack?.FileName);
        var status = await _manager.ApplyAsync(token);
        Message = DescribeOptionsResult(status,
            SelectedPack is null ? "셰이더를 껐습니다." : $"'{SelectedPack.FileName}'을(를) 적용했습니다.");
        await ReloadAsync(token);
    });

    private async Task AddAsync() => await RunAsync(async token =>
    {
        if (FilePicker is null) return;
        var path = await FilePicker();
        if (string.IsNullOrWhiteSpace(path)) return;
        var added = await _manager.AddAsync(path, token);
        Message = $"'{added.FileName}'을(를) 추가했습니다.";
        await ReloadAsync(token);
    });

    private async Task DeleteAsync() => await RunAsync(async token =>
    {
        if (SelectedPack is null) return;
        var name = SelectedPack.FileName;
        await _manager.DeleteAsync(name, token);
        Message = $"'{name}'을(를) 삭제했습니다.";
        await ReloadAsync(token);
    });

    private async Task ReloadAsync(CancellationToken token)
    {
        var selectedName = SelectedPack?.FileName;
        Packs.Clear();
        foreach (var pack in await _manager.GetPacksAsync(token)) Packs.Add(pack);
        SelectedPack = Packs.FirstOrDefault(pack => pack.FileName == selectedName)
            ?? Packs.FirstOrDefault(pack => pack.IsSelected);
        RaisePropertyChanged(nameof(IsEmpty));
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ResourcePackViewModel : ContentTabViewModel
{
    private readonly ResourcePackManager _manager;
    private ResourcePackItem? _selectedPack;
    private bool _suppressToggle;

    public ResourcePackViewModel(ResourcePackManager manager)
    {
        _manager = manager;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !Busy, ReportError);
        ApplyCommand = new AsyncCommand(ApplyAsync, () => !Busy, ReportError);
        AddCommand = new AsyncCommand(AddAsync, () => !Busy && FilePicker is not null, ReportError);
        DeleteCommand = new AsyncCommand(DeleteAsync,
            () => !Busy && _selectedPack is { IsServerManaged: false }, ReportError);
        MoveUpCommand = new AsyncCommand(() => MoveAsync(-1), () => !Busy && _selectedPack is not null, ReportError);
        MoveDownCommand = new AsyncCommand(() => MoveAsync(1), () => !Busy && _selectedPack is not null, ReportError);
    }

    public ObservableCollection<ResourcePackItem> Packs { get; } = [];
    public bool IsEmpty => Packs.Count == 0;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ApplyCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand MoveUpCommand { get; }
    public AsyncCommand MoveDownCommand { get; }
    public Func<Task<string?>>? FilePicker { get; set; }

    public ResourcePackItem? SelectedPack
    {
        get => _selectedPack;
        set
        {
            if (!SetProperty(ref _selectedPack, value)) return;
            DeleteCommand.NotifyCanExecuteChanged();
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task RefreshAsync() => await RunAsync(async token =>
    {
        await _manager.RefreshAsync(token);
        Reload();
        if (Packs.Count == 0) Message = "리소스팩 폴더가 비어 있습니다.";
    });

    public async Task ApplyAsync() => await RunAsync(async token =>
    {
        var status = await _manager.ApplyAsync(token);
        Message = DescribeOptionsResult(status, "리소스팩 목록을 적용했습니다.");
    });

    /// <summary>Moves the selected pack by <paramref name="offset"/> positions.</summary>
    public Task MoveAsync(int offset)
    {
        if (SelectedPack is null) return Task.CompletedTask;
        return MoveToAsync(SelectedPack.Id, SelectedPack.Order + offset);
    }

    /// <summary>Highlights the row the dragged pack would land above. Pass null to clear.</summary>
    public void SetDropTarget(int? index)
    {
        for (var position = 0; position < Packs.Count; position++)
            Packs[position].IsDropTarget = index == position;
    }

    /// <summary>Drag-and-drop entry point: moves <paramref name="id"/> to an absolute index.</summary>
    public Task MoveToAsync(string id, int targetIndex)
    {
        try
        {
            _manager.Reorder(id, targetIndex);
            Reload();
            SelectedPack = Packs.FirstOrDefault(pack => pack.Id == id);
            Message = "순서를 변경했습니다. '적용'을 눌러 저장하세요.";
        }
        catch (InvalidOperationException exception)
        {
            // Required server packs are pinned to the top; surface that instead of faulting.
            Message = exception.Message;
        }
        catch (KeyNotFoundException)
        {
            Message = "선택한 리소스팩을 찾을 수 없습니다. 새로 고침해 주세요.";
        }
        return Task.CompletedTask;
    }

    public void SetEnabled(string id, bool enabled)
    {
        if (_suppressToggle) return;
        try
        {
            _manager.SetEnabled(id, enabled);
            Message = "'적용'을 눌러 변경 사항을 저장하세요.";
        }
        catch (InvalidOperationException exception)
        {
            Message = exception.Message;
            RevertToggle(id, !enabled);
        }
        catch (KeyNotFoundException)
        {
            Message = "선택한 리소스팩을 찾을 수 없습니다. 새로 고침해 주세요.";
            RevertToggle(id, !enabled);
        }
    }

    private void RevertToggle(string id, bool value)
    {
        _suppressToggle = true;
        try
        {
            var item = Packs.FirstOrDefault(pack => pack.Id == id);
            if (item is not null) item.IsEnabled = value;
        }
        finally { _suppressToggle = false; }
    }

    private async Task AddAsync() => await RunAsync(async token =>
    {
        if (FilePicker is null) return;
        var path = await FilePicker();
        if (string.IsNullOrWhiteSpace(path)) return;
        var added = await _manager.AddAsync(path, token);
        Message = $"'{added.FileName}'을(를) 추가했습니다.";
        await _manager.RefreshAsync(token);
        Reload();
    });

    private async Task DeleteAsync() => await RunAsync(async token =>
    {
        if (SelectedPack is null) return;
        var name = SelectedPack.FileName;
        await _manager.DeleteAsync(name, token);
        Message = $"'{name}'을(를) 삭제했습니다.";
        await _manager.RefreshAsync(token);
        Reload();
    });

    private void Reload()
    {
        var selectedId = SelectedPack?.Id;
        foreach (var item in Packs) item.EnabledChanged -= OnItemEnabledChanged;
        Packs.Clear();
        foreach (var pack in _manager.Packs)
        {
            var item = new ResourcePackItem(pack);
            item.EnabledChanged += OnItemEnabledChanged;
            Packs.Add(item);
        }
        SelectedPack = Packs.FirstOrDefault(pack => pack.Id == selectedId);
        RaisePropertyChanged(nameof(IsEmpty));
    }

    private void OnItemEnabledChanged(object? sender, EventArgs e)
    {
        if (sender is ResourcePackItem item) SetEnabled(item.Id, item.IsEnabled);
    }

    protected override void OnBusyChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        AddCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    public override void Dispose()
    {
        foreach (var item in Packs) item.EnabledChanged -= OnItemEnabledChanged;
        base.Dispose();
    }
}

/// <summary>Bindable wrapper around <see cref="ResourcePackInfo"/> so checkbox edits raise events.</summary>
public sealed class ResourcePackItem(ResourcePackInfo info) : ViewModelBase
{
    private bool _isDropTarget;

    public ResourcePackInfo Info { get; } = info;
    public string Id => Info.Id;
    public string FileName => Info.FileName;
    public bool IsServerManaged => Info.IsServerManaged;
    public bool CanDisable => !Info.IsServerManaged;
    public int Order => Info.Order;

    /// <summary>Drives the insertion line drawn above this row while dragging.</summary>
    public bool IsDropTarget { get => _isDropTarget; set => SetProperty(ref _isDropTarget, value); }

    public bool IsEnabled
    {
        get => Info.IsEnabled;
        set
        {
            if (Info.IsEnabled == value) return;
            Info.IsEnabled = value;
            RaisePropertyChanged();
            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? EnabledChanged;
}
