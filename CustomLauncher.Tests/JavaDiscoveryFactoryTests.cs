using CustomLauncher.Core;
using CustomLauncher.Core.Java;

namespace CustomLauncher.Tests;

public sealed class JavaDiscoveryFactoryTests
{
    [Theory]
    [InlineData(PlatformKind.Windows, typeof(WindowsJavaDiscovery))]
    [InlineData(PlatformKind.MacOS, typeof(MacJavaDiscovery))]
    [InlineData(PlatformKind.Linux, typeof(LinuxJavaDiscovery))]
    public void CreatesPlatformDiscovery(PlatformKind platform, Type expected) =>
        Assert.IsType(expected, JavaDiscoveryFactory.Create(platform));

    [Theory]
    [InlineData("21.0.2", 21)]
    [InlineData("17", 17)]
    [InlineData("", 17)]
    public void ParsesManifestFeatureVersion(string value, int expected) =>
        Assert.Equal(expected, JavaProvisioningService.ParseFeatureVersion(value));
}
