using System.Text.Json;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.ManifestTool.Commands;

public static class DiffCommand
{
    public static async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length != 2)
            throw new ArgumentException("diff requires <existing.json> and <candidate.json>.");

        var existing = await ReadAsync(args[0], cancellationToken);
        var candidate = await ReadAsync(args[1], cancellationToken);
        var changes = existing.Servers.Count > 0 || candidate.Servers.Count > 0
            ? CompareServers(existing, candidate)
            : Compare(existing, candidate);

        if (changes.Count == 0)
        {
            Console.WriteLine("No changes.");
            return 0;
        }

        foreach (var change in changes)
            Console.WriteLine(change);
        return 0;
    }

    public static IReadOnlyList<string> Compare(ServerDistribution existing, ServerDistribution candidate)
    {
        var changes = new List<string>();
        if (!string.Equals(existing.ServerId, candidate.ServerId, StringComparison.Ordinal))
            changes.Add($"~ serverId: {existing.ServerId} -> {candidate.ServerId}");
        if (!string.Equals(existing.DefaultShaderPackId, candidate.DefaultShaderPackId, StringComparison.Ordinal))
            changes.Add($"~ defaultShaderPackId: {Display(existing.DefaultShaderPackId)} -> {Display(candidate.DefaultShaderPackId)}");

        var oldModules = existing.Modules.ToDictionary(ModuleKey, StringComparer.Ordinal);
        var newModules = candidate.Modules.ToDictionary(ModuleKey, StringComparer.Ordinal);

        foreach (var key in newModules.Keys.Except(oldModules.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            changes.Add($"+ {key}");
        foreach (var key in oldModules.Keys.Except(newModules.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            changes.Add($"- {key}");
        foreach (var key in oldModules.Keys.Intersect(newModules.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var details = ModuleChanges(oldModules[key], newModules[key]);
            if (details.Count > 0)
                changes.Add($"~ {key}: {string.Join(", ", details)}");
        }

        if (!JsonEquals(existing.Java, candidate.Java))
            changes.Add("~ java requirement");
        return changes;
    }

    /// <summary>
    /// Compares two manifests server by server, prefixing each line with <c>[server-id]</c>. A
    /// single-server manifest counts as one server, so old and new formats can be compared.
    /// </summary>
    public static IReadOnlyList<string> CompareServers(ServerDistribution existing, ServerDistribution candidate)
    {
        var legacy = new ServerEntry { Id = ServerCatalog.LegacyServerId };
        var oldManifest = ServerCatalog.Normalize(existing, legacy);
        var newManifest = ServerCatalog.Normalize(candidate, legacy);
        var oldServers = oldManifest.Servers.ToDictionary(server => server.Id, StringComparer.Ordinal);
        var newServers = newManifest.Servers.ToDictionary(server => server.Id, StringComparer.Ordinal);

        var changes = new List<string>();
        if (!string.Equals(existing.DefaultShaderPackId, candidate.DefaultShaderPackId, StringComparison.Ordinal))
            changes.Add($"~ defaultShaderPackId: {Display(existing.DefaultShaderPackId)} -> {Display(candidate.DefaultShaderPackId)}");

        foreach (var id in oldServers.Keys.Concat(newServers.Keys.Except(oldServers.Keys, StringComparer.Ordinal)))
        {
            if (!newServers.TryGetValue(id, out var after))
            {
                changes.Add($"[{id}] - server");
                continue;
            }

            if (!oldServers.TryGetValue(id, out var before))
            {
                changes.Add($"[{id}] + server");
                continue;
            }

            if ((before.Name, before.Address, before.Port, before.MinecraftVersion, before.ModLoader.Type,
                    before.ModLoader.Version, before.StatusUrl) !=
                (after.Name, after.Address, after.Port, after.MinecraftVersion, after.ModLoader.Type,
                    after.ModLoader.Version, after.StatusUrl))
                changes.Add($"[{id}] ~ server settings");
            var oldServer = ServerCatalog.ForServer(oldManifest, before);
            var newServer = ServerCatalog.ForServer(newManifest, after);
            // The shader pack is manifest-wide and is reported once above, not once per server.
            oldServer.DefaultShaderPackId = null;
            newServer.DefaultShaderPackId = null;
            changes.AddRange(Compare(oldServer, newServer).Select(change => $"[{id}] {change}"));
        }

        return changes;
    }

    private static async Task<ServerDistribution> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Path.GetFullPath(path));
        return await JsonSerializer.DeserializeAsync<ServerDistribution>(stream, ManifestJson.Options, cancellationToken)
               ?? throw new InvalidDataException($"Manifest is empty: {path}");
    }

    private static string ModuleKey(DistroModule module) =>
        string.IsNullOrWhiteSpace(module.Path) ? module.Id : module.Path.Replace('\\', '/').TrimStart('/');

    private static List<string> ModuleChanges(DistroModule oldModule, DistroModule newModule)
    {
        var changes = new List<string>();
        Add(nameof(DistroModule.Id), oldModule.Id, newModule.Id);
        Add(nameof(DistroModule.Url), oldModule.Url, newModule.Url);
        Add(nameof(DistroModule.Hash), oldModule.Hash, newModule.Hash);
        Add(nameof(DistroModule.Type), oldModule.Type, newModule.Type);
        Add(nameof(DistroModule.Packaging), oldModule.Packaging, newModule.Packaging);
        Add(nameof(DistroModule.ParentId), oldModule.ParentId, newModule.ParentId);
        Add(nameof(DistroModule.LoadOrder), oldModule.LoadOrder, newModule.LoadOrder);
        Add(nameof(DistroModule.IsServerManaged), oldModule.IsServerManaged, newModule.IsServerManaged);
        return changes;

        void Add<T>(string property, T oldValue, T newValue)
        {
            if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
                changes.Add($"{ToCamelCase(property)} {Display(oldValue)} -> {Display(newValue)}");
        }
    }

    private static bool JsonEquals<T>(T left, T right) =>
        JsonSerializer.Serialize(left, ManifestJson.Options) == JsonSerializer.Serialize(right, ManifestJson.Options);

    private static string Display<T>(T value) => value?.ToString() ?? "<null>";

    private static string ToCamelCase(string value) => char.ToLowerInvariant(value[0]) + value[1..];
}
