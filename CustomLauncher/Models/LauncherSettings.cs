using System.Text.Json.Serialization;

namespace CustomLauncher.Models;

public sealed class LauncherSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Resolution Resolution { get; set; } = new(1280, 720);
    public string InstallPath { get; set; } = string.Empty;
    public JavaConfig Java { get; set; } = JavaConfig.CreateDefault();
    public bool DiscordRpcEnabled { get; set; }
    public bool FullScreen { get; set; }
    public bool CloseLauncherOnGameStart { get; set; }
    public string? SelectedShaderPackId { get; set; }
    public List<string> EnabledResourcePacks { get; set; } = [];

    public string? SelectedServerId { get; set; }

    /// <summary>
    /// 서버별로 사용자가 끈 선택형(Optional) 모듈의 Id. 서버 매니페스트는 서버 소유이므로
    /// 사용자 선택 상태는 여기(사용자 설정)에만 기록한다.
    /// </summary>
    public Dictionary<string, List<string>> DisabledOptionalModuleIdsByServer { get; set; } = new(StringComparer.Ordinal);

    /// <summary>스키마 2 이하에서 읽어 이전하기 위한 속성. 새로 쓰지 않으며 저장 때 파일에서 사라진다.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DisabledOptionalModuleIds { get; set; }

    /// <summary>같은 인스턴스를 돌려준다. UI가 목록을 직접 고친 뒤 저장하기 때문이다.</summary>
    public List<string> DisabledModulesFor(string serverId)
    {
        if (!DisabledOptionalModuleIdsByServer.TryGetValue(serverId, out var disabled))
            DisabledOptionalModuleIdsByServer[serverId] = disabled = [];
        return disabled;
    }
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
