using System.Text;

namespace Basalt.Workspace.Terminal;

/// <summary>
/// Turns a terminal byte stream into plain text.
///
/// A pseudo-terminal emits ANSI escape sequences for colour, cursor movement
/// and screen clearing. Rendering them literally would fill the panel with
/// noise like "ESC[0m", so they are interpreted here. This is a text model,
/// not a full screen emulator: cursor addressing used by full-screen programs
/// is recognised and skipped rather than acted upon, which keeps line-oriented
/// tools — the ones an IDE terminal is actually used for — readable.
/// </summary>
public sealed class AnsiScreen
{
    private readonly List<StringBuilder> _lines = [new()];
    private int _row;
    private int _column;

    /// <summary>Maximum lines kept; older ones scroll away.</summary>
    public int Scrollback { get; init; } = 5000;

    public event EventHandler? Changed;

    /// <summary>Current contents as plain text.</summary>
    public string Text
    {
        get
        {
            lock (_lines) return string.Join('\n', _lines.Select(l => l.ToString()));
        }
    }

    public void Append(string data)
    {
        lock (_lines) Process(data);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lines)
        {
            _lines.Clear();
            _lines.Add(new StringBuilder());
            _row = 0;
            _column = 0;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Process(string data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];

            switch (c)
            {
                case '\x1b':
                    i = SkipEscapeSequence(data, i);
                    continue;

                case '\r':
                    // Carriage return alone rewrites the current line, which is
                    // how progress indicators update in place.
                    _column = 0;
                    continue;

                case '\n':
                    NewLine();
                    continue;

                case '\b':
                    if (_column > 0) _column--;
                    continue;

                case '\t':
                    // Tab stops every eight columns, as terminals do.
                    var target = (_column / 8 + 1) * 8;
                    while (_column < target) WriteCharacter(' ');
                    continue;

                case '\a':
                    // Bell: nothing to show.
                    continue;
            }

            // Remaining control characters would render as boxes.
            if (!char.IsControl(c)) WriteCharacter(c);
        }
    }

    private void WriteCharacter(char c)
    {
        var line = _lines[_row];

        // Writing past the end extends the line; writing inside it overwrites,
        // which is what a terminal does after a carriage return.
        if (_column < line.Length) line[_column] = c;
        else
        {
            while (line.Length < _column) line.Append(' ');
            line.Append(c);
        }

        _column++;
    }

    private void NewLine()
    {
        _row++;
        _column = 0;

        if (_row >= _lines.Count) _lines.Add(new StringBuilder());

        // Drop the oldest lines once the buffer is full.
        while (_lines.Count > Scrollback)
        {
            _lines.RemoveAt(0);
            _row--;
        }
    }

    /// <summary>
    /// Consumes an escape sequence and returns the index of its last character.
    ///
    /// Only the effects that matter for readable output are applied: erase
    /// commands. Colour and cursor addressing are recognised and discarded.
    /// </summary>
    private int SkipEscapeSequence(string data, int start)
    {
        if (start + 1 >= data.Length) return start;

        var next = data[start + 1];

        // CSI: ESC [ parameters intermediate final
        if (next == '[')
        {
            var i = start + 2;
            var parameters = new StringBuilder();

            while (i < data.Length && (char.IsDigit(data[i]) || data[i] is ';' or '?' or ' '))
            {
                parameters.Append(data[i]);
                i++;
            }

            if (i >= data.Length) return data.Length - 1;

            ApplyCsi(data[i], parameters.ToString());
            return i;
        }

        // OSC: ESC ] ... terminated by BEL or ESC \, used for window titles.
        if (next == ']')
        {
            var i = start + 2;
            while (i < data.Length && data[i] != '\a')
            {
                if (data[i] == '\x1b' && i + 1 < data.Length && data[i + 1] == '\\') return i + 1;
                i++;
            }
            return Math.Min(i, data.Length - 1);
        }

        // Two-character sequences such as ESC = and ESC >.
        return start + 1;
    }

    private void ApplyCsi(char final, string parameters)
    {
        switch (final)
        {
            case 'J':
                // Erase display: "2" clears the whole screen.
                if (parameters is "2" or "3") Clear();
                break;

            case 'K':
                // Erase in line, from the cursor to the end.
                var line = _lines[_row];
                if (parameters is "" or "0" && _column < line.Length)
                    line.Remove(_column, line.Length - _column);
                else if (parameters == "2")
                    line.Clear();
                break;

            // Colour, cursor movement and mode changes carry no text.
        }
    }
}
