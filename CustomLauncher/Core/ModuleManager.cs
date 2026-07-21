using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomLauncher.Shared.Models;
using SharpCompress.Archives;

namespace CustomLauncher.Core;

public enum ModuleUpdateStatus { UpToDate, Updated, Cancelled, Failed }

public sealed record ModuleUpdateResult(ModuleUpdateStatus Status, IReadOnlyList<string> UpdatedModuleIds, string? Error = null);

public sealed class ModuleManagerOptions
{
    public int MaxArchiveEntries { get; init; } = 10_000;
    public long MaxArchiveBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public long MaxSingleEntryBytes { get; init; } = 1024L * 1024 * 1024;
    public long MaxDownloadBytes { get; init; } = 4L * 1024 * 1024 * 1024;
}

public sealed class ModuleManager
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient;
    private readonly string _gameDirectory;
    private readonly ManagedModuleStateStore _stateStore;
    private readonly ModuleManagerOptions _options;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public ModuleManager(HttpClient httpClient, string gameDirectory, ModuleManagerOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _gameDirectory = Path.GetFullPath(gameDirectory);
        _stateStore = new ManagedModuleStateStore(_gameDirectory);
        _options = options ?? new ModuleManagerOptions();
    }

    public async Task<ServerDistribution> FetchDistributionAsync(string manifestUrl, CancellationToken cancellationToken = default)
    {
        var uri = ModuleValidation.ValidateHttpsUri(manifestUrl, "manifest");
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var distribution = await JsonSerializer.DeserializeAsync<ServerDistribution>(stream, ManifestJson, cancellationToken)
            ?? throw new InvalidDataException("The distribution manifest is empty.");
        ModuleValidation.ValidateDistribution(distribution);
        return distribution;
    }

    public async Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken = default)
    {
        await _updateLock.WaitAsync(cancellationToken);
        var updated = new List<string>();
        var stagingRoot = Path.Combine(_gameDirectory, ".launcher-staging", Guid.NewGuid().ToString("N"));
        try
        {
            ModuleValidation.ValidateDistribution(distribution);
            Directory.CreateDirectory(stagingRoot);
            var previous = await _stateStore.LoadAsync(cancellationToken);
            var desired = new ManagedModuleState();
            var stagedFiles = new Dictionary<string, string>(ModuleValidation.PathComparer);
            var preservedUnmanagedPaths = new HashSet<string>(ModuleValidation.PathComparer);

            foreach (var unmanaged in distribution.Modules.Where(module => !module.IsServerManaged))
            {
                if (previous.Modules.TryGetValue(unmanaged.Id, out var formerlyManaged))
                    preservedUnmanagedPaths.UnionWith(formerlyManaged.Paths);
            }

            foreach (var module in distribution.Modules.Where(module => module.IsServerManaged))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (previous.Modules.TryGetValue(module.Id, out var installed) &&
                    await IsInstalledCurrentAsync(module, installed, cancellationToken))
                {
                    desired.Modules[module.Id] = installed;
                    continue;
                }

                var artifact = await DownloadAndVerifyAsync(module, stagingRoot, cancellationToken);
                var record = module.Packaging == ModulePackaging.Archive
                    ? await StageArchiveAsync(module, artifact, stagingRoot, stagedFiles, cancellationToken)
                    : StageFile(module, artifact, stagedFiles);
                desired.Modules[module.Id] = record;
                updated.Add(module.Id);
            }

            if (updated.Count == 0 && StateEquivalent(previous, desired))
                return new ModuleUpdateResult(ModuleUpdateStatus.UpToDate, updated);

            EnsureUniqueOwnedPaths(desired);
            await CommitAsync(previous, desired, stagedFiles, preservedUnmanagedPaths, stagingRoot, cancellationToken);
            return new ModuleUpdateResult(ModuleUpdateStatus.Updated, updated);
        }
        catch (OperationCanceledException)
        {
            return new ModuleUpdateResult(ModuleUpdateStatus.Cancelled, updated);
        }
        catch (Exception exception)
        {
            return new ModuleUpdateResult(ModuleUpdateStatus.Failed, updated, exception.Message);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            _updateLock.Release();
        }
    }

    private async Task<string> DownloadAndVerifyAsync(DistroModule module, string stagingRoot, CancellationToken token)
    {
        var uri = ModuleValidation.ValidateHttpsUri(module.Url, $"module '{module.Id}'");
        var target = Path.Combine(stagingRoot, "downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > _options.MaxDownloadBytes)
            throw new InvalidDataException($"Module '{module.Id}' exceeds the download limit.");

        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await CopyWithLimitAsync(input, output, _options.MaxDownloadBytes, token);
        }

        await using var file = File.OpenRead(target);
        var actual = await SHA256.HashDataAsync(file, token);
        if (!ModuleValidation.HashEquals(module.Hash, actual))
            throw new InvalidDataException($"Downloaded hash does not match module '{module.Id}'.");
        return target;
    }

    private ManagedModuleRecord StageFile(DistroModule module, string artifact, IDictionary<string, string> stagedFiles)
    {
        var relative = ModuleValidation.NormalizeManifestPath(module.Path);
        var final = ModuleValidation.ResolvePath(_gameDirectory, relative);
        ModuleValidation.EnsureNoLinksInExistingParents(_gameDirectory, final);
        stagedFiles.Add(relative, artifact);
        return new ManagedModuleRecord { Hash = module.Hash, Paths = [relative] };
    }

    private async Task<ManagedModuleRecord> StageArchiveAsync(
        DistroModule module,
        string artifact,
        string stagingRoot,
        IDictionary<string, string> stagedFiles,
        CancellationToken token)
    {
        var destinationPrefix = ModuleValidation.NormalizeManifestPath(module.Path);
        var extractRoot = Path.Combine(stagingRoot, "extracted", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);
        using var archive = ArchiveFactory.Open(artifact);
        var entries = archive.Entries.ToList();
        if (entries.Count > _options.MaxArchiveEntries)
            throw new InvalidDataException($"Archive for '{module.Id}' has too many entries.");

        long total = 0;
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var entryKey = entry.Key ?? throw new InvalidDataException($"Archive for '{module.Id}' contains an unnamed entry.");
            if (!string.IsNullOrEmpty(entry.LinkTarget))
                throw new InvalidDataException($"Archive for '{module.Id}' contains a symbolic link.");
            if (entry.IsEncrypted)
                throw new InvalidDataException($"Archive for '{module.Id}' contains an encrypted entry.");
            if (entry.Size < 0 || entry.Size > _options.MaxSingleEntryBytes)
                throw new InvalidDataException($"Archive entry '{entryKey}' exceeds the size limit.");
            total = checked(total + entry.Size);
            if (total > _options.MaxArchiveBytes)
                throw new InvalidDataException($"Archive for '{module.Id}' exceeds the total size limit.");
            if (!entry.IsDirectory)
                ModuleValidation.NormalizeManifestPath(entryKey);
        }

        var paths = new List<string>();
        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            token.ThrowIfCancellationRequested();
            var entryPath = ModuleValidation.NormalizeManifestPath(
                entry.Key ?? throw new InvalidDataException($"Archive for '{module.Id}' contains an unnamed entry."));
            var relative = ModuleValidation.NormalizeManifestPath(destinationPrefix + "/" + entryPath);
            var final = ModuleValidation.ResolvePath(_gameDirectory, relative);
            ModuleValidation.EnsureNoLinksInExistingParents(_gameDirectory, final);
            var staged = ModuleValidation.ResolvePath(extractRoot, entryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            await using var input = entry.OpenEntryStream();
            await using var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await CopyWithLimitAsync(input, output, _options.MaxSingleEntryBytes, token);
            stagedFiles.Add(relative, staged);
            paths.Add(relative);
        }

        return new ManagedModuleRecord { Hash = module.Hash, Paths = paths };
    }

    private async Task CommitAsync(
        ManagedModuleState previous,
        ManagedModuleState desired,
        IReadOnlyDictionary<string, string> stagedFiles,
        IReadOnlySet<string> preservedUnmanagedPaths,
        string stagingRoot,
        CancellationToken token)
    {
        var previouslyOwned = previous.OwnedPaths();
        var desiredPaths = desired.OwnedPaths();
        foreach (var relative in stagedFiles.Keys)
        {
            var final = ModuleValidation.ResolvePath(_gameDirectory, relative);
            if (File.Exists(final) && !previouslyOwned.Contains(relative))
                throw new IOException($"Refusing to overwrite unmanaged file '{relative}'.");
        }

        var backupRoot = Path.Combine(stagingRoot, "backup");
        var movedToBackup = new List<(string Final, string Backup)>();
        var installed = new List<string>();
        try
        {
            var removed = previouslyOwned
                .Except(desiredPaths, ModuleValidation.PathComparer)
                .Except(preservedUnmanagedPaths, ModuleValidation.PathComparer);
            var affected = removed.Union(stagedFiles.Keys, ModuleValidation.PathComparer);
            foreach (var relative in affected)
            {
                token.ThrowIfCancellationRequested();
                var final = ModuleValidation.ResolvePath(_gameDirectory, relative);
                if (!File.Exists(final))
                    continue;
                var backup = ModuleValidation.ResolvePath(backupRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Move(final, backup);
                movedToBackup.Add((final, backup));
            }

            foreach (var pair in stagedFiles)
            {
                token.ThrowIfCancellationRequested();
                var final = ModuleValidation.ResolvePath(_gameDirectory, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(final)!);
                File.Move(pair.Value, final);
                installed.Add(final);
            }
            await _stateStore.SaveAsync(desired, token);
        }
        catch
        {
            foreach (var path in installed.Where(File.Exists))
                File.Delete(path);
            foreach (var (final, backup) in movedToBackup.AsEnumerable().Reverse())
            {
                if (File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(final)!);
                    File.Move(backup, final, true);
                }
            }
            throw;
        }
    }

    private async Task<bool> IsInstalledCurrentAsync(
        DistroModule module,
        ManagedModuleRecord installed,
        CancellationToken token)
    {
        if (!string.Equals(installed.Hash, module.Hash, StringComparison.OrdinalIgnoreCase) || installed.Paths.Count == 0)
            return false;
        foreach (var relative in installed.Paths)
        {
            var path = ModuleValidation.ResolvePath(_gameDirectory, relative);
            ModuleValidation.EnsureNoLinksInExistingParents(_gameDirectory, path);
            if (!File.Exists(path))
                return false;
        }
        if (module.Packaging != ModulePackaging.File || installed.Paths.Count != 1)
            return true;
        await using var stream = File.OpenRead(ModuleValidation.ResolvePath(_gameDirectory, installed.Paths[0]));
        return ModuleValidation.HashEquals(module.Hash, await SHA256.HashDataAsync(stream, token));
    }

    private static void EnsureUniqueOwnedPaths(ManagedModuleState state)
    {
        var owners = new Dictionary<string, string>(ModuleValidation.PathComparer);
        foreach (var module in state.Modules)
            foreach (var path in module.Value.Paths)
            {
                if (!owners.TryAdd(path, module.Key))
                    throw new InvalidDataException($"Modules '{owners[path]}' and '{module.Key}' both own '{path}'.");
            }
    }

    private static async Task CopyWithLimitAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            total = checked(total + read);
            if (total > limit)
                throw new InvalidDataException("Stream exceeds the configured size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    private static bool StateEquivalent(ManagedModuleState left, ManagedModuleState right) =>
        left.Modules.Count == right.Modules.Count && left.Modules.All(pair =>
            right.Modules.TryGetValue(pair.Key, out var other) &&
            string.Equals(pair.Value.Hash, other.Hash, StringComparison.OrdinalIgnoreCase) &&
            pair.Value.Paths.SequenceEqual(other.Paths, ModuleValidation.PathComparer));

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
