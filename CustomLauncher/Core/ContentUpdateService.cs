using System.Text.Json;
using System.Text.Json.Serialization;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

public interface IContentUpdateService
{
    bool HasManagedState { get; }
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

    private readonly ModuleManager _manager;
    private readonly string _gameDirectory;
    private readonly string _cachePath;

    public ContentUpdateService(HttpClient httpClient, string gameDirectory)
    {
        _gameDirectory = Path.GetFullPath(gameDirectory);
        _manager = new ModuleManager(httpClient, _gameDirectory);
        _cachePath = Path.Combine(_gameDirectory, ".launcher-distribution-cache.json");
    }

    public bool HasManagedState => File.Exists(Path.Combine(_gameDirectory, ".launcher-managed-modules.json"));

    public Task<ServerDistribution> FetchAsync(CancellationToken cancellationToken) =>
        _manager.FetchDistributionAsync(LauncherConfig.ManifestUrl, cancellationToken);

    public Task<ModuleUpdateResult> UpdateAsync(ServerDistribution distribution, CancellationToken cancellationToken) =>
        _manager.UpdateAsync(distribution, cancellationToken);

    public async Task SaveCacheAsync(ServerDistribution distribution, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_gameDirectory);
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
            if (distribution is not null) ModuleValidation.ValidateDistribution(distribution);
            return distribution;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return null;
        }
    }
}
