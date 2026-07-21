namespace CustomLauncher.Shared.Models;

public sealed class JavaRequirement
{
    public string MinVersion { get; set; } = string.Empty;
    public string RecommendedVersion { get; set; } = string.Empty;
    public int? MinRamMb { get; set; }
}

public sealed class ServerDistribution
{
    public string ServerId { get; set; } = string.Empty;
    public List<DistroModule> Modules { get; set; } = [];
    public JavaRequirement Java { get; set; } = new();
    public string? DefaultShaderPackId { get; set; }
}
