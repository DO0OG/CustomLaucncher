namespace CustomLauncher.Shared.Models;

public sealed class ServerEntry
{
    /// <summary>Lowercase kebab-case id; also the key for per-server user settings.</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = ServerCatalog.DefaultPort;
    public string MinecraftVersion { get; set; } = string.Empty;
    public ModLoaderSpec ModLoader { get; set; } = new();
    public JavaRequirement Java { get; set; } = new();
    public List<DistroModule> Modules { get; set; } = [];
    public DiscordSpec? Discord { get; set; }
    public string? StatusUrl { get; set; }
}

public sealed class ModLoaderSpec
{
    public string Type { get; set; } = "vanilla";
    public string Version { get; set; } = string.Empty;
}

public sealed class DiscordSpec
{
    public string? Details { get; set; }
    public string? State { get; set; }
    public string? LargeImageKey { get; set; }
}
