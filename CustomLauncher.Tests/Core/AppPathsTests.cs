using CustomLauncher.Core;

namespace CustomLauncher.Tests.Core;

public sealed class AppPathsTests
{
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
