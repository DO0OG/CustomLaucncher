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
        var handler = new Handler(request =>
        {
            Assert.Equal("CustomLauncher/2.0", request.Headers.UserAgent.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") };
        });
        var checker = new ServerStatusChecker(new HttpClient(handler), "https://example.invalid/status");

        var result = await checker.CheckAsync();

        Assert.False(result.RequestSucceeded);
        Assert.False(result.IsOnline);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.CheckAsync(cancellation.Token));
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
