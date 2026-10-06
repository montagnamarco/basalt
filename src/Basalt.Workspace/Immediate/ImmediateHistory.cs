namespace Basalt.Workspace.Immediate;

/// <summary>
/// The lines typed so far, walked with Up and Down.
///
/// Like a shell's history: Up goes back, Down comes forward, and one step
/// past the newest line is the empty line again, so the user gets back to
/// what they were typing.
/// </summary>
public sealed class ImmediateHistory
{
    private const int Capacity = 200;

    private readonly List<string> _lines = [];
    private int _position;

    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Remembers a line and puts the walk back at the end.</summary>
    public void Add(string line)
    {
        var text = line.Trim();

        if (text.Length == 0) return;

        // The same line twice in a row would make Up stand still.
        if (_lines.Count == 0 || _lines[^1] != text) _lines.Add(text);

        if (_lines.Count > Capacity) _lines.RemoveAt(0);

        _position = _lines.Count;
    }

    /// <summary>The line before the current one, or null at the oldest.</summary>
    public string? Previous()
    {
        if (_lines.Count == 0 || _position == 0) return null;

        _position--;
        return _lines[_position];
    }

    /// <summary>The line after the current one; empty past the newest; null when not walking.</summary>
    public string? Next()
    {
        if (_position >= _lines.Count) return null;

        _position++;
        return _position == _lines.Count ? "" : _lines[_position];
    }

    /// <summary>Stops walking, so the next Up starts from the newest line.</summary>
    public void Reset() => _position = _lines.Count;

    public void Clear()
    {
        _lines.Clear();
        _position = 0;
    }
}
