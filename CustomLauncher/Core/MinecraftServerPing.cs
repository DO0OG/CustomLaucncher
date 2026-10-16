using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CustomLauncher.Models;

namespace CustomLauncher.Core;

/// <summary>Queries a Minecraft server directly with the Server List Ping protocol.</summary>
public static class MinecraftServerPing
{
    private const int MaxPacketBytes = 1_048_576;
    private const int MaxHostBytes = 255;
    private const int MaxVarIntBytes = 5;
    private const int MaxVersionNameLength = 64;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    public static async Task<ServerStatusInfo> QueryAsync(
        string host,
        int port,
        TimeSpan? timeout = null,
        DebugLogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout ?? DefaultTimeout);
            var token = timeoutSource.Token;

            var handshake = BuildHandshake(host, port);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await stream.WriteAsync(handshake, token).ConfigureAwait(false);
            await stream.WriteAsync(new byte[] { 0x01, 0x00 }, token).ConfigureAwait(false);

            var (id, body) = await ReadPacketAsync(stream, token).ConfigureAwait(false);
            if (id != 0x00)
            {
                throw new InvalidDataException($"Unexpected status packet id {id}.");
            }

            var json = await ReadStringAsync(body, token).ConfigureAwait(false);
            var pingMilliseconds = await PingAsync(stream, logger, cancellationToken, token).ConfigureAwait(false);
            return ParseStatus(json, pingMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SocketException or IOException or OperationCanceledException or JsonException
                or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            if (logger is not null)
            {
                await logger.WriteAsync(LauncherLogLevel.Warn, $"Server list ping to {host}:{port} failed", exception)
                    .ConfigureAwait(false);
            }

            return ServerStatusInfo.Unavailable(exception.Message);
        }
    }

    public static ServerStatusInfo ParseStatus(string json, long? pingMilliseconds = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The status response is not a JSON object.");
        }

        int? online = null;
        int? max = null;
        var players = new List<string>();
        if (root.TryGetProperty("players", out var playersElement) && playersElement.ValueKind == JsonValueKind.Object)
        {
            online = ServerStatusChecker.ReadNonNegativeInt(playersElement, "online");
            max = ServerStatusChecker.ReadNonNegativeInt(playersElement, "max");
            if (playersElement.TryGetProperty("sample", out var sample) && sample.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sample.EnumerateArray())
                {
                    if (players.Count >= ServerStatusChecker.MaxDisplayedPlayers)
                    {
                        break;
                    }

                    var name = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var nameElement) &&
                               nameElement.ValueKind == JsonValueKind.String
                        ? ServerStatusChecker.Sanitize(nameElement.GetString(), ServerStatusChecker.MaxPlayerNameLength)
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        players.Add(name);
                    }
                }
            }
        }

        var motd = new StringBuilder();
        if (root.TryGetProperty("description", out var description))
        {
            AppendText(description, motd);
        }

        string? versionName = null;
        if (root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Object &&
            version.TryGetProperty("name", out var versionNameElement) && versionNameElement.ValueKind == JsonValueKind.String)
        {
            versionName = ServerStatusChecker.Sanitize(versionNameElement.GetString(), MaxVersionNameLength);
        }

        return new ServerStatusInfo
        {
            RequestSucceeded = true,
            IsOnline = true,
            OnlinePlayers = online,
            MaxPlayers = max,
            Players = players,
            Motd = ServerStatusChecker.Sanitize(motd.ToString(), ServerStatusChecker.MaxMotdLength),
            VersionName = versionName,
            PingMilliseconds = pingMilliseconds,
        };
    }

    public static byte[] BuildHandshake(string host, int port)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        var hostBytes = Encoding.UTF8.GetBytes(host);
        if (hostBytes.Length > MaxHostBytes)
        {
            throw new ArgumentException("The host name is too long.", nameof(host));
        }

        using var payload = new MemoryStream();
        payload.WriteByte(0x00);
        payload.Write(EncodeVarInt(-1));
        payload.Write(EncodeVarInt(hostBytes.Length));
        payload.Write(hostBytes);
        Span<byte> portBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(portBytes, (ushort)port);
        payload.Write(portBytes);
        payload.Write(EncodeVarInt(1));
        return Frame(payload.ToArray());
    }

    public static byte[] EncodeVarInt(int value)
    {
        var remaining = unchecked((uint)value);
        var bytes = new List<byte>(MaxVarIntBytes);
        do
        {
            var next = (byte)(remaining & 0x7F);
            remaining >>= 7;
            bytes.Add(remaining == 0 ? next : (byte)(next | 0x80));
        }
        while (remaining != 0);

        return [.. bytes];
    }

    public static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1];
        var result = 0;
        for (var index = 0; index < MaxVarIntBytes; index++)
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            result |= (buffer[0] & 0x7F) << (7 * index);
            if ((buffer[0] & 0x80) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException("The VarInt is longer than five bytes.");
    }

    private static byte[] Frame(byte[] payload)
    {
        var length = EncodeVarInt(payload.Length);
        var packet = new byte[length.Length + payload.Length];
        length.CopyTo(packet, 0);
        payload.CopyTo(packet, length.Length);
        return packet;
    }

    private static async Task<(int Id, byte[] Body)> ReadPacketAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = await ReadVarIntAsync(stream, cancellationToken).ConfigureAwait(false);
        if (length < 1 || length > MaxPacketBytes)
        {
            throw new InvalidDataException($"Invalid packet length {length}.");
        }

        var packet = new byte[length];
        await stream.ReadExactlyAsync(packet, cancellationToken).ConfigureAwait(false);
        using var reader = new MemoryStream(packet);
        var id = await ReadVarIntAsync(reader, cancellationToken).ConfigureAwait(false);
        return (id, packet[(int)reader.Position..]);
    }

    private static async Task<string> ReadStringAsync(byte[] body, CancellationToken cancellationToken)
    {
        using var reader = new MemoryStream(body);
        var length = await ReadVarIntAsync(reader, cancellationToken).ConfigureAwait(false);
        if (length < 0 || length > MaxPacketBytes || length != body.Length - reader.Position)
        {
            throw new InvalidDataException($"Invalid string length {length}.");
        }

        return Encoding.UTF8.GetString(body, (int)reader.Position, length);
    }

    private static async Task<long?> PingAsync(
        Stream stream, DebugLogger? logger, CancellationToken callerToken, CancellationToken token)
    {
        try
        {
            var sent = Stopwatch.GetTimestamp();
            var payload = new byte[9];
            payload[0] = 0x01;
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1), sent);
            await stream.WriteAsync(Frame(payload), token).ConfigureAwait(false);
            var (id, body) = await ReadPacketAsync(stream, token).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(sent);
            if (id != 0x01 || body.Length != 8 || BinaryPrimitives.ReadInt64BigEndian(body) != sent)
            {
                throw new InvalidDataException("The pong does not match the ping.");
            }

            return (long)elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or OperationCanceledException)
        {
            if (logger is not null)
            {
                await logger.WriteAsync(LauncherLogLevel.Warn, "Server list pong failed; keeping the status", exception)
                    .ConfigureAwait(false);
            }

            return null;
        }
    }

    private static void AppendText(JsonElement element, StringBuilder builder)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            builder.Append(element.GetString());
            return;
        }

        // 숫자·불리언·null 같은 다른 종류는 MOTD 본문이 아니므로 건너뜁니다.
        if (element.ValueKind != JsonValueKind.Object) return;

        if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            builder.Append(text.GetString());
        }

        if (element.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in extra.EnumerateArray())
            {
                AppendText(part, builder);
            }
        }
    }
}
