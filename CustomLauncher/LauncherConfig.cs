namespace CustomLauncher;

public static class LauncherConfig
{
    public const string McVersion = "1.20.1";
    public const string ModLoaderType = "forge";
    public const string ForgeVersion = "47.3.0";
    public const string ServerIp = "play.example.com";
    public const int ServerPort = 25565;
    public const string ManifestUrl = "https://example.com/distribution.json";
    public const string ServerStatusApiUrl = "https://api.mcsrvstat.us/3/" + ServerIp;
    public const string SettingsFileName = "settings.json";
    public const string LegacySettingsFileName = "customServer_settings.txt";
    public const string LegacyUserDataFileName = "customServer_udata";
    public const string DebugLogFileName = "launcher.log";
    public const string GameLauncherName = "CustomLauncher";
    public const string UserAgent = "CustomLauncher/2.0 (+https://github.com/DO0OG/CustomLaucncher)";
    public const string DiscordClientId = "";
    public const bool EnableDiscordRpc = false;
}
