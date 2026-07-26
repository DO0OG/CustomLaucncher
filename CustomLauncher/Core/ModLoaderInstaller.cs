using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.VersionMetadata;

namespace CustomLauncher.Core;

public interface IModLoaderInstaller
{
    Task<string> EnsureInstalledAsync(IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
}

public interface IModLoaderBackend
{
    Task<IReadOnlyCollection<string>> GetInstalledVersionNamesAsync(CancellationToken cancellationToken);
    string GetFabricVersionName(string minecraftVersion, string fabricVersion);
    Task<string> InstallForgeAsync(string minecraftVersion, string forgeVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
    Task<string> InstallFabricAsync(string minecraftVersion, string fabricVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
}

public sealed record ModLoaderConfiguration(
    string Type,
    string MinecraftVersion,
    string ForgeVersion,
    string FabricVersion)
{
    public static ModLoaderConfiguration FromLauncherConfig() => new(
        LauncherConfig.ModLoaderType,
        LauncherConfig.McVersion,
        LauncherConfig.ForgeVersion,
        LauncherConfig.FabricVersion);
}

public sealed class ModLoaderInstaller : IModLoaderInstaller
{
    private readonly IModLoaderBackend _backend;
    private readonly ModLoaderConfiguration _configuration;

    public ModLoaderInstaller(IModLoaderBackend backend, ModLoaderConfiguration? configuration = null)
    {
        _backend = backend;
        _configuration = configuration ?? ModLoaderConfiguration.FromLauncherConfig();
    }

    public async Task<string> EnsureInstalledAsync(IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        var type = _configuration.Type.Trim().ToLowerInvariant();
        if (type == "none") return _configuration.MinecraftVersion;

        progress?.Report(new LaunchProgress("모드 로더 확인", 0));
        var installed = await _backend.GetInstalledVersionNamesAsync(cancellationToken);
        switch (type)
        {
            case "forge":
                {
                    var existing = installed.FirstOrDefault(name =>
                        name.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains(_configuration.MinecraftVersion, StringComparison.OrdinalIgnoreCase) &&
                        name.Contains(_configuration.ForgeVersion, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null) return existing;
                    progress?.Report(new LaunchProgress("Forge 설치", 0));
                    return await _backend.InstallForgeAsync(
                        _configuration.MinecraftVersion, _configuration.ForgeVersion, progress, cancellationToken);
                }
            case "fabric":
                {
                    var expected = _backend.GetFabricVersionName(
                        _configuration.MinecraftVersion, _configuration.FabricVersion);
                    if (installed.Contains(expected, StringComparer.OrdinalIgnoreCase)) return expected;
                    progress?.Report(new LaunchProgress("Fabric 설치", 0));
                    return await _backend.InstallFabricAsync(
                        _configuration.MinecraftVersion, _configuration.FabricVersion, progress, cancellationToken);
                }
            default:
                throw new InvalidOperationException($"지원하지 않는 모드 로더입니다: {_configuration.Type}");
        }
    }
}

public sealed class CmlModLoaderBackend(
    MinecraftLauncher launcher,
    MinecraftPath minecraftPath,
    HttpClient httpClient) : IModLoaderBackend
{
    public async Task<IReadOnlyCollection<string>> GetInstalledVersionNamesAsync(CancellationToken cancellationToken)
    {
        VersionMetadataCollection versions = await launcher.GetAllVersionsAsync(cancellationToken);
        return versions.OfType<LocalVersionMetadata>().Select(version => version.Name).ToArray();
    }

    public string GetFabricVersionName(string minecraftVersion, string fabricVersion) =>
        FabricInstaller.GetVersionName(minecraftVersion, fabricVersion);

    public Task<string> InstallForgeAsync(string minecraftVersion, string forgeVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        var options = new ForgeInstallOptions
        {
            CancellationToken = cancellationToken,
            FileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(value =>
                progress?.Report(new LaunchProgress("Forge 설치", value.ProgressedTasks / (double)Math.Max(1, value.TotalTasks), value.Name))),
            ByteProgress = new Progress<ByteProgress>(value =>
                progress?.Report(new LaunchProgress("Forge 설치", value.ToRatio()))),
            InstallerOutput = new Progress<string>(line =>
                progress?.Report(new LaunchProgress("Forge 설치", 0.9, line))),
        };
        return new ForgeInstaller(launcher, httpClient).Install(minecraftVersion, forgeVersion, options);
    }

    public async Task<string> InstallFabricAsync(string minecraftVersion, string fabricVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await new FabricInstaller(httpClient).Install(minecraftVersion, fabricVersion, minecraftPath);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LaunchProgress("Fabric 설치", 1));
        return result;
    }
}
