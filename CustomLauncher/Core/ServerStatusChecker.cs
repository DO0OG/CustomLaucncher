using System.Text.Json;
using System.Text.RegularExpressions;
using CustomLauncher.Models;

namespace CustomLauncher.Core;

public sealed class ServerStatusChecker
{
    private const int MaxMotdLength = 240;
    private const int MaxDisplayedPlayers = 50;
    private const int MaxPlayerNameLength = 64;
    private static readonly Regex MinecraftFormattingCode =
        new("§[0-9A-FK-OR]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UnsafeControlCharacters =
        new("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F\\x7F]", RegexOptions.Compiled);

    private readonly HttpClient httpClient;
    private readonly string statusApiUrl;
    private readonly TimeSpan requestTimeout;

    public ServerStatusChecker(
        HttpClient httpClient,
        string? statusApiUrl = null,
        TimeSpan? requestTimeout = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.statusApiUrl = statusApiUrl ?? LauncherConfig.ServerStatusApiUrl;
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);

        if (this.requestTimeout <= TimeSpan.Zero || this.requestTimeout >= TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "The request timeout must be between zero and one minute.");
        }
    }

    public async Task<ServerStatusInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(requestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, statusApiUrl);
            request.Headers.UserAgent.ParseAdd("CustomLauncher/2.0");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: timeout.Token).ConfigureAwait(false);
            return Parse(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return ServerStatusInfo.Unavailable(exception.Message);
        }
    }

    public static ServerStatusInfo Parse(JsonElement root)
    {
        var online = root.TryGetProperty("online", out var onlineElement) &&
                     onlineElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                     onlineElement.GetBoolean();

        int? onlinePlayers = null;
        int? maxPlayers = null;
        var players = new List<string>();
        if (root.TryGetProperty("players", out var playersElement) && playersElement.ValueKind == JsonValueKind.Object)
        {
            onlinePlayers = ReadNonNegativeInt(playersElement, "online");
            maxPlayers = ReadNonNegativeInt(playersElement, "max");
            if (playersElement.TryGetProperty("list", out var listElement) && listElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in listElement.EnumerateArray())
                {
                    if (players.Count >= MaxDisplayedPlayers)
                    {
                        break;
                    }

                    string? name = item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var nameElement) &&
                          nameElement.ValueKind == JsonValueKind.String
                            ? nameElement.GetString()
                            : null;
                    name = Sanitize(name, MaxPlayerNameLength);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        players.Add(name);
                    }
                }
            }
        }

        var motd = string.Empty;
        if (root.TryGetProperty("motd", out var motdElement) && motdElement.ValueKind == JsonValueKind.Object &&
            motdElement.TryGetProperty("clean", out var cleanElement))
        {
            if (cleanElement.ValueKind == JsonValueKind.Array)
            {
                var lines = new List<string>();
                foreach (var line in cleanElement.EnumerateArray())
                {
                    if (line.ValueKind == JsonValueKind.String)
                    {
                        lines.Add(line.GetString() ?? string.Empty);
                    }
                }

                motd = string.Join(Environment.NewLine, lines);
            }
            else if (cleanElement.ValueKind == JsonValueKind.String)
            {
                motd = cleanElement.GetString() ?? string.Empty;
            }
        }

        return new ServerStatusInfo
        {
            RequestSucceeded = true,
            IsOnline = online,
            OnlinePlayers = onlinePlayers,
            MaxPlayers = maxPlayers,
            Players = players,
            Motd = Sanitize(motd, MaxMotdLength),
        };
    }

    private static int? ReadNonNegativeInt(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private static string Sanitize(string? value, int maxLength)
    {
        var sanitized = UnsafeControlCharacters.Replace(MinecraftFormattingCode.Replace(value ?? string.Empty, string.Empty), string.Empty);
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }
}
