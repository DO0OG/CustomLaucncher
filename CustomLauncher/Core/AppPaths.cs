namespace CustomLauncher.Core;

public sealed class AppPaths
{
    private readonly string _home;
    private readonly IReadOnlyDictionary<string, string?> _environment;
    private readonly string _launcherId;
    private readonly string _gameFolderName;

    public AppPaths(PlatformKind? platform = null, string? home = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? launcherId = null, string? gameFolderName = null)
    {
        Platform = platform ?? PlatformDetector.Current;
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _environment = environment ?? Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(x => (string)x.Key, x => x.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        // Folder names come from configuration: two launchers built for two servers must not
        // share a settings file, and the default game directory must not overlap either.
        _launcherId = Sanitize(launcherId ?? LauncherConfig.LauncherId, "CustomLauncher");
        _gameFolderName = Sanitize(gameFolderName ?? LauncherConfig.GameFolderName, ".custom");
    }

    public PlatformKind Platform { get; }
    public string LauncherId => _launcherId;

    public string ConfigDir => Platform switch
    {
        PlatformKind.Windows => Path.Combine(GetEnvironment("APPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _launcherId),
        PlatformKind.MacOS => Path.Combine(_home, "Library", "Application Support", _launcherId),
        _ => Path.Combine(GetEnvironment("XDG_CONFIG_HOME") ?? Path.Combine(_home, ".config"), _launcherId)
    };

    public string LogDir => Platform switch
    {
        PlatformKind.Windows => Path.Combine(GetEnvironment("LOCALAPPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _launcherId, "logs"),
        PlatformKind.MacOS => Path.Combine(_home, "Library", "Logs", _launcherId),
        _ => Path.Combine(GetEnvironment("XDG_STATE_HOME") ?? Path.Combine(_home, ".local", "state"), _launcherId)
    };

    public string DefaultGameDir => Platform switch
    {
        PlatformKind.Windows => Path.Combine(GetEnvironment("APPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _gameFolderName),
        PlatformKind.MacOS => Path.Combine(_home, "Library", "Application Support", _gameFolderName.TrimStart('.')),
        _ => Path.Combine(_home, _gameFolderName)
    };

    public string SettingsFile => Path.Combine(ConfigDir, LauncherConfig.SettingsFileName);
    public string AccountProfileFile => Path.Combine(ConfigDir, "account-profile.json");
    public string RuntimeDir => Path.Combine(ConfigDir, "runtime");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(RuntimeDir);
    }

    public void MigrateLegacyFiles()
    {
        EnsureCreated();
        if (Platform != PlatformKind.Windows) return;
        var appData = GetEnvironment("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        MoveIfNeeded(Path.Combine(appData, LauncherConfig.LegacySettingsFileName),
            Path.Combine(ConfigDir, LauncherConfig.LegacySettingsFileName));
        var legacyUserData = Path.Combine(appData, LauncherConfig.LegacyUserDataFileName);
        if (File.Exists(legacyUserData)) File.Delete(legacyUserData);
    }

    /// <summary>
    /// Keeps a misconfigured id from escaping the intended parent directory or producing a path the
    /// OS rejects. Falls back rather than throwing so a bad constant cannot brick startup.
    /// </summary>
    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var cleaned = new string(value.Trim()
            .Where(character => !Path.GetInvalidFileNameChars().Contains(character))
            .ToArray());
        return cleaned is "" or "." or ".." ? fallback : cleaned;
    }

    private string? GetEnvironment(string name) =>
        _environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static void MoveIfNeeded(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);
    }
}
