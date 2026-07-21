using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

public sealed class ShaderPackManager : DropInPackManagerBase
{
    private readonly string _settingsFile;
    private readonly string _settingsKey;
    private readonly OptionsTextEditor _editor;
    private string? _selectedFileName;

    public ShaderPackManager(
        string shaderPackDirectory,
        string settingsFile,
        string settingsKey,
        OptionsTextEditor editor,
        IEnumerable<string>? serverManagedFiles = null)
        : base(shaderPackDirectory, serverManagedFiles)
    {
        _settingsFile = settingsFile;
        _settingsKey = settingsKey;
        _editor = editor;
    }

    public void SetSelected(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            _selectedFileName = null;
            return;
        }
        var normalized = ModuleValidation.NormalizeManifestPath(fileName);
        if (normalized.Contains('/'))
            throw new InvalidDataException("A shader pack selection must be a file name.");
        _selectedFileName = normalized;
    }

    public async Task<IReadOnlyList<ShaderPackInfo>> GetPacksAsync(CancellationToken cancellationToken = default)
    {
        var packs = await ScanAsync(cancellationToken);
        if (_selectedFileName is not null && packs.All(pack => !ModuleValidation.PathComparer.Equals(pack.FileName, _selectedFileName)))
            _selectedFileName = null;
        return packs.Select(pack => new ShaderPackInfo
        {
            Id = pack.Id,
            FileName = pack.FileName,
            IsServerManaged = pack.IsServerManaged,
            IsSelected = _selectedFileName is not null && ModuleValidation.PathComparer.Equals(pack.FileName, _selectedFileName),
        }).ToList();
    }

    public async Task<OptionsEditStatus> ApplyAsync(CancellationToken cancellationToken = default)
    {
        await GetPacksAsync(cancellationToken);
        return await _editor.SetValueAsync(_settingsFile, _settingsKey, _selectedFileName ?? "OFF", cancellationToken);
    }

    protected override bool IsSupportedFile(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);
}
