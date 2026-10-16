using CustomLauncher.Core;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests.Core;

public sealed class ServerDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "launcher-servers-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void GameDirectoryFor_LegacyServerKeepsRoot()
    {
        Assert.Equal(Path.GetFullPath(_root), LauncherServers.GameDirectoryFor(_root, ServerCatalog.LegacyServerId));
    }

    [Fact]
    public void GameDirectoryFor_OtherServerUsesInstancesFolder()
    {
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "instances", "survival")),
            LauncherServers.GameDirectoryFor(_root, "survival"));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("A")]
    [InlineData("")]
    public void GameDirectoryFor_RejectsInvalidServerId(string serverId)
    {
        Assert.Throws<ArgumentException>(() => LauncherServers.GameDirectoryFor(_root, serverId));
    }

    [Fact]
    public void MinecraftPathFor_ServerFolderIsGameDirectoryAndSharedFilesStayAtRoot()
    {
        var shared = new CmlLib.Core.MinecraftPath(_root);

        var path = LauncherServers.MinecraftPathFor(_root, "survival");

        Assert.Equal(LauncherServers.GameDirectoryFor(_root, "survival"), path.BasePath);
        Assert.Equal(shared.Library, path.Library);
        Assert.Equal(shared.Versions, path.Versions);
        Assert.Equal(shared.Assets, path.Assets);
        Assert.Equal(shared.Resource, path.Resource);
        Assert.Equal(shared.Runtime, path.Runtime);
        Assert.False(Directory.Exists(path.Library));
    }

    [Fact]
    public void MinecraftPathFor_LegacyServerIsTheRootPath()
    {
        var shared = new CmlLib.Core.MinecraftPath(_root);

        var path = LauncherServers.MinecraftPathFor(_root, ServerCatalog.LegacyServerId);

        Assert.Equal(shared.BasePath, path.BasePath);
        Assert.Equal(shared.Library, path.Library);
    }

    [Fact]
    public void HasManagedState_ReadsOnlyTheRequestedServerFolder()
    {
        var serverDirectory = LauncherServers.GameDirectoryFor(_root, "b");
        Directory.CreateDirectory(serverDirectory);
        File.WriteAllText(Path.Combine(serverDirectory, ".launcher-managed-modules.json"), "{}");
        using var httpClient = new HttpClient();
        var service = new ContentUpdateService(httpClient, _root);

        Assert.True(service.HasManagedState("b"));
        Assert.False(service.HasManagedState(ServerCatalog.LegacyServerId));
        Assert.False(service.HasManagedState("a"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
