namespace Basalt.Shell.ViewModels;

/// <summary>A place in the code the user was at.</summary>
public readonly record struct NavigationPoint(string FilePath, int Offset, int Line);

/// <summary>
/// Where the user has been, so they can step back and forward.
///
/// A new jump discards anything ahead, the way a browser does: the forward
/// entries described a path the user has now left.
/// </summary>
public sealed class NavigationHistory
{
    private readonly List<NavigationPoint> _points = [];
    private int _current = -1;

    /// <summary>How many places are remembered before the oldest is dropped.</summary>
    public int Capacity { get; init; } = 50;

    public bool CanGoBack => _current > 0;

    public bool CanGoForward => _current >= 0 && _current < _points.Count - 1;

    public NavigationPoint? Current =>
        _current >= 0 && _current < _points.Count ? _points[_current] : null;

    public event EventHandler? Changed;

    /// <summary>Records a place, unless it is where we already are.</summary>
    public void Record(NavigationPoint point)
    {
        // Moving within the same line is not a jump worth remembering: it would
        // fill the history with keystrokes.
        if (Current is { } current &&
            current.FilePath == point.FilePath &&
            current.Line == point.Line)
            return;

        // Anything ahead described a path the user has now left.
        if (_current < _points.Count - 1)
            _points.RemoveRange(_current + 1, _points.Count - _current - 1);

        _points.Add(point);

        while (_points.Count > Capacity) _points.RemoveAt(0);

        _current = _points.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public NavigationPoint? GoBack()
    {
        if (!CanGoBack) return null;

        _current--;
        Changed?.Invoke(this, EventArgs.Empty);
        return _points[_current];
    }

    public NavigationPoint? GoForward()
    {
        if (!CanGoForward) return null;

        _current++;
        Changed?.Invoke(this, EventArgs.Empty);
        return _points[_current];
    }

    public void Clear()
    {
        _points.Clear();
        _current = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
