using System.Text.Json;
using CustomLauncher.Models;

namespace CustomLauncher.Core;

public sealed class AppSettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppPaths _paths;

    public AppSettingsManager(AppPaths paths) => _paths = paths;

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        _paths.MigrateLegacyFiles();
        if (File.Exists(_paths.SettingsFile))
        {
            try
            {
                await using var stream = File.OpenRead(_paths.SettingsFile);
                var loaded = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken);
                return Normalize(loaded ?? CreateDefault());
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        var migrated = await LoadLegacyAsync(cancellationToken);
        await SaveAsync(migrated, cancellationToken);
        return migrated;
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        settings = Normalize(settings);
        var temp = _paths.SettingsFile + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temp, _paths.SettingsFile, true);
    }

    private async Task<LauncherSettings> LoadLegacyAsync(CancellationToken cancellationToken)
    {
        var result = CreateDefault();
        var legacy = Path.Combine(_paths.ConfigDir, LauncherConfig.LegacySettingsFileName);
        if (!File.Exists(legacy)) return result;
        var lines = await File.ReadAllLinesAsync(legacy, cancellationToken);
        if (lines.Length > 0)
        {
            var parts = lines[0].Split('x', 'X');
            if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height)
                && width > 0 && height > 0) result.Resolution = new Resolution(width, height);
        }
        if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])) result.InstallPath = lines[1];
        if (lines.Length > 2 && int.TryParse(lines[2], out var ram) && ram >= 512)
        {
            result.Java.MaxRamMb = ram;
            result.Java.MinRamMb = Math.Min(ram, Math.Max(512, ram / 2));
        }
        return result;
    }

    private LauncherSettings CreateDefault() => new() { InstallPath = _paths.DefaultGameDir };

    private LauncherSettings Normalize(LauncherSettings settings)
    {
        settings.SchemaVersion = LauncherSettings.CurrentSchemaVersion;
        settings.InstallPath = string.IsNullOrWhiteSpace(settings.InstallPath) ? _paths.DefaultGameDir : settings.InstallPath;
        settings.Java ??= JavaConfig.CreateDefault();
        settings.Java.MinRamMb = Math.Max(512, settings.Java.MinRamMb);
        settings.Java.MaxRamMb = Math.Max(settings.Java.MinRamMb, settings.Java.MaxRamMb);
        settings.EnabledResourcePacks ??= [];
        return settings;
    }
}
