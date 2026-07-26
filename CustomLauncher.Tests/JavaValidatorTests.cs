using CustomLauncher.Core.Java;

namespace CustomLauncher.Tests;

public sealed class JavaValidatorTests
{
    [Theory]
    [InlineData("java version \"1.8.0_402\"", 8)]
    [InlineData("openjdk version \"17.0.12\" 2024-07-16", 17)]
    [InlineData("openjdk version \"21.0.4+7\"", 21)]
    public async Task ValidateAsync_ParsesStderrVersionFormats(string stderr, int expected)
    {
        var validator = new JavaValidator(
            new FakeRunner(new JavaProcessResult(0, string.Empty, stderr)),
            new ExistingFileSystem());

        var result = await validator.ValidateAsync("java", expected);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.MajorVersion);
    }

    [Fact]
    public async Task ValidateAsync_RejectsWrongRequiredMajor()
    {
        var validator = new JavaValidator(
            new FakeRunner(new JavaProcessResult(0, string.Empty, "openjdk version \"17.0.1\"")),
            new ExistingFileSystem());

        var result = await validator.ValidateAsync("java", 21);

        Assert.False(result.IsValid);
        Assert.Equal(17, result.MajorVersion);
    }

    private sealed class FakeRunner : IJavaProcessRunner
    {
        private readonly JavaProcessResult result;
        public FakeRunner(JavaProcessResult result) => this.result = result;
        public Task<JavaProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ExistingFileSystem : IJavaFileSystem
    {
        public bool FileExists(string path) => true;
        public IEnumerable<string> EnumerateDirectories(string path) => Array.Empty<string>();
        public string? GetEnvironmentVariable(string name) => null;
    }
}
