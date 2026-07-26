using System.Security.Cryptography;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

public static class ModuleValidation
{
    public static void ValidateDistribution(ServerDistribution distribution)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        if (string.IsNullOrWhiteSpace(distribution.ServerId))
            throw new InvalidDataException("ServerId is required.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(PathComparer);
        foreach (var module in distribution.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Id) || !ids.Add(module.Id))
                throw new InvalidDataException($"Duplicate or empty module id: '{module.Id}'.");
            var normalizedPath = NormalizeManifestPath(module.Path);
            if (module.Packaging == ModulePackaging.File && !paths.Add(normalizedPath))
                throw new InvalidDataException($"Duplicate module path: '{module.Path}'.");
            ValidateHttpsUri(module.Url, $"module '{module.Id}'");
            if (module.Hash.Length != 64 || !module.Hash.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Module '{module.Id}' has an invalid SHA256 hash.");
        }

        foreach (var module in distribution.Modules)
        {
            if (module.ParentId is not null && !ids.Contains(module.ParentId))
                throw new InvalidDataException($"Module '{module.Id}' has an unknown parent '{module.ParentId}'.");
            DetectParentCycle(module, distribution.Modules);
        }
    }

    public static Uri ValidateHttpsUri(string value, string description)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidDataException($"The {description} URL must be an absolute HTTPS URL without credentials.");
        }

        return uri;
    }

    public static string ResolvePath(string root, string relativePath)
    {
        var normalized = NormalizeManifestPath(relativePath);
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, PathComparison))
            throw new InvalidDataException($"Path escapes the game directory: '{relativePath}'.");
        return fullPath;
    }

    public static string NormalizeManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
            path.StartsWith('/') || path.StartsWith('\\') ||
            (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
        {
            throw new InvalidDataException($"Rooted or empty paths are not allowed: '{path}'.");
        }

        var segments = path.Replace('\\', '/').Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"Unsafe path segment in '{path}'.");
        return string.Join('/', segments);
    }

    public static void EnsureNoLinksInExistingParents(string root, string target)
    {
        var fullRoot = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(fullRoot, target);
        var current = fullRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;
            var info = Directory.Exists(current) ? (FileSystemInfo)new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Symbolic links are not allowed in managed paths: '{current}'.");
        }
    }

    public static bool HashEquals(string expectedHex, byte[] actual) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHex), actual);

    internal static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static void DetectParentCycle(DistroModule start, IReadOnlyCollection<DistroModule> modules)
    {
        var byId = modules.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var current = start;
        while (current.ParentId is not null)
        {
            if (!visited.Add(current.ParentId))
                throw new InvalidDataException($"Module parent cycle includes '{start.Id}'.");
            current = byId[current.ParentId];
        }
    }
}
