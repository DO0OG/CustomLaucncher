namespace CustomLauncher.Core;

public enum ServerListStatus { Added, Updated, AlreadyPresent, GameRunning, Failed }

public interface IServerListWriter
{
    ServerListStatus Register(string gameDirectory, string name, string address);
}

/// <summary>
/// Pre-registers the launcher's server in the player's multiplayer list.
/// <para>
/// The launcher joins the server directly on start, but once a player disconnects to the main menu
/// the server has to be in <c>servers.dat</c> for them to get back in - otherwise they need the
/// address, which the launcher deliberately no longer shows.
/// </para>
/// </summary>
public sealed class ServerListWriter(Func<bool>? isGameRunning = null) : IServerListWriter
{
    private const string FileName = "servers.dat";
    private readonly Func<bool> _isGameRunning = isGameRunning ?? (() => false);

    public ServerListStatus Register(string gameDirectory, string name, string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return ServerListStatus.Failed;
        // Minecraft rewrites servers.dat wholesale when it exits, so editing it under a running
        // game would lose either our entry or whatever the player did in that session.
        if (_isGameRunning()) return ServerListStatus.GameRunning;

        var path = Path.Combine(gameDirectory, FileName);
        try
        {
            var root = ReadOrCreate(path);
            if (root.TryGetValue("servers", out var existing) && existing is not NbtList)
                return ServerListStatus.Failed;
            if (!root.TryGetValue("servers", out _))
                root["servers"] = new NbtList(NbtTagType.Compound);
            var servers = (NbtList)root["servers"];

            var entry = servers.OfType<NbtCompound>().FirstOrDefault(server =>
                AddressesMatch(server.GetString("ip"), address));

            ServerListStatus status;
            if (entry is null)
            {
                // Put ours first: it is the reason this launcher exists.
                servers.Insert(0, new NbtCompound { ["name"] = name, ["ip"] = address });
                servers.ElementType = NbtTagType.Compound;
                status = ServerListStatus.Added;
            }
            else if (string.Equals(entry.GetString("name"), name, StringComparison.Ordinal))
            {
                return ServerListStatus.AlreadyPresent;
            }
            else
            {
                // Keep the player's ordering and any icon they cached; only refresh the name.
                entry["name"] = name;
                status = ServerListStatus.Updated;
            }

            Save(path, root);
            return status;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
                                          or UnauthorizedAccessException or EndOfStreamException)
        {
            // A broken or unreadable server list must never stop someone from playing.
            return ServerListStatus.Failed;
        }
    }

    private static NbtCompound ReadOrCreate(string path)
    {
        if (!File.Exists(path)) return [];
        using var stream = File.OpenRead(path);
        return Nbt.Read(stream);
    }

    private static void Save(string path, NbtCompound root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backup = path + ".bak";
        if (File.Exists(path) && !File.Exists(backup))
            File.Copy(path, backup);

        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                Nbt.Write(stream, root);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>
    /// Minecraft stores the address as typed, so "host" and "host:25565" are the same server and
    /// must not produce a duplicate entry.
    /// </summary>
    private static bool AddressesMatch(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? address)
    {
        var value = (address ?? string.Empty).Trim();
        return value.EndsWith(":25565", StringComparison.Ordinal)
            ? value[..^6]
            : value;
    }
}
