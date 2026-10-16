using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core.Java;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

public interface IGameRuntime
{
    Task InstallAsync(string versionName, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken);
    Task<Process> CreateProcessAsync(string versionName, MLaunchOption option, CancellationToken cancellationToken);
}

public sealed record PreparedGameSession(
    Process Process,
    ServerDistribution Distribution,
    string TargetVersionName,
    string? Warning = null);

public sealed class GameSessionPreparer(
    IContentUpdateService content,
    IModLoaderInstaller modLoader,
    IGameRuntime runtime,
    IJavaProvisioner? javaProvisioner = null,
    Func<string, IReadOnlyCollection<string>>? disabledOptionalModules = null,
    IServerListWriter? serverList = null,
    string? gameDirectory = null,
    Func<string?>? selectedServerId = null)
{
    public async Task<PreparedGameSession> PrepareAsync(
        MLaunchOption launchOption,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ServerDistribution manifest;
        ServerEntry server;
        ServerDistribution distribution;
        string? warning = null;
        try
        {
            progress?.Report(new LaunchProgress("매니페스트 조회", 0));
            manifest = await content.FetchAsync(cancellationToken);
            // Honour the user's optional-module choices before touching the game directory.
            (server, distribution) = SelectDistribution(manifest);
            progress?.Report(new LaunchProgress("콘텐츠 동기화", 0));
            var update = await content.UpdateAsync(distribution, cancellationToken);
            switch (update.Status)
            {
                case ModuleUpdateStatus.Updated:
                case ModuleUpdateStatus.UpToDate:
                    await content.SaveCacheAsync(manifest, cancellationToken);
                    break;
                case ModuleUpdateStatus.Cancelled:
                    throw new OperationCanceledException(cancellationToken);
                case ModuleUpdateStatus.Failed:
                    throw new InvalidOperationException(update.Error ?? "콘텐츠 업데이트에 실패했습니다.");
                default:
                    throw new InvalidOperationException("알 수 없는 콘텐츠 업데이트 상태입니다.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            manifest = await content.TryLoadCacheAsync(cancellationToken)
                ?? throw new InvalidOperationException("최초 콘텐츠 동기화가 필요하지만 매니페스트에 연결할 수 없습니다.", exception);
            (server, distribution) = SelectDistribution(manifest);
            if (!content.HasManagedState(server.Id))
                throw new InvalidOperationException("검증된 로컬 콘텐츠 상태가 없어 오프라인으로 실행할 수 없습니다.", exception);
            warning = "네트워크에 연결할 수 없어 마지막으로 검증된 콘텐츠로 실행합니다.";
            progress?.Report(new LaunchProgress("오프라인 콘텐츠 사용", 1, warning));
        }

        if (javaProvisioner is not null)
            await javaProvisioner.EnsureAsync(distribution.Java, launchOption, false, progress, cancellationToken);
        if (distribution.Java.MinRamMb is int requiredRam && launchOption.MaximumRamMb < requiredRam)
            warning = $"서버 권장 최소 메모리는 {requiredRam}MB입니다. 현재 최대값은 {launchOption.MaximumRamMb}MB입니다.";

        // Best effort: the player can always add the server by hand, so a failure here is not
        // allowed to stop the launch.
        if (gameDirectory is not null)
        {
            // The game process starts inside this folder, so it has to exist even before any module lands.
            var serverDirectory = LauncherServers.GameDirectoryFor(gameDirectory, server.Id);
            Directory.CreateDirectory(serverDirectory);
            serverList?.Register(serverDirectory, server.Name, ServerCatalog.JoinAddress(server));
            launchOption.Path = LauncherServers.MinecraftPathFor(gameDirectory, server.Id);
        }

        launchOption.ServerIp = server.Address;
        launchOption.ServerPort = server.Port;
        var targetVersion = await modLoader.EnsureInstalledAsync(
            ModLoaderConfiguration.FromServer(server), progress, cancellationToken);
        progress?.Report(new LaunchProgress("게임 파일 설치", 0));
        await runtime.InstallAsync(targetVersion, progress, cancellationToken);
        progress?.Report(new LaunchProgress("프로세스 생성", 0.95));
        var process = await runtime.CreateProcessAsync(targetVersion, launchOption, cancellationToken);
        progress?.Report(new LaunchProgress("실행 준비 완료", 1));
        return new PreparedGameSession(process, distribution, targetVersion, warning);
    }

    /// <summary>Re-verifies every managed file of the selected server; there is no offline fallback.</summary>
    public async Task<ModuleUpdateResult> RepairAsync(IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new LaunchProgress("매니페스트 조회", 0));
        var manifest = await content.FetchAsync(cancellationToken);
        var (_, distribution) = SelectDistribution(manifest);
        return await content.RepairAsync(distribution, progress, cancellationToken);
    }

    private (ServerEntry Server, ServerDistribution Distribution) SelectDistribution(ServerDistribution manifest)
    {
        var server = ServerCatalog.Select(manifest, selectedServerId?.Invoke());
        return (server, ModuleSelection.Filter(
            ServerCatalog.ForServer(manifest, server), disabledOptionalModules?.Invoke(server.Id)));
    }
}
