using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.VersionMetadata;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

public interface IModLoaderInstaller
{
    Task<string> EnsureInstalledAsync(ModLoaderConfiguration configuration, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
}

public interface IModLoaderBackend
{
    Task<IReadOnlyCollection<string>> GetInstalledVersionNamesAsync(CancellationToken cancellationToken);
    string GetFabricVersionName(string minecraftVersion, string fabricVersion);
    Task<string> InstallForgeAsync(string minecraftVersion, string forgeVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
    Task<string> InstallFabricAsync(string minecraftVersion, string fabricVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
    string GetQuiltVersionName(string minecraftVersion, string quiltVersion);
    Task<string> InstallQuiltAsync(string minecraftVersion, string quiltVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
    Task<string> InstallNeoForgeAsync(string minecraftVersion, string neoForgeVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
}

public sealed record ModLoaderConfiguration(
    string Type,
    string MinecraftVersion,
    string ForgeVersion,
    string FabricVersion)
{
    public static ModLoaderConfiguration FromServer(ServerEntry server) => new(
        server.ModLoader.Type,
        server.MinecraftVersion,
        server.ModLoader.Version,
        server.ModLoader.Version);
}

public sealed class ModLoaderInstaller(IModLoaderBackend backend) : IModLoaderInstaller
{
    public async Task<string> EnsureInstalledAsync(
        ModLoaderConfiguration configuration, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        var type = configuration.Type.Trim().ToLowerInvariant();
        if (type is "none" or "vanilla") return configuration.MinecraftVersion;

        progress?.Report(new LaunchProgress("모드 로더 확인", 0));
        var installed = await backend.GetInstalledVersionNamesAsync(cancellationToken);
        switch (type)
        {
            case "forge":
                {
                    var existing = installed.FirstOrDefault(name =>
                        name.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains(configuration.MinecraftVersion, StringComparison.OrdinalIgnoreCase) &&
                        name.Contains(configuration.ForgeVersion, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null) return existing;
                    progress?.Report(new LaunchProgress("Forge 설치", 0));
                    return await backend.InstallForgeAsync(
                        configuration.MinecraftVersion, configuration.ForgeVersion, progress, cancellationToken);
                }
            case "fabric":
                {
                    var expected = backend.GetFabricVersionName(
                        configuration.MinecraftVersion, configuration.FabricVersion);
                    if (installed.Contains(expected, StringComparer.OrdinalIgnoreCase)) return expected;
                    progress?.Report(new LaunchProgress("Fabric 설치", 0));
                    return await backend.InstallFabricAsync(
                        configuration.MinecraftVersion, configuration.FabricVersion, progress, cancellationToken);
                }
            case "quilt":
                {
                    var expected = backend.GetQuiltVersionName(
                        configuration.MinecraftVersion, configuration.FabricVersion);
                    if (installed.Contains(expected, StringComparer.OrdinalIgnoreCase)) return expected;
                    progress?.Report(new LaunchProgress("Quilt 설치", 0));
                    return await backend.InstallQuiltAsync(
                        configuration.MinecraftVersion, configuration.FabricVersion, progress, cancellationToken);
                }
            case "neoforge":
                {
                    var existing = installed.FirstOrDefault(name =>
                        name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(configuration.ForgeVersion, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null) return existing;
                    progress?.Report(new LaunchProgress("NeoForge 설치", 0));
                    return await backend.InstallNeoForgeAsync(
                        configuration.MinecraftVersion, configuration.ForgeVersion, progress, cancellationToken);
                }
            default:
                throw new InvalidOperationException($"지원하지 않는 모드 로더입니다: {configuration.Type}");
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

    public string GetQuiltVersionName(string minecraftVersion, string quiltVersion) =>
        QuiltInstaller.GetVersionName(minecraftVersion, quiltVersion);

    public async Task<string> InstallQuiltAsync(string minecraftVersion, string quiltVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await new QuiltInstaller(httpClient).Install(minecraftVersion, quiltVersion, minecraftPath);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LaunchProgress("Quilt 설치", 1));
        return result;
    }

    public Task<string> InstallNeoForgeAsync(string minecraftVersion, string neoForgeVersion,
        IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        var options = new NeoForgeInstallOptions
        {
            CancellationToken = cancellationToken,
            FileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(value =>
                progress?.Report(new LaunchProgress("NeoForge 설치", value.ProgressedTasks / (double)Math.Max(1, value.TotalTasks), value.Name))),
            ByteProgress = new Progress<ByteProgress>(value =>
                progress?.Report(new LaunchProgress("NeoForge 설치", value.ToRatio()))),
            InstallerOutput = new Progress<string>(line =>
                progress?.Report(new LaunchProgress("NeoForge 설치", 0.9, line))),
        };
        return new NeoForgeInstaller(launcher).Install(minecraftVersion, neoForgeVersion, options);
    }
}
