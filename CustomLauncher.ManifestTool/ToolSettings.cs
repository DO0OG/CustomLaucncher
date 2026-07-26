using System.Text.Json;

namespace CustomLauncher.ManifestTool;

/// <summary>
/// Remembers what was typed last time. The operator runs this on every release, so retyping the
/// folder, share token and output path each time is most of the tedium the window exists to remove.
/// </summary>
public sealed class ToolSettings
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string UrlTemplate { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool MergeWithExisting { get; set; } = true;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomLauncherManifestTool",
        "settings.json");

    public static ToolSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(FilePath), Options) ?? new ToolSettings()
                : new ToolSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return new ToolSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing the remembered values is an annoyance, not a failure worth interrupting for.
        }
    }
}
