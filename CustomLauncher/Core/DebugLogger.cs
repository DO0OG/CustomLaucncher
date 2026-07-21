namespace CustomLauncher.Core;

public enum LauncherLogLevel { Debug, Info, Warn, Error }

public sealed class DebugLogger
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DebugLogger(AppPaths paths)
    {
        paths.EnsureCreated();
        _path = Path.Combine(paths.LogDir, LauncherConfig.DebugLogFileName);
    }

    public async Task WriteAsync(LauncherLogLevel level, string message, Exception? exception = null)
    {
        await _gate.WaitAsync();
        try
        {
            RollIfNeeded();
            var suffix = exception is null ? string.Empty : $"{Environment.NewLine}{exception}";
            await File.AppendAllTextAsync(_path,
                $"{DateTimeOffset.Now:O} [{level.ToString().ToUpperInvariant()}] {message}{suffix}{Environment.NewLine}");
        }
        finally { _gate.Release(); }
    }

    private void RollIfNeeded()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length < MaxLogBytes) return;
        File.Move(_path, _path + ".old", true);
    }
}
