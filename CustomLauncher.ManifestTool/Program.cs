using Avalonia;
using CustomLauncher.ManifestTool;
using CustomLauncher.ManifestTool.Commands;

// Double-clicking opens the window; passing arguments keeps the original command line usable for
// scripting, so the generation logic has exactly one implementation behind both.
return args.Length == 0
    ? RunWindow(args)
    : await ManifestToolProgram.RunAsync(args);

static int RunWindow(string[] args) =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args);

internal static class ManifestToolProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (IsHelp(args[0]))
        {
            WriteUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "generate" => await GenerateCommand.ExecuteAsync(args[1..]),
                "diff" => await DiffCommand.ExecuteAsync(args[1..]),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        WriteUsage();
        return 1;
    }

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            CustomLauncher manifest tool

            Run without arguments to open the window. The command line below stays available
            for scripting.

            Usage:
              manifesttool generate <directory> [options]
              manifesttool diff <existing.json> <candidate.json>

            Generate options:
              --output <path>       Output file (default: ./distribution.json)
              --existing <path>     Preserve manual metadata from an existing manifest
              --server-id <id>      Server identifier (default: scanned directory name)
              --base-url <url>      Prefix used to construct download URLs
              --url-template <url>  URL pattern containing {path}, for hosts that take the file
                                    path as a query parameter. One shared folder link covers every
                                    file under it, e.g.
                                    "https://host/d/TOKEN/files/?p=/{path}&dl=1"
              --type <type>         required|optional|dropin|shader|resource
              --packaging <value>   file|archive (defaults to file; never inferred)
              --parent <id>         Parent module id for generated modules
              --load-order <number> Resource-pack load order for generated modules
              --managed <bool>      Whether generated modules are server managed

            Existing metadata is matched by normalized relative path. Id, type, parent,
            load order, management flag, and URL are retained unless explicitly overridden.
            """);
    }
}
