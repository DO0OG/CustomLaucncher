using System.Net;
using System.Text.Json;
using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class ServerStatusCheckerTests
{
    [Fact]
    public void Parse_ReadsPlayersAndSanitizesDisplayText()
    {
        using var document = JsonDocument.Parse("""
            {
              "online": true,
              "players": {
                "online": 2,
                "max": 20,
                "list": ["§aAlice", { "name": "Bob\u0001" }]
              },
              "motd": { "clean": ["§6Welcome", "Second line"] }
            }
            """);

        var result = ServerStatusChecker.Parse(document.RootElement);

        Assert.True(result.RequestSucceeded);
        Assert.True(result.IsOnline);
        Assert.Equal(2, result.OnlinePlayers);
        Assert.Equal(20, result.MaxPlayers);
        Assert.Equal(new[] { "Alice", "Bob" }, result.Players);
        Assert.DoesNotContain('§', result.Motd);
    }

    [Fact]
    public void Parse_AllowsHiddenPlayersAndMotd()
    {
        using var document = JsonDocument.Parse("""{ "online": false }""");

        var result = ServerStatusChecker.Parse(document.RootElement);

        Assert.True(result.RequestSucceeded);
        Assert.False(result.IsOnline);
        Assert.Null(result.OnlinePlayers);
        Assert.Null(result.MaxPlayers);
        Assert.Empty(result.Players);
        Assert.Empty(result.Motd);
    }

    [Fact]
    public async Task CheckAsync_UsesLauncherUserAgentAndFallsBackOnMalformedJson()
    {
        var closedPort = FakeStatusServer.ClosedPort();
        var handler = new Handler(request =>
        {
            Assert.Equal(LauncherConfig.UserAgent, request.Headers.UserAgent.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") };
        });
        var checker = new ServerStatusChecker(new HttpClient(handler), "https://example.invalid/status");

        var result = await checker.CheckAsync("127.0.0.1", closedPort);

        Assert.False(result.RequestSucceeded);
        Assert.False(result.IsOnline);
    }

    [Fact]
    public async Task CheckAsync_PingsTheServerWhenNoStatusUrlIsGiven()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("HTTP must not be used."));
        await using var server = new FakeStatusServer(ServeStatusAsync);
        var checker = new ServerStatusChecker(new HttpClient(handler));

        var result = await checker.CheckAsync("127.0.0.1", server.Port);

        Assert.True(result.IsOnline);
        Assert.Equal(7, result.OnlinePlayers);
        Assert.NotNull(result.PingMilliseconds);
    }

    [Fact]
    public async Task CheckAsync_PingsTheServerWhenTheHttpRequestFails()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await using var server = new FakeStatusServer(ServeStatusAsync);
        var checker = new ServerStatusChecker(new HttpClient(handler), "https://example.invalid/status");

        var result = await checker.CheckAsync("127.0.0.1", server.Port);

        Assert.True(result.IsOnline);
        Assert.Equal(7, result.OnlinePlayers);
    }

    [Fact]
    public async Task CheckAsync_UsesTheHttpResultWithoutPingingWhenItSucceeds()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"online":true,"players":{"online":1,"max":2}}"""),
        });
        var checker = new ServerStatusChecker(new HttpClient(handler), "https://example.invalid/status");

        var result = await checker.CheckAsync("127.0.0.1", FakeStatusServer.ClosedPort());

        Assert.True(result.IsOnline);
        Assert.Equal(1, result.OnlinePlayers);
        Assert.Null(result.PingMilliseconds);
    }

    private static async Task ServeStatusAsync(System.Net.Sockets.NetworkStream stream, CancellationToken token)
    {
        await FakeStatusServer.ReadPacketAsync(stream, token);
        await FakeStatusServer.ReadPacketAsync(stream, token);
        await FakeStatusServer.SendStatusAsync(stream, """{"players":{"online":7,"max":10},"description":"x"}""", token);
        await stream.WriteAsync(FakeStatusServer.Frame(await FakeStatusServer.ReadPacketAsync(stream, token)), token);
    }

    [Fact]
    public async Task CheckAsync_PropagatesCallerCancellation()
    {
        var handler = new Handler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var checker = new ServerStatusChecker(new HttpClient(handler), "https://example.invalid/status");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.CheckAsync("127.0.0.1", 25565, cancellation.Token));
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback;

        public Handler(Func<HttpRequestMessage, HttpResponseMessage> callback)
            : this((request, _) => Task.FromResult(callback(request)))
        {
        }

        public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) =>
            this.callback = callback;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }
}
