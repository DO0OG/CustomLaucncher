using Microsoft.Win32;

namespace CustomLauncher.Core.Java;

public interface IWindowsJavaRegistry
{
    IEnumerable<string> FindJavaHomes();
}

public sealed class WindowsJavaRegistry : IWindowsJavaRegistry
{
    private static readonly string[] Roots =
    {
        @"SOFTWARE\JavaSoft\Java Runtime Environment",
        @"SOFTWARE\JavaSoft\JDK",
        @"SOFTWARE\Eclipse Adoptium\JDK",
        @"SOFTWARE\AdoptOpenJDK\JDK",
    };

    public IEnumerable<string> FindJavaHomes()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                foreach (var rootPath in Roots)
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var root = baseKey.OpenSubKey(rootPath);
                    if (root is null)
                    {
                        continue;
                    }

                    if (root.GetValue("JavaHome") is string rootHome)
                    {
                        yield return rootHome;
                    }

                    foreach (var childName in root.GetSubKeyNames())
                    {
                        using var child = root.OpenSubKey(childName);
                        if (child?.GetValue("JavaHome") is string javaHome)
                        {
                            yield return javaHome;
                        }

                        foreach (var grandChildName in child?.GetSubKeyNames() ?? Array.Empty<string>())
                        {
                            using var grandChild = child!.OpenSubKey(grandChildName);
                            if (grandChild?.GetValue("Path") is string path)
                            {
                                yield return path;
                            }
                        }
                    }
                }
    }
}

public sealed class WindowsJavaDiscovery : IJavaDiscovery
{
    private readonly IJavaFileSystem fileSystem;
    private readonly IWindowsJavaRegistry registry;

    public WindowsJavaDiscovery(IJavaFileSystem? fileSystem = null, IWindowsJavaRegistry? registry = null)
    {
        this.fileSystem = fileSystem ?? new PhysicalJavaFileSystem();
        this.registry = registry ?? new WindowsJavaRegistry();
    }

    public string ExecutableFileName => "javaw.exe";

    public string? ScanSystemForValidJava() => JavaDiscoveryUtilities.FirstExistingExecutable(
        fileSystem,
        EnumerateCandidates(),
        ExecutableFileName);

    private IEnumerable<string?> EnumerateCandidates()
    {
        yield return fileSystem.GetEnvironmentVariable("JAVA_HOME");
        foreach (var home in registry.FindJavaHomes())
        {
            yield return home;
        }

        foreach (var environmentName in new[] { "ProgramFiles", "ProgramFiles(x86)" })
        {
            var programFiles = fileSystem.GetEnvironmentVariable(environmentName);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                continue;
            }

            foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Microsoft", "Zulu" })
                foreach (var home in fileSystem.EnumerateDirectories(Path.Combine(programFiles, vendor)))
                {
                    yield return home;
                }
        }

        foreach (var executable in JavaDiscoveryUtilities.PathCandidates(fileSystem, ExecutableFileName))
        {
            yield return executable;
        }
    }
}
