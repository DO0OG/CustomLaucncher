using CustomLauncher.Core;

namespace CustomLauncher.Tests.Core;

public sealed class AppPathsTests
{
    /// <summary>
    /// Two launchers built for two servers must not share a settings file. The folder name used to
    /// be hard-coded, so every build wrote to the same directory and overwrote the other's config.
    /// </summary>
    [Theory]
    [InlineData(PlatformKind.Windows)]
    [InlineData(PlatformKind.MacOS)]
    [InlineData(PlatformKind.Linux)]
    public void SeparateLauncherIdsGetSeparateDirectories(PlatformKind platform)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["APPDATA"] = @"C:\Users\demo\AppData\Roaming",
            ["LOCALAPPDATA"] = @"C:\Users\demo\AppData\Local",
        };
        var first = new AppPaths(platform, "/home/demo", environment, "AlphaServer", ".alpha");
        var second = new AppPaths(platform, "/home/demo", environment, "BetaServer", ".beta");

        Assert.NotEqual(first.ConfigDir, second.ConfigDir);
        Assert.NotEqual(first.LogDir, second.LogDir);
        Assert.NotEqual(first.DefaultGameDir, second.DefaultGameDir);
        Assert.NotEqual(first.SettingsFile, second.SettingsFile);
        Assert.Contains("AlphaServer", first.ConfigDir, StringComparison.Ordinal);
        Assert.Contains("BetaServer", second.ConfigDir, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData(".")]
    public void AnUnusableLauncherIdFallsBackInsteadOfEscapingTheParentDirectory(string launcherId)
    {
        var paths = new AppPaths(PlatformKind.Linux, "/home/demo",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), launcherId, ".game");

        Assert.Equal("CustomLauncher", paths.LauncherId);
        Assert.Equal(Path.Combine("/home/demo", ".config", "CustomLauncher"), paths.ConfigDir);
    }

    [Fact]
    public void InvalidPathCharactersAreStrippedFromTheLauncherId()
    {
        var paths = new AppPaths(PlatformKind.Linux, "/home/demo",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), "My/Server\0Name", ".game");

        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), paths.LauncherId.Contains);
    }

    [Fact]
    public void WindowsPathsAreSeparated()
    {
        var paths = new AppPaths(PlatformKind.Windows, "C:\\Users\\tester",
            new Dictionary<string, string?> { ["APPDATA"] = "C:\\Roaming", ["LOCALAPPDATA"] = "C:\\Local" });
        Assert.Equal(Path.Combine("C:\\Roaming", "CustomLauncher"), paths.ConfigDir);
        Assert.Equal(Path.Combine("C:\\Local", "CustomLauncher", "logs"), paths.LogDir);
        Assert.Equal(Path.Combine("C:\\Roaming", ".custom"), paths.DefaultGameDir);
    }

    [Fact]
    public void MacPathsFollowPlatformConvention()
    {
        var paths = new AppPaths(PlatformKind.MacOS, "/Users/tester", new Dictionary<string, string?>());
        Assert.Equal(Path.Combine("/Users/tester", "Library", "Application Support", "CustomLauncher"), paths.ConfigDir);
        Assert.Equal(Path.Combine("/Users/tester", "Library", "Logs", "CustomLauncher"), paths.LogDir);
    }

    [Fact]
    public void LinuxHonorsXdgVariables()
    {
        var paths = new AppPaths(PlatformKind.Linux, "/home/tester",
            new Dictionary<string, string?> { ["XDG_CONFIG_HOME"] = "/cfg", ["XDG_STATE_HOME"] = "/state" });
        Assert.Equal(Path.Combine("/cfg", "CustomLauncher"), paths.ConfigDir);
        Assert.Equal(Path.Combine("/state", "CustomLauncher"), paths.LogDir);
        Assert.Equal(Path.Combine("/home/tester", ".custom"), paths.DefaultGameDir);
    }
}
