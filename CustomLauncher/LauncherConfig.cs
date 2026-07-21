namespace CustomLauncher;

public static class LauncherConfig
{
    public const string McVersion = "1.20.1";
    public const string ModLoaderType = "forge";
    public const string ForgeVersion = "47.3.0";
    public const string FabricVersion = "0.15.7";
    public const string ServerIp = "play.example.com";
    public const int ServerPort = 25565;
    public const string ManifestUrl = "https://example.com/distribution.json";
    public const string ServerStatusApiUrl = "https://api.mcsrvstat.us/3/" + ServerIp;
    public const string SettingsFileName = "settings.json";
    public const string LegacySettingsFileName = "customServer_settings.txt";
    public const string LegacyUserDataFileName = "customServer_udata";
    public const string DebugLogFileName = "launcher.log";
    public const string GameLauncherName = "CustomLauncher";

    // ── 콘텐츠 폴더 / options 파일 ─────────────────────────────────
    /// <summary>드롭인 모드가 놓이는 게임 하위 폴더.</summary>
    public const string ModsFolderName = "mods";
    /// <summary>셰이더팩이 놓이는 게임 하위 폴더.</summary>
    public const string ShaderPackFolderName = "shaderpacks";
    /// <summary>리소스팩이 놓이는 게임 하위 폴더.</summary>
    public const string ResourcePackFolderName = "resourcepacks";
    /// <summary>리소스팩 목록이 기록되는 파일 (마인크래프트 표준).</summary>
    public const string GameOptionsFileName = "options.txt";
    /// <summary>
    /// 셰이더 선택이 기록되는 파일. Iris/OptiFine은 <c>optionsshaders.txt</c>를 쓰고
    /// 일부 구성은 <c>options.txt</c>에 기록한다. 서버 모드팩에 맞춰 이 값을 조정한다.
    /// </summary>
    public const string ShaderSettingsFileName = "optionsshaders.txt";
    /// <summary>셰이더 선택 키. Iris/OptiFine 공통으로 <c>shaderPack</c>.</summary>
    public const string ShaderSettingsKey = "shaderPack";
    public const string UserAgent = "CustomLauncher/2.0 (+https://github.com/DO0OG/CustomLaucncher)";
    public const string DiscordClientId = "";
    public const string MicrosoftClientId = "";
    public const bool EnableDiscordRpc = false;
}
