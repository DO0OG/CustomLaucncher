namespace CustomLauncher.Core;

/// <summary>Thread-safe ring of the newest game output lines, read out in batches for the console.</summary>
public sealed class GameLogBuffer
{
    private const int DefaultCapacity = 500;
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly int _capacity;
    private bool _changed;

    public GameLogBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public void Append(string line)
    {
        lock (_gate)
        {
            if (_lines.Count == _capacity) _lines.Dequeue();
            _lines.Enqueue(line);
            _changed = true;
        }
    }

    /// <summary>Returns the current text only if lines were appended since the last successful take.</summary>
    public bool TryTakeSnapshot(out string text)
    {
        lock (_gate)
        {
            if (!_changed)
            {
                text = string.Empty;
                return false;
            }
            _changed = false;
            text = string.Join('\n', _lines);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
            _changed = false;
        }
    }
}
