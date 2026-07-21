using System.Diagnostics;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core.Java;
using CustomLauncher.Models;

namespace CustomLauncher.Core;

public interface ILauncherService : IDisposable
{
    Task<PreparedGameSession> PrepareGameSessionAsync(
        LauncherSettings settings,
        MSession session,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class LauncherService : ILauncherService
{
    private readonly HttpClient _httpClient;
    private MinecraftLauncher? _launcher;
    private GameSessionPreparer? _preparer;
    private JavaProvisioningService? _javaProvisioner;
    private string? _launcherPath;

    public LauncherService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherConfig.UserAgent);
    }

    public async Task<PreparedGameSession> PrepareGameSessionAsync(
        LauncherSettings settings,
        MSession session,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _activeSettings = settings;
        EnsureLauncher(settings.InstallPath);
        try
        {
            var option = CreateLaunchOption(settings, session);
            var result = await _preparer!.PrepareAsync(option, progress, cancellationToken);
            settings.Java.ExecutablePath = option.JavaPath;
            return result;
        }
        finally { _activeSettings = null; }
    }

    public async Task<Process> CreateGameProcessAsync(
        LauncherSettings settings,
        MSession session,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var mapped = progress is null
            ? null
            : new Progress<LaunchProgress>(value => progress.Report(value.Ratio));
        return (await PrepareGameSessionAsync(settings, session, mapped, cancellationToken)).Process;
    }

    private static MLaunchOption CreateLaunchOption(LauncherSettings settings, MSession session)
    {
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
            JavaPath = settings.Java.ExecutablePath,
            ExtraJvmArguments = settings.Java.JvmArguments.Select(argument => new MArgument(argument)),
        };
        return option;
    }

    private void EnsureLauncher(string path)
    {
        path = Path.GetFullPath(path);
        if (_launcher is not null && string.Equals(_launcherPath, path, ModuleValidation.PathComparison)) return;

        Directory.CreateDirectory(path);
        var minecraftPath = new MinecraftPath(path);
        var parameters = MinecraftLauncherParameters.CreateDefault(minecraftPath, _httpClient);
        _launcher = new MinecraftLauncher(parameters);
        _launcherPath = path;

        var content = new ContentUpdateService(_httpClient, path);
        var modLoader = new ModLoaderInstaller(new CmlModLoaderBackend(_launcher, minecraftPath, _httpClient));
        _javaProvisioner?.Dispose();
        _javaProvisioner = new JavaProvisioningService(new AppPaths().RuntimeDir);
        _preparer = new GameSessionPreparer(content, modLoader, new CmlGameRuntime(_launcher),
            new ConsentAwareJavaProvisioner(_javaProvisioner, () => _activeSettings?.Java.AutoInstallEnabled == true),
            () => _activeSettings?.DisabledOptionalModuleIds ?? []);
    }

    private LauncherSettings? _activeSettings;

    public void Dispose()
    {
        _javaProvisioner?.Dispose();
        _httpClient.Dispose();
    }

    private sealed class ConsentAwareJavaProvisioner(IJavaProvisioner inner, Func<bool> allowInstall) : IJavaProvisioner
    {
        public Task<string?> EnsureAsync(CustomLauncher.Shared.Models.JavaRequirement requirement,
            MLaunchOption launchOption, bool ignored, IProgress<LaunchProgress>? progress,
            CancellationToken cancellationToken) =>
            inner.EnsureAsync(requirement, launchOption, allowInstall(), progress, cancellationToken);
    }

    private sealed class CmlGameRuntime(MinecraftLauncher launcher) : IGameRuntime
    {
        public async Task InstallAsync(
            string versionName,
            IProgress<LaunchProgress>? progress,
            CancellationToken cancellationToken)
        {
            var fileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(value =>
                progress?.Report(new LaunchProgress(
                    "게임 파일 설치",
                    value.ProgressedTasks / (double)Math.Max(1, value.TotalTasks),
                    value.Name)));
            var byteProgress = new Progress<ByteProgress>(value =>
                progress?.Report(new LaunchProgress("게임 파일 다운로드", value.ToRatio())));
            await launcher.InstallAsync(versionName, fileProgress, byteProgress, cancellationToken);
        }

        public async Task<Process> CreateProcessAsync(
            string versionName,
            MLaunchOption option,
            CancellationToken cancellationToken) =>
            await launcher.BuildProcessAsync(versionName, option, cancellationToken);
    }
}
