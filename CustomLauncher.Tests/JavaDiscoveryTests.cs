using CustomLauncher.Core.Java;

namespace CustomLauncher.Tests;

public sealed class JavaDiscoveryTests
{
    [Fact]
    public void WindowsDiscovery_PrefersJavaHomeAndUsesJavaw()
    {
        var fileSystem = new FakeFileSystem(
            new Dictionary<string, string?> { ["JAVA_HOME"] = @"C:\jdk" },
            new[] { @"C:\jdk\bin\javaw.exe" });
        var discovery = new WindowsJavaDiscovery(fileSystem, new FakeRegistry());

        Assert.Equal("javaw.exe", discovery.ExecutableFileName);
        Assert.Equal(@"C:\jdk\bin\javaw.exe", discovery.ScanSystemForValidJava());
    }

    [Fact]
    public void MacDiscovery_ParsesJavaHomeOutputAndUsesExtensionlessJava()
    {
        const string home = "/Library/Java/JavaVirtualMachines/jdk-21.jdk/Contents/Home";
        var fileSystem = new FakeFileSystem(new Dictionary<string, string?>(), new[] { home + "/bin/java" });
        var runner = new FakeRunner(new JavaProcessResult(0, string.Empty, $"21.0.4 (arm64) Oracle - Java {home}"));
        var discovery = new MacJavaDiscovery(fileSystem, runner);

        Assert.Equal("java", discovery.ExecutableFileName);
        Assert.Equal(home + "/bin/java", discovery.ScanSystemForValidJava());
    }

    [Fact]
    public void LinuxDiscovery_UsesUpdateAlternatives()
    {
        const string executable = "/usr/lib/jvm/java-21/bin/java";
        var fileSystem = new FakeFileSystem(new Dictionary<string, string?>(), new[] { executable });
        var runner = new FakeRunner(new JavaProcessResult(0, executable + "\n", string.Empty));
        var discovery = new LinuxJavaDiscovery(fileSystem, runner);

        Assert.Equal(executable, discovery.ScanSystemForValidJava());
    }

    private sealed class FakeRegistry : IWindowsJavaRegistry
    {
        public IEnumerable<string> FindJavaHomes() => Array.Empty<string>();
    }

    private sealed class FakeRunner : IJavaProcessRunner
    {
        private readonly JavaProcessResult result;
        public FakeRunner(JavaProcessResult result) => this.result = result;
        public Task<JavaProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeFileSystem : IJavaFileSystem
    {
        private readonly IReadOnlyDictionary<string, string?> environment;
        private readonly HashSet<string> files;

        public FakeFileSystem(IReadOnlyDictionary<string, string?> environment, IEnumerable<string> files)
        {
            this.environment = environment;
            this.files = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        }

        public bool FileExists(string path) => files.Contains(path);
        public IEnumerable<string> EnumerateDirectories(string path) => Array.Empty<string>();
        public string? GetEnvironmentVariable(string name) => environment.TryGetValue(name, out var value) ? value : null;
    }
}
