namespace CustomLauncher.Core;

public enum PlatformKind { Windows, MacOS, Linux }

public static class PlatformDetector
{
    public static PlatformKind Current =>
        OperatingSystem.IsWindows() ? PlatformKind.Windows :
        OperatingSystem.IsMacOS() ? PlatformKind.MacOS :
        PlatformKind.Linux;
}
