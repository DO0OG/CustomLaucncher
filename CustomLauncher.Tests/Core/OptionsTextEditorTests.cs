using System.Text;
using CustomLauncher.Core;

namespace CustomLauncher.Tests.Core;

public sealed class OptionsTextEditorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "launcher-options-tests-" + Guid.NewGuid().ToString("N"));

    public OptionsTextEditorTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task SetValue_PreservesBomNewlinesAndUnknownKeys()
    {
        var path = Path.Combine(_directory, "options.txt");
        var encoding = new UTF8Encoding(true);
        await File.WriteAllTextAsync(path, "music:1.0\r\nresourcePacks:[]\r\nguiScale:3\r\n", encoding);

        var status = await new OptionsTextEditor().SetValueAsync(path, "resourcePacks", "[\"file/a.zip\"]");

        Assert.Equal(OptionsEditStatus.Applied, status);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var text = encoding.GetString(bytes, Encoding.UTF8.Preamble.Length, bytes.Length - Encoding.UTF8.Preamble.Length);
        Assert.Equal("music:1.0\r\nresourcePacks:[\"file/a.zip\"]\r\nguiScale:3\r\n", text);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task SetValue_DoesNotWriteWhenMissingOrGameRunning()
    {
        var path = Path.Combine(_directory, "options.txt");
        Assert.Equal(OptionsEditStatus.MissingFile, await new OptionsTextEditor().SetValueAsync(path, "key", "value"));
        await File.WriteAllTextAsync(path, "key:old");
        Assert.Equal(OptionsEditStatus.GameRunning, await new OptionsTextEditor(() => true).SetValueAsync(path, "key", "new"));
        Assert.Equal("key:old", await File.ReadAllTextAsync(path));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
