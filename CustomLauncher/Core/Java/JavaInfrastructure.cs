using System.Diagnostics;

namespace CustomLauncher.Core.Java;

public interface IJavaDiscovery
{
    string? ScanSystemForValidJava();

    string ExecutableFileName { get; }
}

public interface IJavaFileSystem
{
    bool FileExists(string path);

    IEnumerable<string> EnumerateDirectories(string path);

    string? GetEnvironmentVariable(string name);
}

public sealed class PhysicalJavaFileSystem : IJavaFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateDirectories(path).ToArray() : Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}

public sealed record JavaProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => string.Join(Environment.NewLine, new[] { StandardOutput, StandardError }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public interface IJavaProcessRunner
{
    Task<JavaProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class JavaProcessRunner : IJavaProcessRunner
{
    public async Task<JavaProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{executablePath}'.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        return new JavaProcessResult(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }
}

internal static class JavaDiscoveryUtilities
{
    public static string? FirstExistingExecutable(
        IJavaFileSystem fileSystem,
        IEnumerable<string?> javaHomesOrExecutables,
        string executableFileName)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        foreach (var value in javaHomesOrExecutables)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim().Trim('"');
            var candidate = Path.GetFileName(trimmed).Equals(executableFileName, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : Path.Combine(trimmed, "bin", executableFileName);
            if (seen.Add(candidate) && fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static IEnumerable<string> PathCandidates(IJavaFileSystem fileSystem, string executableFileName)
    {
        var path = fileSystem.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(directory.Trim().Trim('"'), executableFileName);
        }
    }
}
