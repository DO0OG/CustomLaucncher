namespace CustomLauncher.Core;

public sealed record LaunchProgress(string Stage, double Ratio, string? Detail = null)
{
    public double Ratio { get; init; } = Math.Clamp(Ratio, 0, 1);
}
