using SkiaSharp;

namespace System.Drawing.Text;

/// <summary>
/// Lays a string out the way GDI+'s DrawString does, for drawing, measuring
/// and turning into a path alike, so that the three always agree.
/// </summary>
/// <remarks>
/// The rules, in GDI+'s order: '&amp;' hotkey prefixes; paragraphs at line
/// breaks; tabs to the format's tab stops; a sixth of an em of room at each
/// end of a line unless the format is typographic; wrapping at spaces, and
/// inside a word only when the word alone is wider than the line; whole lines
/// only with LineLimit; trimming the last line that shows; alignment in the
/// rectangle, or around the point when there is no rectangle.
///
/// Not done, and not compared with Windows: vertical text (refused), and
/// right-to-left scripts, whose letters are not reordered (only the alignment
/// follows DirectionRightToLeft). Characters missing from the font are drawn
/// with a font that has them, chosen by the platform. Text is not shaped, so
/// scripts that join letters show them unjoined.
/// </remarks>
internal sealed class TextLayout : IDisposable
{
    // GDI+ draws "…" when trimming with an ellipsis; not compared with Windows.
    private const string Ellipsis = "…";

    // One Skia font per typeface the text needs, made once and disposed with the layout.
    private readonly Dictionary<SKTypeface, SKFont> _fonts = new(ReferenceEqualityComparer.Instance);

    // The face that draws each character the main face lacks, asked of the platform once.
    private readonly Dictionary<int, SKTypeface> _fallbacks = [];

    private TextLayout(Font font, float emSize, SKTypeface typeface)
    {
        Font = font;
        EmSize = emSize;
        Typeface = typeface;

        var (em, ascent, _, lineSpacing) = FontResolver.DesignMetrics(typeface);
        LineHeight = lineSpacing * emSize / em;
        Ascent = ascent * emSize / em;
    }

    public Font Font { get; }

    public float EmSize { get; }

    public SKTypeface Typeface { get; }

    public float LineHeight { get; }

    public float Ascent { get; }

    public List<Line> Lines { get; } = [];

    public SizeF Size { get; private set; }

    public int CharactersFitted { get; private set; }

    public int LinesFilled { get; private set; }

    /// <summary>The underlined character of a shown hotkey, as an index into the original string; -1 for none.</summary>
    public int HotkeyIndex { get; private set; } = -1;

    /// <summary>One laid-out line: what it shows, where, and which characters of the original string those are.</summary>
    public sealed record Line(string Text, int[] Sources, float X, float Top, float Width);

    public static TextLayout Create(string text, Font font, float emSize, RectangleF layout, StringFormat? format)
    {
        format ??= new StringFormat();

        if ((format.FormatFlags & StringFormatFlags.DirectionVertical) != 0)
        {
            throw new NotImplementedException("Vertical text (StringFormatFlags.DirectionVertical) is not implemented.");
        }

        var result = new TextLayout(font, emSize, font.FontFamily.Typeface(font.Style));
        result.Build(text, layout, format);

        return result;
    }

    /// <summary>The fonts that draw a line, run by run, with the platform's choice for characters this font lacks.</summary>
    public IEnumerable<(string Text, SKFont Font, float Offset)> Runs(Line line)
    {
        var offset = 0f;

        foreach (var (text, font) in FontRuns(line.Text))
        {
            yield return (text, font, offset);
            offset += font.MeasureText(text);
        }
    }

    private void Build(string text, RectangleF layout, StringFormat format)
    {
        var flags = format.FormatFlags;
        var padding = format.IsTypographic ? 0f : EmSize / 6f;
        var wrapWidth = layout.Width > 0 && (flags & StringFormatFlags.NoWrap) == 0 ? Math.Max(layout.Width - 2 * padding, 0) : float.PositiveInfinity;
        var measureTrailing = (flags & StringFormatFlags.MeasureTrailingSpaces) != 0;

        var (visible, sources) = ApplyHotkeyPrefix(text, format.HotkeyPrefix);

        // Lines before alignment, as (text, sources, width).
        var lines = new List<(string Text, int[] Sources)>();
        foreach (var (paragraph, paragraphSources) in Paragraphs(visible, sources))
        {
            var expanded = ExpandTabs(paragraph, paragraphSources, format);
            lines.AddRange(Wrap(expanded.Text, expanded.Sources, wrapWidth));
        }

        if (lines.Count == 0) lines.Add((string.Empty, []));

        // How many lines show: all when there is no height, whole ones with
        // LineLimit, and any that begins inside the rectangle otherwise.
        var shown = lines.Count;
        if (layout.Height > 0)
        {
            var fit = (flags & StringFormatFlags.LineLimit) != 0
                ? (int)MathF.Floor(layout.Height / LineHeight + 0.0001f)
                : (int)MathF.Ceiling(layout.Height / LineHeight - 0.0001f);

            shown = Math.Clamp(fit, (flags & StringFormatFlags.LineLimit) != 0 ? 0 : 1, lines.Count);
        }

        var trimmed = shown < lines.Count || (wrapWidth == float.PositiveInfinity && layout.Width > 0 && lines.Any(line => Measure(line.Text) > layout.Width - 2 * padding));
        if (trimmed && shown > 0 && format.Trimming != StringTrimming.None && layout.Width > 0)
        {
            // Only the last line that shows is trimmed, and only with its own
            // text: the lines cut off below are dropped, not joined to it. An
            // ellipsis says that more text followed.
            var last = shown - 1;
            var more = shown < lines.Count;
            lines[last] = Trim(lines[last].Text, lines[last].Sources, Math.Max(layout.Width - 2 * padding, 0), format.Trimming, more);
        }

        lines.RemoveRange(shown, lines.Count - shown);

        // Alignment, in the rectangle or around the point.
        var rightToLeft = (flags & StringFormatFlags.DirectionRightToLeft) != 0;
        var alignment = format.Alignment;
        if (rightToLeft) alignment = alignment switch { StringAlignment.Near => StringAlignment.Far, StringAlignment.Far => StringAlignment.Near, _ => alignment };

        var blockHeight = lines.Count * LineHeight;
        var top = format.LineAlignment switch
        {
            StringAlignment.Center => layout.Height > 0 ? layout.Y + (layout.Height - blockHeight) / 2 : layout.Y - blockHeight / 2,
            StringAlignment.Far => layout.Height > 0 ? layout.Bottom - blockHeight : layout.Y - blockHeight,
            _ => layout.Y,
        };

        var widest = 0f;
        for (var i = 0; i < lines.Count; i++)
        {
            var (lineText, lineSources) = lines[i];
            var measured = measureTrailing ? lineText : lineText.TrimEnd(' ');
            var width = Measure(measured);
            widest = Math.Max(widest, width);

            var x = alignment switch
            {
                StringAlignment.Center => layout.Width > 0 ? layout.X + (layout.Width - width) / 2 : layout.X - width / 2,
                StringAlignment.Far => layout.Width > 0 ? layout.Right - padding - width : layout.X - padding - width,
                _ => layout.X + padding,
            };

            Lines.Add(new Line(lineText, lineSources, x, top + i * LineHeight, width));
        }

        var fullWidth = widest + 2 * padding;
        Size = new SizeF(layout.Width > 0 ? Math.Min(fullWidth, layout.Width) : fullWidth, blockHeight);
        LinesFilled = lines.Count;
        CharactersFitted = lines.Count == 0 ? 0 : lines.SelectMany(line => line.Sources).DefaultIfEmpty(-1).Max() + 1;
    }

    /// <summary>
    /// Removes the '&amp;' of hotkeys ("&amp;&amp;" stays one '&amp;') and remembers the
    /// character that follows the first one, which Show underlines.
    /// </summary>
    private (string Text, int[] Sources) ApplyHotkeyPrefix(string text, HotkeyPrefix prefix)
    {
        var sources = Enumerable.Range(0, text.Length).ToArray();
        if (prefix == HotkeyPrefix.None) return (text, sources);

        var builder = new System.Text.StringBuilder(text.Length);
        var kept = new List<int>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '&' && i + 1 < text.Length)
            {
                i++;
                if (text[i] != '&' && HotkeyIndex < 0 && prefix == HotkeyPrefix.Show) HotkeyIndex = i;
            }

            builder.Append(text[i]);
            kept.Add(i);
        }

        return (builder.ToString(), kept.ToArray());
    }

    private static IEnumerable<(string Text, int[] Sources)> Paragraphs(string text, int[] sources)
    {
        var start = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n' && text[i] != '\r') continue;

            yield return (text[start..i], sources[start..i]);

            if (i < text.Length && text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;

            start = i + 1;
        }
    }

    /// <summary>
    /// Each tab reaches the next tab stop. Without tab stops, GDI+ draws nothing
    /// for a tab; that has not been compared with Windows, so the same width as
    /// four spaces is used.
    /// </summary>
    private (string Text, int[] Sources) ExpandTabs(string text, int[] sources, StringFormat format)
    {
        if (!text.Contains('\t')) return (text, sources);

        var space = Measure(" ");
        var builder = new System.Text.StringBuilder();
        var kept = new List<int>();

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\t')
            {
                builder.Append(text[i]);
                kept.Add(sources[i]);
                continue;
            }

            var position = Measure(builder.ToString());
            var target = NextTabStop(position, format, space);
            var count = Math.Max(1, (int)MathF.Round((target - position) / Math.Max(space, 0.001f)));

            for (var k = 0; k < count; k++)
            {
                builder.Append(' ');
                kept.Add(sources[i]);
            }
        }

        return (builder.ToString(), kept.ToArray());
    }

    private static float NextTabStop(float position, StringFormat format, float space)
    {
        var stops = format.TabStopsOrEmpty;
        if (stops.Length == 0 || stops.All(stop => stop <= 0))
        {
            var width = 4 * space;
            return (MathF.Floor(position / width) + 1) * width;
        }

        var at = format.FirstTabOffset;
        var index = 0;
        while (at <= position)
        {
            at += stops[Math.Min(index, stops.Length - 1)];
            index++;
            if (stops[Math.Min(index - 1, stops.Length - 1)] <= 0) break;
        }

        return at;
    }

    private IEnumerable<(string Text, int[] Sources)> Wrap(string text, int[] sources, float width)
    {
        var advances = Advances(text);

        // The width of text[start..end], trailing spaces left out as GDI+ does when breaking.
        float WidthOf(int start, int end)
        {
            while (end > start && text[end - 1] == ' ') end--;

            var sum = 0f;
            for (var k = start; k < end; k++) sum += advances[k];

            return sum;
        }

        if (float.IsPositiveInfinity(width) || WidthOf(0, text.Length) <= width)
        {
            yield return (text, sources);
            yield break;
        }

        var lineStart = 0;
        while (lineStart < text.Length)
        {
            var end = lineStart;
            var lastBreak = -1;
            var running = 0f;
            var trailingSpaces = 0f;

            while (end < text.Length)
            {
                var advance = advances[end];
                var isSpace = text[end] == ' ';
                var widthIfAdded = isSpace ? running : running + trailingSpaces + advance;

                if (!isSpace && widthIfAdded > width) break;

                if (isSpace)
                {
                    trailingSpaces += advance;
                    lastBreak = end + 1;
                }
                else
                {
                    running = widthIfAdded;
                    trailingSpaces = 0;
                }

                end++;
            }

            if (end == text.Length)
            {
                yield return (text[lineStart..], sources[lineStart..]);
                yield break;
            }

            // Break after the last space; inside the word only when there is none.
            var cut = lastBreak > lineStart ? lastBreak : Math.Max(end, lineStart + 1);

            // Spaces at the break stay at the end of the line they follow.
            while (cut < text.Length && text[cut] == ' ') cut++;

            yield return (text[lineStart..cut], sources[lineStart..cut]);
            lineStart = cut;
        }
    }

    private (string Text, int[] Sources) Trim(string text, int[] sources, float width, StringTrimming trimming, bool more)
    {
        // A line that fits and has nothing after it stays as it is.
        if (!more && Measure(text.TrimEnd(' ')) <= width) return (text, sources);

        var withEllipsis = trimming is StringTrimming.EllipsisCharacter or StringTrimming.EllipsisWord or StringTrimming.EllipsisPath;
        var suffix = withEllipsis ? Ellipsis : string.Empty;
        var room = width - (withEllipsis ? Measure(Ellipsis) : 0);

        if (trimming == StringTrimming.EllipsisPath)
        {
            // Keep the end, which names the file, and as much of the start as fits.
            var lastSeparator = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
            var end = lastSeparator >= 0 ? text[lastSeparator..] : string.Empty;
            var head = text[..(lastSeparator >= 0 ? lastSeparator : text.Length)];

            while (head.Length > 0 && Measure(head + Ellipsis + end) > width) head = head[..^1];

            var pathText = head + Ellipsis + end;
            var pathSources = sources[..head.Length].Concat(Enumerable.Repeat(sources.Length > 0 ? sources[^1] : 0, Ellipsis.Length + end.Length)).ToArray();

            return (pathText, pathSources);
        }

        var advances = Advances(text);
        var count = 0;
        var used = 0f;
        while (count < text.Length && used + advances[count] <= room)
        {
            used += advances[count];
            count++;
        }

        if (trimming is StringTrimming.Word or StringTrimming.EllipsisWord)
        {
            var space = text.LastIndexOf(' ', Math.Max(count - 1, 0));
            if (space > 0 && count < text.Length) count = space;
        }

        var kept = text[..count].TrimEnd(' ');
        var keptSources = sources[..kept.Length];

        return (kept + suffix, keptSources.Concat(Enumerable.Repeat(keptSources.Length > 0 ? keptSources[^1] : 0, suffix.Length)).ToArray());
    }

    /// <summary>The width of a string in the layout's units.</summary>
    public float Measure(string text) => Advances(text).Sum();

    /// <summary>
    /// The advance of each character: how far the next one starts after it.
    /// Text is not shaped, so these add up exactly to the width of any run of
    /// them; the second half of a surrogate pair advances by nothing.
    /// </summary>
    private float[] Advances(string text)
    {
        var advances = new float[text.Length];
        var at = 0;

        foreach (var (run, font) in FontRuns(text))
        {
            var glyphs = font.GetGlyphs(run);
            var widths = font.GetGlyphWidths(glyphs);

            // Map glyph widths back to characters: one glyph per code point.
            var glyph = 0;
            for (var k = 0; k < run.Length; k++)
            {
                advances[at + k] = glyph < widths.Length ? widths[glyph] : 0f;
                glyph++;

                if (char.IsHighSurrogate(run[k]) && k + 1 < run.Length)
                {
                    k++;
                    advances[at + k] = 0f;
                }
            }

            at += run.Length;
        }

        return advances;
    }

    /// <summary>
    /// The string cut into runs of one font: this one, or the one the platform
    /// picks for characters this font does not have.
    /// </summary>
    private IEnumerable<(string Text, SKFont Font)> FontRuns(string text)
    {
        if (text.Length == 0) yield break;

        var start = 0;
        SKTypeface? current = null;

        for (var i = 0; i < text.Length; i++)
        {
            var codepoint = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text, i) : text[i];
            var face = FaceFor(codepoint, text[i]);

            if (current is not null && !ReferenceEquals(face, current))
            {
                yield return (text[start..i], FontFor(current));
                start = i;
            }

            current = face;

            if (char.IsSurrogatePair(text, i)) i++;
        }

        yield return (text[start..], FontFor(current!));
    }

    private SKTypeface FaceFor(int codepoint, char character)
    {
        if (char.IsWhiteSpace(character) || Typeface.ContainsGlyph(codepoint)) return Typeface;

        if (!_fallbacks.TryGetValue(codepoint, out var face))
        {
            face = SKFontManager.Default.MatchCharacter(Typeface.FamilyName, Typeface.FontStyle, null, codepoint) ?? Typeface;
            _fallbacks[codepoint] = face;
        }

        return face;
    }

    private SKFont FontFor(SKTypeface face)
    {
        if (_fonts.TryGetValue(face, out var font)) return font;

        font = Font.ToSkia(EmSize);
        if (!ReferenceEquals(face, Typeface)) font.Typeface = face;

        _fonts[face] = font;
        return font;
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();

        _fonts.Clear();
    }
}
