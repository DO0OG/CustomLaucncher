namespace CustomLauncher.Core.Java;

public static class JavaDiscoveryFactory
{
    public static IJavaDiscovery Create(PlatformKind? platform = null) => (platform ?? PlatformDetector.Current) switch
    {
        PlatformKind.Windows => new WindowsJavaDiscovery(),
        PlatformKind.MacOS => new MacJavaDiscovery(),
        PlatformKind.Linux => new LinuxJavaDiscovery(),
        _ => throw new PlatformNotSupportedException("이 운영 체제에서는 Java 자동 검색을 지원하지 않습니다.")
    };
}
