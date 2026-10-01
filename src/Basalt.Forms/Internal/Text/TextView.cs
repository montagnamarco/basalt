using System.Drawing;

namespace System.Windows.Forms.Internal.Text;

/// <summary>
/// Where the characters of an edit control's text go: the lines (broken at
/// line breaks, and at words when wrapping) and the x of every caret stop,
/// measured with the font's advance widths so that hit testing and drawing
/// agree to the pixel.
/// </summary>
internal sealed class TextView
{
    public sealed record Line(int Start, int Length, float[] Stops)
    {
        /// <summary>The x of the caret before character <paramref name="index"/> of the text, the line's own included.</summary>
        public float X(int index) => Stops[Math.Clamp(index - Start, 0, Stops.Length - 1)];

        public float Width => Stops[^1];

        public int End => Start + Length;
    }

    private readonly List<Line> _lines = [];

    public IReadOnlyList<Line> Lines => _lines;

    public int LineHeight { get; private set; }

    /// <summary>Lays the text out; <paramref name="wrapWidth"/> zero means no wrapping.</summary>
    public void Layout(string text, Font font, bool multiline, int wrapWidth, char passwordChar)
    {
        _lines.Clear();
        LineHeight = font.Height;

        var display = passwordChar == '\0' ? text : new string(passwordChar, text.Length);

        if (!multiline)
        {
            _lines.Add(new Line(0, text.Length, Advances(display, 0, display.Length, font)));
            return;
        }

        // Lines break at CR LF, at LF and at a lone CR, as the Lines property splits them.
        var lineStart = 0;
        var index = 0;
        while (index <= text.Length)
        {
            if (index < text.Length && text[index] != '\n' && text[index] != '\r')
            {
                index++;
                continue;
            }

            AddWrapped(display, lineStart, index - lineStart, font, wrapWidth);

            if (index == text.Length) break;

            var breakLength = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            index += breakLength;
            lineStart = index;
        }
    }

    /// <summary>One logical line, broken at the last space that fits when wrapping.</summary>
    private void AddWrapped(string text, int start, int length, Font font, int wrapWidth)
    {
        var stops = Advances(text, start, length, font);

        if (wrapWidth <= 0 || stops[^1] <= wrapWidth)
        {
            _lines.Add(new Line(start, length, stops));
            return;
        }

        var offset = 0;
        while (offset < length)
        {
            var fit = offset;
            while (fit < length && stops[fit + 1] - stops[offset] <= wrapWidth) fit++;

            if (fit == length)
            {
                _lines.Add(new Line(start + offset, length - offset, Shift(stops, offset, length)));
                return;
            }

            // Break after the last space that fits; a word longer than the line breaks anywhere.
            var breakAt = fit;
            while (breakAt > offset && text[start + breakAt - 1] != ' ') breakAt--;
            if (breakAt == offset) breakAt = Math.Max(fit, offset + 1);

            _lines.Add(new Line(start + offset, breakAt - offset, Shift(stops, offset, breakAt)));
            offset = breakAt;
        }
    }

    private static float[] Shift(float[] stops, int from, int to)
    {
        var shifted = new float[to - from + 1];
        for (var i = from; i <= to; i++) shifted[i - from] = stops[i] - stops[from];

        return shifted;
    }

    /// <summary>The caret stops of a run: 0, then the sum of the advances so far.</summary>
    private static float[] Advances(string text, int start, int length, Font font)
    {
        var stops = new float[length + 1];
        var x = 0f;

        for (var i = 0; i < length; i++)
        {
            x += AdvanceWidths.Of(font, text[start + i]);
            stops[i + 1] = x;
        }

        return stops;
    }

    /// <summary>The line that holds the character index; at a wrap, the caret belongs to the next line.</summary>
    public int LineOf(int index)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            var nextStart = i + 1 < _lines.Count ? _lines[i + 1].Start : int.MaxValue;
            if (index < nextStart) return i;
        }

        return Math.Max(_lines.Count - 1, 0);
    }

    /// <summary>The character index nearest to a point in text coordinates.</summary>
    public int IndexAt(PointF point)
    {
        if (_lines.Count == 0) return 0;

        var lineIndex = Math.Clamp((int)Math.Floor(point.Y / Math.Max(LineHeight, 1)), 0, _lines.Count - 1);

        return IndexInLine(lineIndex, point.X);
    }

    /// <summary>The caret stop on a line nearest to an x, rounding at the middle of each character.</summary>
    public int IndexInLine(int lineIndex, float x)
    {
        var line = _lines[lineIndex];

        for (var i = 0; i < line.Length; i++)
        {
            var middle = (line.Stops[i] + line.Stops[i + 1]) / 2;
            if (x < middle) return line.Start + i;
        }

        return line.End;
    }

    public PointF PositionOf(int index)
    {
        if (_lines.Count == 0) return PointF.Empty;

        var lineIndex = LineOf(index);
        var line = _lines[lineIndex];

        return new PointF(line.X(index), lineIndex * LineHeight);
    }
}

/// <summary>Per-font advance widths of characters, measured once each.</summary>
internal static class AdvanceWidths
{
    private static readonly Dictionary<(string Name, float Size, FontStyle Style, char Character), float> Cache = [];

    public static float Of(Font font, char character)
    {
        var key = (font.Name, font.SizeInPoints, font.Style, character);

        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var width)) return width;
        }

        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        format.HotkeyPrefix = Drawing.Text.HotkeyPrefix.None;

        var measured = Control.MeasurementGraphics.MeasureString(character.ToString(), font, new PointF(0, 0), format).Width;

        lock (Cache)
        {
            Cache[key] = measured;
        }

        return measured;
    }
}
