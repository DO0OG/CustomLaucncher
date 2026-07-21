using System.Text;

namespace CustomLauncher.Core;

public enum OptionsEditStatus { Applied, MissingFile, GameRunning }

public sealed class OptionsTextEditor
{
    private readonly Func<bool> _isGameRunning;

    public OptionsTextEditor(Func<bool>? isGameRunning = null)
    {
        _isGameRunning = isGameRunning ?? (() => false);
    }

    public async Task<OptionsEditStatus> SetValueAsync(
        string filePath,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (_isGameRunning())
            return OptionsEditStatus.GameRunning;
        if (!File.Exists(filePath))
            return OptionsEditStatus.MissingFile;

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var offset = hasBom ? Encoding.UTF8.Preamble.Length : 0;
        var encoding = new UTF8Encoding(hasBom, true);
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var trailingNewline = text.EndsWith("\r\n", StringComparison.Ordinal) || text.EndsWith('\n');
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        if (trailingNewline && lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var prefix = key + ":";
        var index = lines.FindIndex(line => line.StartsWith(prefix, StringComparison.Ordinal));
        var replacement = prefix + value;
        if (index >= 0)
            lines[index] = replacement;
        else
            lines.Add(replacement);

        var updated = string.Join(newline, lines) + (trailingNewline ? newline : string.Empty);
        var temporary = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        var backup = filePath + ".bak";
        try
        {
            if (!File.Exists(backup))
                File.Copy(filePath, backup);
            await File.WriteAllTextAsync(temporary, updated, encoding, cancellationToken);
            File.Move(temporary, filePath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return OptionsEditStatus.Applied;
    }
}
