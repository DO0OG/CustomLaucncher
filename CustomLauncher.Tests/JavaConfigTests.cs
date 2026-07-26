using CustomLauncher.Models;

namespace CustomLauncher.Tests;

public sealed class JavaConfigTests
{
    [Fact]
    public void DefaultsContainG1GcAndLog4ShellMitigation()
    {
        var config = new JavaConfig();

        Assert.Contains("-XX:+UseG1GC", config.CustomJvmArguments);
        Assert.Contains("-Dlog4j2.formatMsgNoLookups=true", config.CustomJvmArguments);
        Assert.Equal(7, config.CustomJvmArguments.Count);
    }

    [Theory]
    [InlineData("-Xmx4G")]
    [InlineData(" -xMs512m")]
    public void ContainsHeapOverride_DetectsGeneratedMemoryArgumentConflicts(string argument)
    {
        Assert.True(JavaConfig.ContainsHeapOverride(new[] { argument }));
    }
}
