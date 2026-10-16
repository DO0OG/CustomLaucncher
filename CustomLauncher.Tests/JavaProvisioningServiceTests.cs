using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using CustomLauncher.Core.Java;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Tests;

public sealed class JavaProvisioningServiceTests
{
    private static readonly JavaRequirement Java21 = new() { MinVersion = "21", RecommendedVersion = "21" };

    [Fact]
    public async Task EnsureAsync_SkipsMismatchedCandidatesUntilOneMatches()
    {
        var service = Create(new[] { "/jvm/17/java", "/jvm/21/java" });
        var option = new MLaunchOption();

        var path = await service.EnsureAsync(Java21, option, false, null, CancellationToken.None);

        Assert.Equal("/jvm/21/java", path);
        Assert.Equal("/jvm/21/java", option.JavaPath);
    }

    [Fact]
    public async Task EnsureAsync_ReportsMismatchedConfiguredPathAndMentionsItWhenInstallIsOff()
    {
        var service = Create(Array.Empty<string>());
        var progress = new CapturingProgress();
        var option = new MLaunchOption { JavaPath = "/jvm/17/java" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureAsync(Java21, option, false, progress, CancellationToken.None));

        Assert.Contains(progress.Items, item => item.Detail?.Contains("지정한 Java가 Java 21 요구 조건과 맞지 않습니다") == true);
        Assert.Contains("지정한 Java가 Java 21 요구 조건과 맞지 않습니다", error.Message);
    }

    private static JavaProvisioningService Create(string[] candidates) =>
        new("runtime", new FakeDiscovery(candidates), new JavaValidator(new VersionByPathRunner(), new ExistingFileSystem()),
            new JavaInstaller(new HttpClient(), "runtime"));

    private sealed class CapturingProgress : IProgress<LaunchProgress>
    {
        public List<LaunchProgress> Items { get; } = [];
        public void Report(LaunchProgress value) => Items.Add(value);
    }

    private sealed class FakeDiscovery(string[] candidates) : IJavaDiscovery
    {
        public string ExecutableFileName => "java";
        public string? ScanSystemForValidJava() => candidates.FirstOrDefault();
        public IEnumerable<string> EnumerateCandidates() => candidates;
    }

    private sealed class VersionByPathRunner : IJavaProcessRunner
    {
        public Task<JavaProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var major = executablePath.Contains("/21/") ? 21 : 17;
            return Task.FromResult(new JavaProcessResult(0, string.Empty, $"openjdk version \"{major}.0.1\""));
        }
    }

    private sealed class ExistingFileSystem : IJavaFileSystem
    {
        public bool FileExists(string path) => true;
        public IEnumerable<string> EnumerateDirectories(string path) => Array.Empty<string>();
        public string? GetEnvironmentVariable(string name) => null;
    }
}
