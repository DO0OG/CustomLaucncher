using DiscordRPC;

namespace CustomLauncher.Core;

public sealed class DiscordRpcClientAdapter : IDiscordRpcClientAdapter
{
    private DiscordRpcClient? _client;

    public void Initialize(string clientId)
    {
        _client ??= new DiscordRpcClient(clientId);
        if (!_client.IsInitialized) _client.Initialize();
    }

    public void SetPresence(DiscordPresence presence) => _client?.SetPresence(new RichPresence
    {
        Details = presence.Details,
        State = presence.State
    });

    public void ClearPresence() => _client?.ClearPresence();

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
