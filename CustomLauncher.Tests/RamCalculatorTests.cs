using CustomLauncher.Core;

namespace CustomLauncher.Tests;

public sealed class RamCalculatorTests
{
    private const long GiB = 1_073_741_824;

    [Theory]
    [InlineData(5, 2048, 2048, 3072)]
    [InlineData(6, 3072, 3072, 4096)]
    [InlineData(8, 3072, 4096, 6144)]
    [InlineData(16, 3072, 4096, 12288)]
    [InlineData(17, 3072, 4096, 12288)]
    public void Calculate_PortsHeliosBoundaries(long totalGiB, int min, int recommended, int max)
    {
        var result = RamCalculator.Calculate(totalGiB * GiB);

        Assert.Equal(min, result.MinimumMb);
        Assert.Equal(recommended, result.RecommendedMb);
        Assert.Equal(max, result.MaximumMb);
    }
}
