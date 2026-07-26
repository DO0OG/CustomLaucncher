using System.Text.Json.Serialization;

namespace CustomLauncher.Models;

public sealed class JavaConfig
{
    private static readonly string[] DefaultArguments =
    {
        "-XX:+UnlockExperimentalVMOptions",
        "-XX:+UseG1GC",
        "-XX:G1NewSizePercent=20",
        "-XX:G1ReservePercent=20",
        "-XX:MaxGCPauseMillis=50",
        "-XX:G1HeapRegionSize=16M",
        "-Dlog4j2.formatMsgNoLookups=true",
    };

    public string? ExecutablePath { get; set; }

    public int MinRamMb { get; set; } = 2048;

    public int MaxRamMb { get; set; } = 4096;

    public bool AutoInstallEnabled { get; set; }

    public List<string> CustomJvmArguments { get; set; } = CreateDefaultJvmArguments();

    [JsonIgnore]
    public List<string> JvmArguments
    {
        get => CustomJvmArguments;
        set => CustomJvmArguments = value ?? new List<string>();
    }

    [JsonIgnore]
    public string JvmArgumentsText
    {
        get => string.Join(System.Environment.NewLine, CustomJvmArguments);
        set => CustomJvmArguments = (value ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)
            .ToList();
    }

    public static List<string> CreateDefaultJvmArguments() => new(DefaultArguments);

    public static JavaConfig CreateDefault() => new();

    public static bool ContainsHeapOverride(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            var value = argument.TrimStart();
            if (value.StartsWith("-Xmx", System.StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("-Xms", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
