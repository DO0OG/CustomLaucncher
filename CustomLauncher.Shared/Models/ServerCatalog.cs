using System.Text.RegularExpressions;

namespace CustomLauncher.Shared.Models;

/// <summary>Normalizes and validates the multi-server manifest shape.</summary>
public static class ServerCatalog
{
    public static readonly string LegacyServerId = "default";
    public static readonly int DefaultPort = 25565;
    public static readonly IReadOnlyList<string> LoaderTypes = ["forge", "fabric", "neoforge", "quilt", "vanilla"];

    private static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool IsValidServerId(string? id) => !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id);

    /// <summary>
    /// Returns a copy of the manifest. A manifest without servers is the single-server format:
    /// its top-level modules and Java requirement move into one server built from <paramref name="legacy"/>.
    /// </summary>
    public static ServerDistribution Normalize(ServerDistribution manifest, ServerEntry legacy)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(legacy);
        var servers = manifest.Servers ?? [];
        var modules = manifest.Modules ?? [];
        if (servers.Count == 0)
        {
            servers =
            [
                new ServerEntry
                {
                    Id = legacy.Id,
                    Name = legacy.Name,
                    Address = legacy.Address,
                    Port = legacy.Port,
                    MinecraftVersion = legacy.MinecraftVersion,
                    ModLoader = legacy.ModLoader,
                    Discord = legacy.Discord,
                    StatusUrl = legacy.StatusUrl,
                    Modules = modules,
                    Java = manifest.Java,
                },
            ];
            modules = [];
        }

        return new ServerDistribution
        {
            ServerId = manifest.ServerId,
            Modules = [.. modules],
            Java = manifest.Java,
            DefaultShaderPackId = manifest.DefaultShaderPackId,
            Servers = [.. servers],
        };
    }

    public static void Validate(ServerDistribution normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        if (normalized.Servers.Count == 0)
            throw new InvalidDataException("At least one server is required.");
        if (normalized.Modules.Count > 0)
            throw new InvalidDataException("Top-level modules cannot be combined with a servers list.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < normalized.Servers.Count; index++)
        {
            var server = normalized.Servers[index];
            if (server is null)
                throw new InvalidDataException($"Server entry {index} is null.");
            if (!IsValidServerId(server.Id))
                throw new InvalidDataException($"Invalid server id: '{server.Id}'.");
            if (!ids.Add(server.Id))
                throw new InvalidDataException($"Duplicate server id: '{server.Id}'.");
            if (string.IsNullOrWhiteSpace(server.Name))
                throw new InvalidDataException($"Server '{server.Id}' name is required.");
            if (string.IsNullOrWhiteSpace(server.Address))
                throw new InvalidDataException($"Server '{server.Id}' address is required.");
            if (string.IsNullOrWhiteSpace(server.MinecraftVersion))
                throw new InvalidDataException($"Server '{server.Id}' minecraftVersion is required.");
            if (server.Port is < 1 or > 65535)
                throw new InvalidDataException($"Server '{server.Id}' port must be between 1 and 65535.");
            if (server.ModLoader is null || !LoaderTypes.Contains(server.ModLoader.Type, StringComparer.Ordinal))
                throw new InvalidDataException($"Server '{server.Id}' has an unsupported mod loader type '{server.ModLoader?.Type}'.");
            if (server.ModLoader.Type != "vanilla" && string.IsNullOrWhiteSpace(server.ModLoader.Version))
                throw new InvalidDataException($"Server '{server.Id}' mod loader version is required.");
            if (server.StatusUrl is not null &&
                !(Uri.TryCreate(server.StatusUrl, UriKind.Absolute, out var status) && status.Scheme == Uri.UriSchemeHttps))
                throw new InvalidDataException($"Server '{server.Id}' statusUrl must be an absolute HTTPS URL.");
            if (server.Modules is null)
                throw new InvalidDataException($"Server '{server.Id}' modules are required.");
            if (server.Java is null)
                throw new InvalidDataException($"Server '{server.Id}' java requirement is required.");
        }
    }

    /// <summary>The server with the given id, or the first server when the id is unknown.</summary>
    public static ServerEntry Select(ServerDistribution normalized, string? serverId) =>
        normalized.Servers.FirstOrDefault(server => string.Equals(server.Id, serverId, StringComparison.Ordinal))
            ?? normalized.Servers[0];

    /// <summary>A single-server distribution in the shape the module pipeline consumes.</summary>
    public static ServerDistribution ForServer(ServerDistribution manifest, ServerEntry server) => new()
    {
        ServerId = server.Id,
        Modules = server.Modules,
        Java = server.Java,
        DefaultShaderPackId = manifest.DefaultShaderPackId,
        Servers = [],
    };

    public static string JoinAddress(ServerEntry server) =>
        server.Port == DefaultPort ? server.Address : $"{server.Address}:{server.Port}";
}
