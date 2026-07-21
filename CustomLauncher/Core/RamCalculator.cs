namespace CustomLauncher.Core;

public sealed record RamRecommendation(int MinimumMb, int RecommendedMb, int MaximumMb);

public static class RamCalculator
{
    private const long Gibibyte = 1_073_741_824;

    // Ported from HeliosLauncher's ConfigManager legacy RAM calculation.
    public static RamRecommendation Calculate(long totalMemoryBytes)
    {
        if (totalMemoryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalMemoryBytes));
        }

        var minimumGb = totalMemoryBytes >= 6 * Gibibyte ? 3 : 2;
        var recommendedGb = totalMemoryBytes >= 8 * Gibibyte ? 4 : minimumGb;
        var overSixteen = totalMemoryBytes - (16 * Gibibyte);
        var reservedBytes = overSixteen > 0
            ? (overSixteen / 8) + (16 * Gibibyte / 4)
            : totalMemoryBytes / 4;
        var maximumGb = Math.Max(minimumGb, (totalMemoryBytes - reservedBytes) / Gibibyte);

        return new RamRecommendation(
            checked((int)(minimumGb * 1024)),
            checked((int)(Math.Min(recommendedGb, maximumGb) * 1024)),
            checked((int)(maximumGb * 1024)));
    }
}
