namespace CustomLauncher.Core;

public interface ISystemMemoryProvider
{
    /// <summary>Total physical memory available to this machine, in bytes.</summary>
    long GetTotalMemoryBytes();
}

/// <summary>
/// Reads the machine's memory budget from the runtime. <c>TotalAvailableMemoryBytes</c> reports the
/// physical total on desktop and the cgroup limit inside containers, which is the number we want in
/// both cases: it is what the JVM heap has to fit inside.
/// </summary>
public sealed class RuntimeSystemMemoryProvider : ISystemMemoryProvider
{
    private const long FallbackBytes = 8L * 1024 * 1024 * 1024;

    public long GetTotalMemoryBytes()
    {
        var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return total > 0 ? total : FallbackBytes;
    }
}
