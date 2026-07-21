using System.Diagnostics;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Models;

namespace CustomLauncher.Core;

public sealed class LauncherService : IDisposable
{
    private readonly HttpClient _httpClient;
    private MinecraftLauncher? _launcher;
    private string? _launcherPath;

    public LauncherService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherConfig.UserAgent);
    }

    public async Task<Process> CreateGameProcessAsync(LauncherSettings settings, MSession session,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureLauncher(settings.InstallPath);
        _launcher!.FileProgressChanged += (_, e) => progress?.Report(e.ProgressedTasks / (double)Math.Max(1, e.TotalTasks));
        await _launcher.InstallAsync(LauncherConfig.McVersion, cancellationToken);
        var option = new MLaunchOption
        {
            Session = session,
            ServerIp = LauncherConfig.ServerIp,
            ServerPort = LauncherConfig.ServerPort,
            ScreenWidth = settings.Resolution.Width,
            ScreenHeight = settings.Resolution.Height,
            GameLauncherName = LauncherConfig.GameLauncherName,
            MinimumRamMb = settings.Java.MinRamMb,
            MaximumRamMb = settings.Java.MaxRamMb,
            JavaPath = settings.Java.ExecutablePath
        };
        option.ExtraJvmArguments = settings.Java.JvmArguments.Select(argument => new MArgument(argument));
        cancellationToken.ThrowIfCancellationRequested();
        return await _launcher.CreateProcessAsync(LauncherConfig.McVersion, option);
    }

    private void EnsureLauncher(string path)
    {
        if (_launcher is not null && string.Equals(_launcherPath, path, StringComparison.Ordinal)) return;
        Directory.CreateDirectory(path);
        var parameters = MinecraftLauncherParameters.CreateDefault(new MinecraftPath(path), _httpClient);
        _launcher = new MinecraftLauncher(parameters);
        _launcherPath = path;
    }

    public void Dispose() => _httpClient.Dispose();
}
