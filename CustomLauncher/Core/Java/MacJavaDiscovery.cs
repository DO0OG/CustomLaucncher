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
                "/usr/libexec/java_home",
                new[] { "-V" },
                TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            /* java_home 도구가 없거나 실행할 수 없으면 이 탐색 경로는 건너뛰고 나머지 후보로 계속합니다. */
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
