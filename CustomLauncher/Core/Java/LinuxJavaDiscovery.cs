namespace CustomLauncher.Core.Java;

public sealed class LinuxJavaDiscovery : IJavaDiscovery
{
    private readonly IJavaFileSystem fileSystem;
    private readonly IJavaProcessRunner processRunner;

    public LinuxJavaDiscovery(IJavaFileSystem? fileSystem = null, IJavaProcessRunner? processRunner = null)
    {
        this.fileSystem = fileSystem ?? new PhysicalJavaFileSystem();
        this.processRunner = processRunner ?? new JavaProcessRunner();
    }

    public string ExecutableFileName => "java";

    public string? ScanSystemForValidJava() => EnumerateCandidates().FirstOrDefault();

    public IEnumerable<string> EnumerateCandidates() => JavaDiscoveryUtilities.ExistingExecutables(
        fileSystem,
        EnumerateRawCandidates(),
        ExecutableFileName);

    private IEnumerable<string?> EnumerateRawCandidates()
    {
        yield return ToExecutable(fileSystem.GetEnvironmentVariable("JAVA_HOME"));

        JavaProcessResult? result = null;
        try
        {
            result = processRunner.RunAsync(
                "update-alternatives",
                new[] { "--list", "java" },
                TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
        }

        if (result?.ExitCode == 0)
        {
            foreach (var executable in result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return executable.Trim();
            }
        }

        foreach (var home in fileSystem.EnumerateDirectories("/usr/lib/jvm"))
        {
            yield return ToExecutable(home);
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
