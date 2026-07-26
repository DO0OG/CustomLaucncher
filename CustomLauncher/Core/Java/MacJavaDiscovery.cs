namespace CustomLauncher.Core.Java;

public sealed class MacJavaDiscovery : IJavaDiscovery
{
    private readonly IJavaFileSystem fileSystem;
    private readonly IJavaProcessRunner processRunner;

    public MacJavaDiscovery(IJavaFileSystem? fileSystem = null, IJavaProcessRunner? processRunner = null)
    {
        this.fileSystem = fileSystem ?? new PhysicalJavaFileSystem();
        this.processRunner = processRunner ?? new JavaProcessRunner();
    }

    public string ExecutableFileName => "java";

    public string? ScanSystemForValidJava() => JavaDiscoveryUtilities.FirstExistingExecutable(
        fileSystem,
        EnumerateCandidates(),
        ExecutableFileName);

    private IEnumerable<string?> EnumerateCandidates()
    {
        yield return ToExecutable(fileSystem.GetEnvironmentVariable("JAVA_HOME"));

        JavaProcessResult? result = null;
        try
        {
            result = processRunner.RunAsync(
                "/usr/libexec/java_home",
                new[] { "-V" },
                TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
        }

        if (result is not null)
        {
            foreach (var line in result.CombinedOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pathStart = line.IndexOf(" /", StringComparison.Ordinal);
                if (pathStart >= 0)
                {
                    yield return ToExecutable(line[(pathStart + 1)..].Trim());
                }
            }
        }

        var home = fileSystem.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            var virtualMachines = Path.Combine(home, "Library", "Java", "JavaVirtualMachines");
            foreach (var bundle in fileSystem.EnumerateDirectories(virtualMachines))
            {
                yield return ToExecutable(bundle.TrimEnd('/', '\\') + "/Contents/Home");
            }
        }

        foreach (var executable in JavaDiscoveryUtilities.PathCandidates(fileSystem, ExecutableFileName))
        {
            yield return executable;
        }
    }

    private static string? ToExecutable(string? home) => string.IsNullOrWhiteSpace(home)
        ? null
        : home.Trim().TrimEnd('/', '\\') + "/bin/java";
}
