using System.Globalization;

namespace CustomLauncher.Core;

public sealed record LaunchProgress(string Stage, double Ratio, string? Detail = null)
{
    private const double BytesPerMb = 1024d * 1024d;

    public double Ratio { get; init; } = Math.Clamp(Ratio, 0, 1);
    public int? FilesDone { get; init; }
    public int? FilesTotal { get; init; }
    public long? BytesDone { get; init; }
    public long? BytesTotal { get; init; }

    /// <summary>"{단계} {n}/{m} 파일, {x}/{y} MB" when counts exist, otherwise the stage with its detail.</summary>
    public string ToStatusText()
    {
        var parts = new List<string>(2);
        if (FilesDone is { } done && FilesTotal is > 0 and var total) parts.Add($"{done}/{total} 파일");
        if (BytesDone is { } bytes && BytesTotal is > 0 and var bytesTotal)
            parts.Add(string.Create(CultureInfo.InvariantCulture,
                $"{bytes / BytesPerMb:0.0}/{bytesTotal / BytesPerMb:0.0} MB"));
        if (parts.Count > 0) return $"{Stage} {string.Join(", ", parts)}";
        return string.IsNullOrWhiteSpace(Detail) ? Stage : $"{Stage}: {Detail}";
    }
}
