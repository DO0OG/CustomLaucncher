using System.Text.Json;

namespace CustomLauncher.Core;

public sealed class ManagedModuleState
{
    public Dictionary<string, ManagedModuleRecord> Modules { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> OwnedPaths() => Modules.Values
        .SelectMany(record => record.Paths)
        .ToHashSet(ModuleValidation.PathComparer);
}

public sealed class ManagedModuleRecord
{
    public string Hash { get; set; } = string.Empty;
    public List<string> Paths { get; set; } = [];
}

public sealed class ManagedModuleStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _statePath;

    public ManagedModuleStateStore(string gameDirectory)
    {
        _statePath = Path.Combine(Path.GetFullPath(gameDirectory), ".launcher-managed-modules.json");
    }

    public async Task<ManagedModuleState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
            return new ManagedModuleState();

        await using var stream = File.OpenRead(_statePath);
        return await JsonSerializer.DeserializeAsync<ManagedModuleState>(stream, JsonOptions, cancellationToken)
            ?? new ManagedModuleState();
    }

    public async Task SaveAsync(ManagedModuleState state, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_statePath)!;
        Directory.CreateDirectory(directory);
        var temporary = _statePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            File.Move(temporary, _statePath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
