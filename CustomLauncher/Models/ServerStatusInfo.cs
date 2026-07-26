namespace CustomLauncher.Models;

public sealed class ServerStatusInfo
{
    public bool RequestSucceeded { get; init; }

    public bool IsOnline { get; init; }

    public int? OnlinePlayers { get; init; }

    public int? MaxPlayers { get; init; }

    public IReadOnlyList<string> Players { get; init; } = Array.Empty<string>();

    public string Motd { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public static ServerStatusInfo Unavailable(string? errorMessage = null) => new()
    {
        RequestSucceeded = false,
        IsOnline = false,
        ErrorMessage = errorMessage,
    };
}
