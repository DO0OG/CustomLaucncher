namespace CustomLauncher.Models;

public sealed class LauncherSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Resolution Resolution { get; set; } = new(1280, 720);
    public string InstallPath { get; set; } = string.Empty;
    public JavaConfig Java { get; set; } = JavaConfig.CreateDefault();
    public bool DiscordRpcEnabled { get; set; }
    public string? SelectedShaderPackId { get; set; }
    public List<string> EnabledResourcePacks { get; set; } = [];

    /// <summary>
    /// 사용자가 끈 선택형(Optional) 모듈의 Id. 서버 매니페스트는 서버 소유이므로
    /// 사용자 선택 상태는 여기(사용자 설정)에만 기록한다.
    /// </summary>
    public List<string> DisabledOptionalModuleIds { get; set; } = [];
}

public sealed record Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

public sealed class AccountProfile
{
    public string? Username { get; set; }
    public string? SkinUrl { get; set; }
}
