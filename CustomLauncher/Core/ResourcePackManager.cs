using System.Text.Json;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

public sealed class ResourcePackManager : DropInPackManagerBase
{
    private readonly string _optionsFile;
    private readonly OptionsTextEditor _editor;
    private readonly List<ResourcePackInfo> _packs = [];

    public ResourcePackManager(
        string resourcePackDirectory,
        string optionsFile,
        OptionsTextEditor editor,
        IEnumerable<string>? serverManagedFiles = null)
        : base(resourcePackDirectory, serverManagedFiles)
    {
        _optionsFile = optionsFile;
        _editor = editor;
    }

    public IReadOnlyList<ResourcePackInfo> Packs => _packs;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var scanned = await ScanAsync(cancellationToken);
        var old = _packs.ToDictionary(pack => pack.FileName, ModuleValidation.PathComparer);
        _packs.Clear();
        foreach (var file in scanned.OrderByDescending(file => file.IsServerManaged))
        {
            old.TryGetValue(file.FileName, out var prior);
            _packs.Add(new ResourcePackInfo
            {
                Id = file.Id,
                FileName = file.FileName,
                IsServerManaged = file.IsServerManaged,
                IsEnabled = prior?.IsEnabled ?? file.IsServerManaged,
                Order = _packs.Count,
            });
        }
    }

    public void SetEnabled(string id, bool enabled)
    {
        var pack = Find(id);
        if (pack.IsServerManaged && !enabled)
            throw new InvalidOperationException("Required server resource packs cannot be disabled.");
        pack.IsEnabled = enabled;
    }

    public void Reorder(string id, int newIndex)
    {
        var pack = Find(id);
        var oldIndex = _packs.IndexOf(pack);
        newIndex = Math.Clamp(newIndex, 0, _packs.Count - 1);
        var requiredBoundary = _packs.TakeWhile(item => item.IsServerManaged).Count();
        if (pack.IsServerManaged && newIndex >= requiredBoundary)
            throw new InvalidOperationException("Required server resource packs must remain at the top.");
        if (!pack.IsServerManaged && newIndex < requiredBoundary)
            newIndex = requiredBoundary;
        _packs.RemoveAt(oldIndex);
        if (newIndex > _packs.Count) newIndex = _packs.Count;
        _packs.Insert(newIndex, pack);
        for (var index = 0; index < _packs.Count; index++)
            _packs[index].Order = index;
    }

    public Task<OptionsEditStatus> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var enabled = _packs.Where(pack => pack.IsEnabled).OrderBy(pack => pack.Order).Select(pack => $"file/{pack.FileName}");
        return _editor.SetValueAsync(_optionsFile, "resourcePacks", JsonSerializer.Serialize(enabled), cancellationToken);
    }

    protected override bool IsSupportedFile(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    private ResourcePackInfo Find(string id) =>
        _packs.FirstOrDefault(pack => string.Equals(pack.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Unknown resource pack '{id}'.");
}
