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
