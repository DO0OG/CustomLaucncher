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
    /// <summary>
    /// Azure Entra(공개 클라이언트) 앱의 애플리케이션 ID. 런처마다 직접 등록해야 하며,
    /// 라이브러리에 내장된 기본값은 없다. 비어 있으면 로그인이 비활성화된다.
    /// 등록 방법은 docs/AUTH_FLOW.md 참조.
    /// </summary>
    public const string MicrosoftClientId = "";

    /// <summary>운영자가 클라이언트 ID를 채웠는지 여부. UI가 로그인 가능 상태를 판단하는 데 쓴다.</summary>
    public static bool IsMicrosoftAuthConfigured => !string.IsNullOrWhiteSpace(MicrosoftClientId);
    public const bool EnableDiscordRpc = false;
}
