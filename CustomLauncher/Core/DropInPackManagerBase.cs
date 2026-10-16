namespace CustomLauncher.Core;

public sealed record PackFile(string Id, string FileName, bool IsServerManaged, bool IsEnabled = true)
{
    public const string DisabledSuffix = ".disabled";

    /// <summary>Returns the enabled file name: "foo.jar.disabled" becomes "foo.jar".</summary>
    public static string EnabledNameOf(string fileName) =>
        fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^DisabledSuffix.Length]
            : fileName;
}

public abstract class DropInPackManagerBase
{
    private readonly HashSet<string> _serverManagedFiles;

    protected DropInPackManagerBase(string directory, IEnumerable<string>? serverManagedFiles = null)
    {
        Directory = Path.GetFullPath(directory);
        _serverManagedFiles = (serverManagedFiles ?? [])
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(ModuleValidation.PathComparer);
    }

    protected string Directory { get; }

    public virtual Task<IReadOnlyList<PackFile>> ScanAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<PackFile>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!System.IO.Directory.Exists(Directory))
            return [];
        return System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedFile)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                var enabledName = PackFile.EnabledNameOf(name);
                return new PackFile(name, name, _serverManagedFiles.Contains(enabledName), enabledName == name);
            })
            .OrderBy(pack => pack.FileName, ModuleValidation.PathComparer)
            .ToList();
    }, cancellationToken);

    public async Task<PackFile> AddAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source) || !IsSupportedFile(source))
            throw new InvalidDataException("The selected file is not a supported content pack.");
        System.IO.Directory.CreateDirectory(Directory);
        var fileName = Path.GetFileName(source);
        var target = ModuleValidation.ResolvePath(Directory, fileName);
        if (File.Exists(target))
            throw new IOException($"A content pack named '{fileName}' already exists.");

        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return new PackFile(fileName, fileName, false);
    }

    public Task DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        fileName = ModuleValidation.NormalizeManifestPath(fileName);
        if (fileName.Contains('/'))
            throw new InvalidDataException("A content pack deletion must specify a file name.");
        if (_serverManagedFiles.Contains(PackFile.EnabledNameOf(fileName)))
            throw new InvalidOperationException("Server-managed content packs cannot be removed.");
        var target = ModuleValidation.ResolvePath(Directory, fileName);
        if (File.Exists(target))
            File.Delete(target);
        return Task.CompletedTask;
    }

    /// <summary>Enables or disables a pack by renaming it with the '.disabled' suffix.</summary>
    public Task<PackFile> SetEnabledAsync(string fileName, bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        fileName = ModuleValidation.NormalizeManifestPath(fileName);
        if (fileName.Contains('/'))
            throw new InvalidDataException("A content pack must be specified by file name.");
        var enabledName = PackFile.EnabledNameOf(fileName);
        if (_serverManagedFiles.Contains(enabledName))
            throw new InvalidOperationException("Server-managed content packs cannot be disabled.");

        var targetName = enabled ? enabledName : enabledName + PackFile.DisabledSuffix;
        var source = ModuleValidation.ResolvePath(Directory, fileName);
        var target = ModuleValidation.ResolvePath(Directory, targetName);
        if (!File.Exists(source))
            throw new FileNotFoundException("The content pack was not found.", fileName);
        if (source != target)
        {
            if (File.Exists(target))
                throw new IOException($"A content pack named '{targetName}' already exists.");
            File.Move(source, target);
        }
        return Task.FromResult(new PackFile(targetName, targetName, false, enabled));
    }

    protected abstract bool IsSupportedFile(string path);
}
