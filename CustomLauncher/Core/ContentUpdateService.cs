using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

public interface IContentUpdateService
{
    bool HasManagedState(string serverId);
    Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken);
    Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken);
    Task SaveCacheAsync(ServerDistribution distribution, CancellationToken cancellationToken);
    Task<ServerDistribution?> TryLoadCacheAsync(CancellationToken cancellationToken);
}

public sealed class ContentUpdateService : IContentUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _httpClient;
    private readonly string _root;
    private readonly string _cachePath;
    // One manager per server folder, so each folder keeps its own update lock.
    private readonly ConcurrentDictionary<string, ModuleManager> _managers = new(ModuleValidation.PathComparer);

    public ContentUpdateService(HttpClient httpClient, string gameDirectory)
    {
        _httpClient = httpClient;
        _root = Path.GetFullPath(gameDirectory);
        _cachePath = Path.Combine(_root, ".launcher-distribution-cache.json");
    }

    public bool HasManagedState(string serverId) =>
        File.Exists(Path.Combine(LauncherServers.GameDirectoryFor(_root, serverId), ".launcher-managed-modules.json"));

    private ModuleManager ManagerFor(string serverId) =>
        _managers.GetOrAdd(LauncherServers.GameDirectoryFor(_root, serverId), directory => new ModuleManager(_httpClient, directory));

    public async Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken) =>
        LauncherServers.Normalize(await ManagerFor(ServerCatalog.LegacyServerId)
            .FetchDistributionAsync(LauncherConfig.ManifestUrl, cancellationToken));

    public Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken) =>
        ManagerFor(distribution.ServerId).UpdateAsync(distribution, cancellationToken);

    public async Task SaveCacheAsync(ServerDistribution distribution, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        var temporary = _cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await JsonSerializer.SerializeAsync(stream, distribution, JsonOptions, cancellationToken);
            File.Move(temporary, _cachePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<ServerDistribution?> TryLoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath)) return null;
        try
        {
            await using var stream = File.OpenRead(_cachePath);
            var distribution = await JsonSerializer.DeserializeAsync<ServerDistribution>(stream, JsonOptions, cancellationToken);
            return distribution is null ? null : LauncherServers.Normalize(distribution);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return null;
        }
    }
}
