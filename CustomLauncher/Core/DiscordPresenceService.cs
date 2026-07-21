namespace CustomLauncher.Core;

public enum DiscordPresenceState
{
    LauncherOpen,
    Ready,
    LaunchingGame,
    Playing,
}

public sealed record DiscordPresence(string Details, string State);

public interface IDiscordRpcClientAdapter : IDisposable
{
    void Initialize(string clientId);

    void SetPresence(DiscordPresence presence);

    void ClearPresence();
}

public interface IDiscordPresenceService : IDisposable
{
    bool IsEnabled { get; }

    void Init();

    void SetState(DiscordPresenceState state);

    void SetEnabled(bool enabled);

    void Shutdown();
}

public sealed class DiscordPresenceService : IDiscordPresenceService
{
    private readonly string clientId;
    private readonly bool featureEnabled;
    private readonly Func<IDiscordRpcClientAdapter> clientFactory;
    private readonly Action<string, Exception?>? log;
    private readonly object sync = new();
    private IDiscordRpcClientAdapter? client;
    private bool userEnabled;

    public DiscordPresenceService(
        string clientId,
        bool featureEnabled,
        bool userEnabled,
        Func<IDiscordRpcClientAdapter> clientFactory,
        Action<string, Exception?>? log = null)
    {
        this.clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        this.featureEnabled = featureEnabled;
        this.userEnabled = userEnabled;
        this.clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        this.log = log;
    }

    public bool IsEnabled => featureEnabled && userEnabled;

    public void Init()
    {
        lock (sync)
        {
            if (!IsEnabled || client is not null || string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            try
            {
                var newClient = clientFactory();
                client = newClient;
                newClient.Initialize(clientId);
                SetStateCore(DiscordPresenceState.LauncherOpen);
            }
            catch (Exception exception)
            {
                log?.Invoke("Discord Rich Presence is unavailable.", exception);
                DisposeClient();
            }
        }
    }

    public void SetState(DiscordPresenceState state)
    {
        lock (sync)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (client is null)
            {
                Init();
            }

            SetStateCore(state);
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            userEnabled = enabled;
            if (!IsEnabled)
            {
                DisposeClient();
                return;
            }

            Init();
        }
    }

    public void Shutdown()
    {
        lock (sync)
        {
            DisposeClient();
        }
    }

    public void Dispose() => Shutdown();

    private void SetStateCore(DiscordPresenceState state)
    {
        if (client is null)
        {
            return;
        }

        try
        {
            client.SetPresence(Map(state));
        }
        catch (Exception exception)
        {
            log?.Invoke("Discord Rich Presence update failed.", exception);
            DisposeClient();
        }
    }

    private void DisposeClient()
    {
        var oldClient = client;
        client = null;
        if (oldClient is null)
        {
            return;
        }

        try
        {
            oldClient.ClearPresence();
        }
        catch (Exception exception)
        {
            log?.Invoke("Discord Rich Presence could not be cleared.", exception);
        }

        try
        {
            oldClient.Dispose();
        }
        catch (Exception exception)
        {
            log?.Invoke("Discord Rich Presence shutdown failed.", exception);
        }
    }

    private static DiscordPresence Map(DiscordPresenceState state) => state switch
    {
        DiscordPresenceState.LauncherOpen => new("CustomLauncher", "Launcher open"),
        DiscordPresenceState.Ready => new("CustomLauncher", "Ready to play"),
        DiscordPresenceState.LaunchingGame => new("CustomLauncher", "Launching game"),
        DiscordPresenceState.Playing => new("CustomLauncher", "Playing"),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
