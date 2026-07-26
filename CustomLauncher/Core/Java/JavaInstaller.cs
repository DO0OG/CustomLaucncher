using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace CustomLauncher.Core.Java;

public sealed record JavaInstallProgress(string Stage, long BytesReceived = 0, long? TotalBytes = null);

public sealed record JavaInstallResult(bool Succeeded, string? ExecutablePath, string? ErrorMessage)
{
    public static JavaInstallResult Failure(string error) => new(false, null, error);
}

public interface IJavaArchiveExtractor
{
    void Extract(string archivePath, string destinationDirectory);
}

public sealed class JavaArchiveExtractor : IJavaArchiveExtractor
{
    public void Extract(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZip(archivePath, destinationDirectory);
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            ValidateTarEntries(archivePath, destinationDirectory);
            using var archive = File.OpenRead(archivePath);
            using var gzip = new GZipStream(archive, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destinationDirectory, overwriteFiles: false);
            return;
        }

        throw new InvalidDataException("The downloaded Java archive format is not supported.");
    }

    private static void ExtractZip(string archivePath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destination = GetSafeDestination(destinationDirectory, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    private static void ValidateTarEntries(string archivePath, string destinationDirectory)
    {
        using var archive = File.OpenRead(archivePath);
        using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            _ = GetSafeDestination(destinationDirectory, entry.Name);
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                ValidateLinkTarget(destinationDirectory, entry.Name, entry.LinkName);
            }
        }
    }

    private static string GetSafeDestination(string root, string entryName)
    {
        if (Path.IsPathRooted(entryName))
        {
            throw new InvalidDataException("The Java archive contains an absolute path.");
        }

        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(fullRoot, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Java archive contains a path outside the installation directory.");
        }

        return destination;
    }

    private static void ValidateLinkTarget(string root, string entryName, string? linkName)
    {
        if (string.IsNullOrWhiteSpace(linkName))
        {
            throw new InvalidDataException("The Java archive contains an invalid link.");
        }

        var entryDirectory = Path.GetDirectoryName(entryName.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        _ = GetSafeDestination(root, Path.Combine(entryDirectory, linkName.Replace('/', Path.DirectorySeparatorChar)));
    }
}

public sealed class JavaInstaller
{
    private readonly HttpClient httpClient;
    private readonly string runtimeRoot;
    private readonly JavaValidator validator;
    private readonly IJavaArchiveExtractor extractor;

    public JavaInstaller(
        HttpClient httpClient,
        string runtimeRoot,
        JavaValidator? validator = null,
        IJavaArchiveExtractor? extractor = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.runtimeRoot = string.IsNullOrWhiteSpace(runtimeRoot)
            ? throw new ArgumentException("A runtime installation directory is required.", nameof(runtimeRoot))
            : runtimeRoot;
        this.validator = validator ?? new JavaValidator();
        this.extractor = extractor ?? new JavaArchiveExtractor();
    }

    public async Task<JavaInstallResult> InstallAsync(
        int featureVersion,
        IProgress<JavaInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (featureVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(featureVersion));
        }

        string? stagingDirectory = null;
        string? archivePath = null;
        try
        {
            var platform = GetPlatform();
            var architecture = GetArchitecture();
            progress?.Report(new JavaInstallProgress("Resolving Java package"));
            var package = await ResolvePackageAsync(featureVersion, platform, architecture, cancellationToken)
                .ConfigureAwait(false);

            Directory.CreateDirectory(runtimeRoot);
            stagingDirectory = Path.Combine(runtimeRoot, $".staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);
            archivePath = Path.Combine(stagingDirectory, package.Name);

            progress?.Report(new JavaInstallProgress("Downloading Java"));
            await DownloadAsync(package.Link, archivePath, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new JavaInstallProgress("Verifying checksum"));
            var actualChecksum = await ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualChecksum, package.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                return JavaInstallResult.Failure("The downloaded Java archive failed checksum verification.");
            }

            var extractionDirectory = Path.Combine(stagingDirectory, "content");
            progress?.Report(new JavaInstallProgress("Extracting Java"));
            extractor.Extract(archivePath, extractionDirectory);

            var executableName = platform == "windows" ? "javaw.exe" : "java";
            var executable = FindExecutable(extractionDirectory, executableName);
            if (executable is null)
            {
                return JavaInstallResult.Failure($"The Java archive does not contain {executableName}.");
            }

            if (!OperatingSystem.IsWindows())
            {
                EnsureExecutable(executable);
            }

            var validationExecutable = platform == "windows"
                ? FindExecutable(extractionDirectory, "java.exe") ?? executable
                : executable;
            var validation = await validator.ValidateAsync(validationExecutable, featureVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!validation.IsValid)
            {
                return JavaInstallResult.Failure(validation.ErrorMessage ?? "The installed Java runtime is invalid.");
            }

            File.Delete(archivePath);
            archivePath = null;
            var finalDirectory = GetAvailableFinalDirectory(featureVersion, platform, architecture);
            var relativeExecutable = Path.GetRelativePath(stagingDirectory, executable);
            Directory.Move(stagingDirectory, finalDirectory);
            stagingDirectory = null;
            progress?.Report(new JavaInstallProgress("Java installation complete"));
            return new JavaInstallResult(true, Path.Combine(finalDirectory, relativeExecutable), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            return JavaInstallResult.Failure($"Java installation failed: {exception.Message}");
        }
        finally
        {
            if (archivePath is not null)
            {
                TryDeleteFile(archivePath);
            }

            if (stagingDirectory is not null)
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
    }

    public static string GetArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "aarch64",
        _ => throw new PlatformNotSupportedException($"Java installation is not supported on {architecture}."),
    };

    public static string GetPlatform(bool isWindows, bool isMacOS, bool isLinux) =>
        isWindows ? "windows" :
        isMacOS ? "mac" :
        isLinux ? "linux" :
        throw new PlatformNotSupportedException("Java installation is not supported on this operating system.");

    private async Task<AdoptiumPackage> ResolvePackageAsync(
        int featureVersion,
        string platform,
        string architecture,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            $"https://api.adoptium.net/v3/assets/latest/{featureVersion}/hotspot" +
            $"?architecture={Uri.EscapeDataString(architecture)}&image_type=jdk&os={Uri.EscapeDataString(platform)}&vendor=eclipse");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CustomLauncher/2.0");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Adoptium did not return a compatible Java package.");
        }

        foreach (var asset in document.RootElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("binary", out var binary) ||
                !binary.TryGetProperty("package", out var packageElement))
            {
                continue;
            }

            var link = packageElement.TryGetProperty("link", out var linkElement) ? linkElement.GetString() : null;
            var checksum = packageElement.TryGetProperty("checksum", out var checksumElement) ? checksumElement.GetString() : null;
            var name = packageElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (Uri.TryCreate(link, UriKind.Absolute, out var packageUri) && packageUri.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrWhiteSpace(checksum) && !string.IsNullOrWhiteSpace(name))
            {
                return new AdoptiumPackage(packageUri, checksum, Path.GetFileName(name));
            }
        }

        throw new InvalidOperationException("Adoptium returned incomplete Java package metadata.");
    }

    private async Task DownloadAsync(
        Uri uri,
        string destination,
        IProgress<JavaInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Java packages must be downloaded over HTTPS.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CustomLauncher/2.0");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new JavaInstallProgress("Downloading Java", received, response.Content.Headers.ContentLength));
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string? FindExecutable(string root, string name)
    {
        return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase));
    }

    [UnsupportedOSPlatform("windows")]
    private static void EnsureExecutable(string path)
    {
        var mode = File.GetUnixFileMode(path);
        mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(path, mode);
    }

    private string GetAvailableFinalDirectory(int featureVersion, string platform, string architecture)
    {
        var basePath = Path.Combine(runtimeRoot, $"java-{featureVersion}-{platform}-{architecture}");
        if (!Directory.Exists(basePath))
        {
            return basePath;
        }

        return $"{basePath}-{Guid.NewGuid():N}";
    }

    private static string GetArchitecture() => GetArchitecture(RuntimeInformation.OSArchitecture);

    private static string GetPlatform() => GetPlatform(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        OperatingSystem.IsLinux());

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record AdoptiumPackage(Uri Link, string Checksum, string Name);
}
