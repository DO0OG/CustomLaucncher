using System.Net;
using System.Net.Sockets;
using System.Text;
using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class MinecraftServerPingTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300);

    [Theory]
    [InlineData(0, "00")]
    [InlineData(1, "01")]
    [InlineData(127, "7f")]
    [InlineData(128, "8001")]
    [InlineData(255, "ff01")]
    [InlineData(25565, "ddc701")]
    [InlineData(int.MaxValue, "ffffffff07")]
    [InlineData(-1, "ffffffff0f")]
    public async Task VarInt_EncodesAndDecodes(int value, string hex)
    {
        var encoded = MinecraftServerPing.EncodeVarInt(value);

        Assert.Equal(hex, Convert.ToHexString(encoded).ToLowerInvariant());
        using var stream = new MemoryStream(encoded);
        Assert.Equal(value, await MinecraftServerPing.ReadVarIntAsync(stream));
    }

    [Fact]
    public async Task VarInt_RejectsMoreThanFiveBytes()
    {
        using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]);

        await Assert.ThrowsAsync<InvalidDataException>(() => MinecraftServerPing.ReadVarIntAsync(stream));
    }

    [Fact]
    public void BuildHandshake_WritesHostPortBigEndianAndNextState()
    {
        var packet = MinecraftServerPing.BuildHandshake("example.com", 25565);

        Assert.Equal(
            "15" + "00" + "ffffffff0f" + "0b" + Convert.ToHexString(Encoding.UTF8.GetBytes("example.com")).ToLowerInvariant() + "63dd" + "01",
            Convert.ToHexString(packet).ToLowerInvariant());
    }

    [Fact]
    public void BuildHandshake_RejectsHostsOver255Bytes()
    {
        Assert.Throws<ArgumentException>(() => MinecraftServerPing.BuildHandshake(new string('a', 256), 25565));
    }

    [Fact]
    public async Task QueryAsync_ReadsStatusAndMeasuresPing()
    {
        var port = 0;
        await using var server = new FakeStatusServer(async (stream, token) =>
        {
            Assert.Equal(
                MinecraftServerPing.BuildHandshake("127.0.0.1", port),
                FakeStatusServer.Frame(await FakeStatusServer.ReadPacketAsync(stream, token)));
            Assert.Equal(new byte[] { 0x00 }, await FakeStatusServer.ReadPacketAsync(stream, token));
            await FakeStatusServer.SendStatusAsync(stream, """
                {"version":{"name":"1.20.1","protocol":763},
                 "players":{"online":3,"max":20,"sample":[{"name":"§aAlice","id":"x"},{"name":"Bob"}]},
                 "description":"§6Welcome"}
                """, token);
            var ping = await FakeStatusServer.ReadPacketAsync(stream, token);
            Assert.Equal(9, ping.Length);
            Assert.Equal(0x01, ping[0]);
            await stream.WriteAsync(FakeStatusServer.Frame(ping), token);
        });
        port = server.Port;

        var result = await MinecraftServerPing.QueryAsync("127.0.0.1", port, TimeSpan.FromSeconds(5));

        Assert.True(result.RequestSucceeded);
        Assert.True(result.IsOnline);
        Assert.Equal(3, result.OnlinePlayers);
        Assert.Equal(20, result.MaxPlayers);
        Assert.Equal(new[] { "Alice", "Bob" }, result.Players);
        Assert.Equal("Welcome", result.Motd);
        Assert.Equal("1.20.1", result.VersionName);
        Assert.NotNull(result.PingMilliseconds);
        Assert.True(result.PingMilliseconds >= 0);
        await server.Completion;
    }

    [Fact]
    public async Task QueryAsync_KeepsStatusWhenPongNeverArrives()
    {
        await using var server = new FakeStatusServer(async (stream, token) =>
        {
            await FakeStatusServer.ReadPacketAsync(stream, token);
            await FakeStatusServer.ReadPacketAsync(stream, token);
            await FakeStatusServer.SendStatusAsync(stream, """{"players":{"online":1,"max":2},"description":"Hi"}""", token);
        });

        var result = await MinecraftServerPing.QueryAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));

        Assert.True(result.RequestSucceeded);
        Assert.Equal(1, result.OnlinePlayers);
        Assert.Null(result.PingMilliseconds);
    }

    [Fact]
    public void ParseStatus_ReadsPlainDescriptionAndStripsFormatting()
    {
        var result = MinecraftServerPing.ParseStatus("""{"description":"§aHello §lWorld"}""");

        Assert.Equal("Hello World", result.Motd);
        Assert.DoesNotContain('§', result.Motd);
    }

    [Fact]
    public void ParseStatus_ReadsNestedTextAndExtra()
    {
        var result = MinecraftServerPing.ParseStatus("""
            {"description":{"text":"§6A","extra":["B",{"text":"C","extra":[{"text":"§kD"}]}]}}
            """);

        Assert.Equal("ABCD", result.Motd);
    }

    [Fact]
    public async Task QueryAsync_ReturnsUnavailableWhenServerStaysSilent()
    {
        await using var server = new FakeStatusServer((_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));

        var result = await MinecraftServerPing.QueryAsync("127.0.0.1", server.Port, ShortTimeout);

        Assert.False(result.RequestSucceeded);
        Assert.False(result.IsOnline);
    }

    [Fact]
    public async Task QueryAsync_ReturnsUnavailableWhenPortIsClosed()
    {
        var result = await MinecraftServerPing.QueryAsync("127.0.0.1", FakeStatusServer.ClosedPort(), TimeSpan.FromSeconds(5));

        Assert.False(result.RequestSucceeded);
    }

    [Fact]
    public async Task QueryAsync_ReturnsUnavailableWhenLengthExceedsOneMegabyte()
    {
        await using var server = new FakeStatusServer(async (stream, token) =>
        {
            await FakeStatusServer.ReadPacketAsync(stream, token);
            await FakeStatusServer.ReadPacketAsync(stream, token);
            await stream.WriteAsync(MinecraftServerPing.EncodeVarInt(1_048_577), token);
        });

        var result = await MinecraftServerPing.QueryAsync("127.0.0.1", server.Port, TimeSpan.FromSeconds(5));

        Assert.False(result.RequestSucceeded);
    }

    [Fact]
    public async Task QueryAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MinecraftServerPing.QueryAsync("127.0.0.1", FakeStatusServer.ClosedPort(), cancellationToken: cancellation.Token));
    }
}

/// <summary>A loopback listener that serves one connection with the supplied script.</summary>
internal sealed class FakeStatusServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cancellation = new();

    public FakeStatusServer(Func<NetworkStream, CancellationToken, Task> script)
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Completion = RunAsync(script);
    }

    public int Port { get; }

    public Task Completion { get; }

    public static int ClosedPort()
    {
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        return port;
    }

    public static byte[] Frame(byte[] payload) => [.. MinecraftServerPing.EncodeVarInt(payload.Length), .. payload];

    public static async Task<byte[]> ReadPacketAsync(Stream stream, CancellationToken token)
    {
        var length = await MinecraftServerPing.ReadVarIntAsync(stream, token);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token);
        return payload;
    }

    public static async Task SendStatusAsync(Stream stream, string json, CancellationToken token)
    {
        var text = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(Frame([0x00, .. MinecraftServerPing.EncodeVarInt(text.Length), .. text]), token);
    }

    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync();
        listener.Stop();
        await Completion;
        cancellation.Dispose();
    }

    private async Task RunAsync(Func<NetworkStream, CancellationToken, Task> script)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = client.GetStream();
            await script(stream, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The test ended while the script was still waiting.
        }
    }
}
