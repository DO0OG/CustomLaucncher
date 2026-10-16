using CustomLauncher.Core;
using CustomLauncher.ViewModels;

namespace CustomLauncher.Tests;

public sealed class GameLogBufferTests
{
    [Fact]
    public void KeepsOnlyNewestFiveHundredLines()
    {
        var buffer = new GameLogBuffer();
        for (var i = 1; i <= 600; i++) buffer.Append($"line {i}");

        Assert.True(buffer.TryTakeSnapshot(out var text));
        var lines = text.Split('\n');
        Assert.Equal(500, lines.Length);
        Assert.Equal("line 101", lines[0]);
        Assert.Equal("line 600", lines[^1]);
    }

    [Fact]
    public void SnapshotIsOnlyTakenWhenSomethingChanged()
    {
        var buffer = new GameLogBuffer();
        buffer.Append("a");
        buffer.Append("b");

        Assert.True(buffer.TryTakeSnapshot(out _));
        Assert.False(buffer.TryTakeSnapshot(out _));
        buffer.Append("c");
        Assert.True(buffer.TryTakeSnapshot(out _));
    }

    [Fact]
    public void ConcurrentAppendsStayWithinCapacity()
    {
        var buffer = new GameLogBuffer();
        Parallel.For(0, 8, worker =>
        {
            for (var i = 0; i < 400; i++) buffer.Append($"{worker}-{i}");
        });

        Assert.True(buffer.TryTakeSnapshot(out var text));
        Assert.True(text.Split('\n').Length <= 500);
    }

    [Fact]
    public void ClearDropsLinesAndPendingChange()
    {
        var buffer = new GameLogBuffer();
        buffer.Append("a");
        buffer.Clear();

        Assert.False(buffer.TryTakeSnapshot(out _));
    }

    [Fact]
    public void LogDirectoryIsServerFolderPlusLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "launcher-root");

        Assert.Equal(
            Path.Combine(LauncherServers.GameDirectoryFor(root, "alpha"), "logs"),
            MainViewModel.GameLogDirectory(root, "alpha"));
        Assert.NotEqual(
            MainViewModel.GameLogDirectory(root, "alpha"),
            MainViewModel.GameLogDirectory(root, "beta"));
    }
}
