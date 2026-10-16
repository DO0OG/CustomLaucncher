using CmlLib.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

/// <summary>Bridges the compiled single-server constants and the multi-server manifest.</summary>
public static class LauncherServers
{
    private static readonly string InstancesFolderName = "instances";

    public static ServerEntry Legacy => new()
    {
        Id = ServerCatalog.LegacyServerId,
        Name = LauncherConfig.ServerName,
        Address = LauncherConfig.ServerIp,
        Port = LauncherConfig.ServerPort,
        MinecraftVersion = LauncherConfig.McVersion,
        ModLoader = new ModLoaderSpec
        {
            Type = LauncherConfig.ModLoaderType == "none" ? "vanilla" : LauncherConfig.ModLoaderType.ToLowerInvariant(),
            Version = LauncherConfig.ModLoaderType switch
            {
                "forge" => LauncherConfig.ForgeVersion,
                "fabric" => LauncherConfig.FabricVersion,
                _ => string.Empty,
            },
        },
    };

    /// <summary>
    /// Each server plays in its own folder so mods, options and saves never mix. The legacy server
    /// keeps the install root, which leaves existing installs where they are.
    /// </summary>
    public static string GameDirectoryFor(string root, string serverId)
    {
        if (!ServerCatalog.IsValidServerId(serverId))
            throw new ArgumentException($"Invalid server id: '{serverId}'.", nameof(serverId));
        return Path.GetFullPath(serverId == ServerCatalog.LegacyServerId
            ? root
            : Path.Combine(root, InstancesFolderName, serverId));
    }

    /// <summary>The server's folder as the game directory, with libraries, versions and assets shared from the root.</summary>
    public static MinecraftPath MinecraftPathFor(string root, string serverId)
    {
        var directory = GameDirectoryFor(root, serverId);
        var path = new MinecraftPath(root);
        if (serverId != ServerCatalog.LegacyServerId) path.BasePath = directory;
        return path;
    }

    /// <summary>The single normalization point for every manifest the launcher reads.</summary>
    public static ServerDistribution Normalize(ServerDistribution manifest)
    {
        var normalized = ServerCatalog.Normalize(manifest, Legacy);
        ServerCatalog.Validate(normalized);
        foreach (var server in normalized.Servers)
            ModuleValidation.ValidateDistribution(ServerCatalog.ForServer(normalized, server));
        return normalized;
    }
}
