namespace CustomLauncher.Core;

public sealed class AppPaths
{
    private readonly string _home;
    private readonly IReadOnlyDictionary<string, string?> _environment;

    public AppPaths(PlatformKind? platform = null, string? home = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        Platform = platform ?? PlatformDetector.Current;
        _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _environment = environment ?? Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(x => (string)x.Key, x => x.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    public PlatformKind Platform { get; }

    public string ConfigDir => Platform switch
    {
        PlatformKind.Windows => Path.Combine(GetEnvironment("APPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomLauncher"),
        PlatformKind.MacOS => Path.Combine(_home, "Library", "Application Support", "CustomLauncher"),
        _ => Path.Combine(GetEnvironment("XDG_CONFIG_HOME") ?? Path.Combine(_home, ".config"), "CustomLauncher")
    };

    public string LogDir => Platform switch
    {
        PlatformKind.Windows => Path.Combine(GetEnvironment("LOCALAPPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CustomLauncher", "logs"),
        PlatformKind.MacOS => Path.Combine(_home, "Library", "Logs", "CustomLauncher"),
        _ => Path.Combine(GetEnvironment("XDG_STATE_HOME") ?? Path.Combine(_home, ".local", "state"), "CustomLauncher")
    };

    public string DefaultGameDir => Platform switch
    {
        PlatformKind.Windows => Path.Combine(GetEnvironment("APPDATA") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".custom"),
        PlatformKind.MacOS => Path.Combine(_home, "Library", "Application Support", "custom"),
        _ => Path.Combine(_home, ".custom")
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

    private string? GetEnvironment(string name) =>
        _environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static void MoveIfNeeded(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);
    }
}
