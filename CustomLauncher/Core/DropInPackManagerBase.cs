namespace CustomLauncher.Core;

public sealed record PackFile(string Id, string FileName, bool IsServerManaged);

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
                return new PackFile(name, name, _serverManagedFiles.Contains(name));
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
        if (_serverManagedFiles.Contains(fileName))
            throw new InvalidOperationException("Server-managed content packs cannot be removed.");
        var target = ModuleValidation.ResolvePath(Directory, fileName);
        if (File.Exists(target))
            File.Delete(target);
        return Task.CompletedTask;
    }

    protected abstract bool IsSupportedFile(string path);
}
