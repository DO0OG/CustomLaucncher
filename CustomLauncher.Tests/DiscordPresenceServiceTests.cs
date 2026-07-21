using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class DiscordPresenceServiceTests
{
    [Fact]
    public void DisabledService_DoesNotCreateClient()
    {
        var created = 0;
        using var service = new DiscordPresenceService("client", true, false, () =>
        {
            created++;
            return new FakeClient();
        });

        service.Init();
        service.SetState(DiscordPresenceState.Playing);

        Assert.Equal(0, created);
    }

    [Fact]
    public void StatesContainNoCallerControlledPersonalInformation()
    {
        var client = new FakeClient();
        using var service = new DiscordPresenceService("client", true, true, () => client);

        service.Init();
        service.SetState(DiscordPresenceState.Ready);
        service.SetState(DiscordPresenceState.LaunchingGame);
        service.SetState(DiscordPresenceState.Playing);

        Assert.All(client.Presences, presence =>
        {
            Assert.Equal("CustomLauncher", presence.Details);
            Assert.DoesNotContain("uuid", presence.State, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user", presence.State, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ToggleOff_ClearsAndDisposesImmediately()
    {
        var client = new FakeClient();
        var service = new DiscordPresenceService("client", true, true, () => client);
        service.Init();

        service.SetEnabled(false);

        Assert.True(client.Cleared);
        Assert.True(client.Disposed);
    }

    private sealed class FakeClient : IDiscordRpcClientAdapter
    {
        public List<DiscordPresence> Presences { get; } = new();
        public bool Cleared { get; private set; }
        public bool Disposed { get; private set; }

        public void Initialize(string clientId) { }
        public void SetPresence(DiscordPresence presence) => Presences.Add(presence);
        public void ClearPresence() => Cleared = true;
        public void Dispose() => Disposed = true;
    }
}
