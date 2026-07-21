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
        var changes = Compare(existing, candidate);

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
