using System.Text.RegularExpressions;

namespace CustomLauncher.Core.Java;

public sealed record JavaValidationResult(
    bool IsValid,
    int? MajorVersion,
    string? Version,
    string? ErrorMessage)
{
    public static JavaValidationResult Invalid(string error) => new(false, null, null, error);
}

public sealed class JavaValidator
{
    private static readonly Regex VersionPattern =
        new("version\\s+\"(?<version>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IJavaProcessRunner processRunner;
    private readonly IJavaFileSystem fileSystem;
    private readonly TimeSpan timeout;

    public JavaValidator(
        IJavaProcessRunner? processRunner = null,
        IJavaFileSystem? fileSystem = null,
        TimeSpan? timeout = null)
    {
        this.processRunner = processRunner ?? new JavaProcessRunner();
        this.fileSystem = fileSystem ?? new PhysicalJavaFileSystem();
        this.timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<JavaValidationResult> ValidateAsync(
        string executablePath,
        int? requiredMajorVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !fileSystem.FileExists(executablePath))
        {
            return JavaValidationResult.Invalid("Java executable was not found.");
        }

        JavaProcessResult result;
        try
        {
            result = await processRunner.RunAsync(
                executablePath,
                new[] { "-version" },
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return JavaValidationResult.Invalid($"Java validation failed: {exception.Message}");
        }

        if (result.ExitCode != 0)
        {
            return JavaValidationResult.Invalid($"Java exited with code {result.ExitCode}.");
        }

        var match = VersionPattern.Match(result.CombinedOutput);
        if (!match.Success || !TryParseMajor(match.Groups["version"].Value, out var major))
        {
            return JavaValidationResult.Invalid("The Java version could not be parsed.");
        }

        if (requiredMajorVersion.HasValue && major != requiredMajorVersion.Value)
        {
            return new JavaValidationResult(
                false,
                major,
                match.Groups["version"].Value,
                $"Java {requiredMajorVersion.Value} is required, but Java {major} was found.");
        }

        return new JavaValidationResult(true, major, match.Groups["version"].Value, null);
    }

    public static bool TryParseMajor(string version, out int major)
    {
        major = 0;
        var parts = version.Split('.', '-', '+', '_');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var first))
        {
            return false;
        }

        if (first == 1)
        {
            return parts.Length > 1 && int.TryParse(parts[1], out major) && major > 0;
        }

        major = first;
        return major > 0;
    }
}
