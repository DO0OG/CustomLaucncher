using System.Security.Cryptography;
using System.Text.Json;
using CustomLauncher.Shared.Models;

namespace CustomLauncher.ManifestTool.Commands;

public static class GenerateCommand
{
    public static async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var options = GenerateOptions.Parse(args);
        var distribution = await GenerateAsync(options, cancellationToken);

        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPath = outputPath + ".tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, distribution, ManifestJson.Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, outputPath, overwrite: true);
        Console.WriteLine($"Generated {distribution.Modules.Count} module(s): {outputPath}");
        return 0;
    }

    public static async Task<ServerDistribution> GenerateAsync(
        GenerateOptions options,
        CancellationToken cancellationToken = default)
    {
        var sourceDirectory = Path.GetFullPath(options.SourceDirectory);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Input directory does not exist: {sourceDirectory}");

        var existing = await LoadExistingAsync(options.ExistingPath, cancellationToken);
        var existingByPath = (existing?.Modules ?? [])
            .GroupBy(module => NormalizeManifestPath(module.Path), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var ignoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(options.OutputPath),
        };
        if (!string.IsNullOrWhiteSpace(options.ExistingPath))
            ignoredPaths.Add(Path.GetFullPath(options.ExistingPath));

        var modules = new List<DistroModule>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", enumerationOptions)
                     .Select(Path.GetFullPath)
                     .Where(path => !ignoredPaths.Contains(path))
                     .OrderBy(path => NormalizeManifestPath(Path.GetRelativePath(sourceDirectory, path)), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestPath = NormalizeManifestPath(Path.GetRelativePath(sourceDirectory, filePath));
            existingByPath.TryGetValue(manifestPath, out var previous);

            modules.Add(new DistroModule
            {
                Id = previous?.Id ?? CreateModuleId(manifestPath),
                Path = manifestPath,
                Url = BuildUrl(options, manifestPath) ?? previous?.Url ?? string.Empty,
                Hash = await ComputeSha256Async(filePath, cancellationToken),
                Type = options.Type ?? previous?.Type ?? ModuleType.RequiredMod,
                Packaging = options.Packaging ?? previous?.Packaging ?? ModulePackaging.File,
                ParentId = options.ParentWasSpecified ? options.ParentId : previous?.ParentId,
                LoadOrder = options.LoadOrderWasSpecified ? options.LoadOrder : previous?.LoadOrder,
                IsServerManaged = options.Managed ?? previous?.IsServerManaged ?? true,
            });
        }

        return new ServerDistribution
        {
            ServerId = options.ServerId ?? existing?.ServerId ?? new DirectoryInfo(sourceDirectory).Name,
            Modules = modules,
            Java = existing?.Java ?? new JavaRequirement(),
            DefaultShaderPackId = existing?.DefaultShaderPackId,
        };
    }

    public static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<ServerDistribution?> LoadExistingAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Existing manifest was not found.", fullPath);

        await using var stream = File.OpenRead(fullPath);
        return await JsonSerializer.DeserializeAsync<ServerDistribution>(stream, ManifestJson.Options, cancellationToken)
               ?? throw new InvalidDataException($"Manifest is empty: {fullPath}");
    }

    private static string NormalizeManifestPath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string CreateModuleId(string manifestPath)
    {
        var withoutExtension = Path.ChangeExtension(manifestPath, null) ?? manifestPath;
        return withoutExtension.Replace('\\', '/').Trim('/');
    }

    /// <summary>
    /// Placeholder replaced with the module's path, relative to the scanned directory.
    /// </summary>
    public const string PathPlaceholder = "{path}";

    private static string? BuildUrl(GenerateOptions options, string manifestPath)
    {
        // Segments are escaped individually so separators survive. A literal '/' is legal in a
        // query value too, which is what file hosts that take the path as a parameter expect.
        var escapedPath = string.Join('/', manifestPath.Split('/').Select(Uri.EscapeDataString));

        if (!string.IsNullOrWhiteSpace(options.UrlTemplate))
            return options.UrlTemplate.Replace(PathPlaceholder, escapedPath, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            return $"{options.BaseUrl.TrimEnd('/')}/{escapedPath}";
        return null;
    }

}

public sealed record GenerateOptions
{
    public required string SourceDirectory { get; init; }
    public string OutputPath { get; init; } = "distribution.json";
    public string? ExistingPath { get; init; }
    public string? ServerId { get; init; }
    public string? BaseUrl { get; init; }

    /// <summary>
    /// URL pattern containing <see cref="GenerateCommand.PathPlaceholder"/>. Use this for hosts that
    /// take the file path as a query parameter instead of a path suffix, such as a Seafile shared
    /// folder: <c>https://host/d/TOKEN/files/?p=/{path}&amp;dl=1</c>. One shared folder covers every
    /// file underneath it, so no per-file links are needed.
    /// </summary>
    public string? UrlTemplate { get; init; }
    public ModuleType? Type { get; init; }
    public ModulePackaging? Packaging { get; init; }
    public string? ParentId { get; init; }
    public bool ParentWasSpecified { get; init; }
    public int? LoadOrder { get; init; }
    public bool LoadOrderWasSpecified { get; init; }
    public bool? Managed { get; init; }

    public static GenerateOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
            throw new ArgumentException("generate requires an input directory.");

        string? output = null;
        string? existing = null;
        string? serverId = null;
        string? baseUrl = null;
        string? urlTemplate = null;
        ModuleType? type = null;
        ModulePackaging? packaging = null;
        string? parent = null;
        int? loadOrder = null;
        bool? managed = null;
        var parentSpecified = false;
        var loadOrderSpecified = false;

        for (var index = 1; index < args.Length; index++)
        {
            var name = args[index];
            var value = RequireValue(args, ref index, name);
            switch (name)
            {
                case "--output": output = value; break;
                case "--existing": existing = value; break;
                case "--server-id": serverId = value; break;
                case "--base-url": baseUrl = value; break;
                case "--url-template": urlTemplate = value; break;
                case "--type": type = ParseType(value); break;
                case "--packaging":
                    packaging = value.ToLowerInvariant() switch
                    {
                        "file" => ModulePackaging.File,
                        "archive" => ModulePackaging.Archive,
                        _ => throw new ArgumentException($"Unknown packaging: {value}"),
                    };
                    break;
                case "--parent":
                    parentSpecified = true;
                    parent = value.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : value;
                    break;
                case "--load-order":
                    loadOrderSpecified = true;
                    loadOrder = value.Equals("none", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : int.TryParse(value, out var parsedOrder)
                            ? parsedOrder
                            : throw new ArgumentException($"Invalid load order: {value}");
                    break;
                case "--managed":
                    managed = bool.TryParse(value, out var parsedManaged)
                        ? parsedManaged
                        : throw new ArgumentException($"Invalid boolean for --managed: {value}");
                    break;
                default: throw new ArgumentException($"Unknown generate option: {name}");
            }
        }

        if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(urlTemplate))
            throw new ArgumentException("Use either --base-url or --url-template, not both.");
        if (!string.IsNullOrWhiteSpace(urlTemplate) &&
            !urlTemplate.Contains(GenerateCommand.PathPlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"--url-template must contain {GenerateCommand.PathPlaceholder}; otherwise every module would share one URL.");
        }

        return new GenerateOptions
        {
            SourceDirectory = args[0],
            OutputPath = output ?? "distribution.json",
            ExistingPath = existing,
            ServerId = serverId,
            BaseUrl = baseUrl,
            UrlTemplate = urlTemplate,
            Type = type,
            Packaging = packaging,
            ParentId = parent,
            ParentWasSpecified = parentSpecified,
            LoadOrder = loadOrder,
            LoadOrderWasSpecified = loadOrderSpecified,
            Managed = managed,
        };
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (!option.StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Unexpected argument: {option}");
        if (++index >= args.Length)
            throw new ArgumentException($"Missing value for {option}.");
        return args[index];
    }

    private static ModuleType ParseType(string value) => value.ToLowerInvariant() switch
    {
        "required" or "requiredmod" => ModuleType.RequiredMod,
        "optional" or "optionalmod" => ModuleType.OptionalMod,
        "dropin" or "drop-in" or "dropinmod" => ModuleType.DropInMod,
        "shader" or "shaderpack" => ModuleType.ShaderPack,
        "resource" or "resourcepack" => ModuleType.ResourcePack,
        _ => throw new ArgumentException($"Unknown module type: {value}"),
    };
}
