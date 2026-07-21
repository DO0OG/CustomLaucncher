namespace CustomLauncher.Core;

/// <summary>
/// Manages user-supplied mod jars in the game's mods folder. Server-managed jars are listed but
/// cannot be deleted here; <see cref="ModuleManager"/> owns those through its managed-state index.
/// </summary>
public sealed class DropInModManager(string modsDirectory, IEnumerable<string>? serverManagedFiles = null)
    : DropInPackManagerBase(modsDirectory, serverManagedFiles)
{
    protected override bool IsSupportedFile(string path) =>
        string.Equals(Path.GetExtension(path), ".jar", StringComparison.OrdinalIgnoreCase);
}
