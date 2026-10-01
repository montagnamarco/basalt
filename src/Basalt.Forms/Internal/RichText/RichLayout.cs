using System.Drawing;
using System.Windows.Forms.Internal.Text;

namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// Where the runs of a <see cref="RichDocument"/> go: lines broken at
/// paragraphs and, when wrapping, at words, each line split into the pieces
/// that share one character format so painting can change font and colour
/// mid-line. Hit testing and the caret use the same geometry painting does.
/// </summary>
internal sealed class RichLayout
{
    /// <summary>One run's worth of a line: its own font, colour and caret stops, relative to the line's left edge.</summary>
    public sealed record Piece(int Start, string Text, CharacterFormat Format, float[] Stops)
    {
        public float Width => Stops[^1];

        public float X(int index) => Stops[Math.Clamp(index - Start, 0, Stops.Length - 1)];
    }

    public sealed record Line(int Start, int End, float Y, float Height, float Indent, float Width, ParagraphFormat Paragraph, IReadOnlyList<Piece> Pieces)
    {
        /// <summary>The x of the caret before character <paramref name="index"/>, the line's indent included.</summary>
        public float X(int index)
        {
            foreach (var piece in Pieces)
            {
                if (index <= piece.Start + piece.Text.Length) return Indent + PieceLeft(piece) + piece.X(index);
            }

            return Indent + Width;
        }

        private float PieceLeft(Piece target)
        {
            var left = 0f;
            foreach (var piece in Pieces)
            {
                if (piece == target) return left;

                left += piece.Width;
            }

            return left;
        }
    }

    private const int BulletGlyphWidth = 14;

    public IReadOnlyList<Line> Lines { get; private set; } = [];

    public float LineHeight { get; private set; } = 1;

    public float TotalHeight { get; private set; }

    public float TotalWidth { get; private set; }

    /// <summary>Lays out the whole document; <paramref name="wrapWidth"/> zero or less means no wrapping.</summary>
    public void Layout(RichDocument document, int wrapWidth, float zoom = 1f)
    {
        using var fonts = new RichFonts();

        var lines = new List<Line>();
        var y = 0f;
        var maxWidth = 0f;
        var tallest = 1f;

        foreach (var paragraph in document.Paragraphs())
        {
            var hasBreak = paragraph.Runs.Count > 0 && paragraph.Runs[^1].IsParagraphBreak;
            var contentRuns = hasBreak ? paragraph.Runs.Take(paragraph.Runs.Count - 1).ToList() : [.. paragraph.Runs];

            var chars = BuildCharacterTable(contentRuns, paragraph.Start, fonts, zoom);
            var bulletWidth = paragraph.Format.Bullet ? BulletGlyphWidth + paragraph.Format.BulletIndent : 0;

            if (chars.Count == 0)
            {
                var emptyFormat = hasBreak ? paragraph.Runs[^1].Format : document.DefaultFormat;
                var height = fonts.Get(emptyFormat, zoom).Height;
                lines.Add(new Line(paragraph.Start, paragraph.Start, y, height, paragraph.Format.Indent, 0, paragraph.Format, []));
                y += height;
                tallest = Math.Max(tallest, height);
                continue;
            }

            var stops = new float[chars.Count + 1];
            for (var i = 0; i < chars.Count; i++) stops[i + 1] = stops[i] + chars[i].Advance;

            var offset = 0;
            var isFirstLine = true;

            while (offset < chars.Count)
            {
                var indent = isFirstLine ? Math.Max(paragraph.Format.Indent - paragraph.Format.HangingIndent, 0) + bulletWidth : paragraph.Format.Indent;
                var available = wrapWidth > 0 ? Math.Max(wrapWidth - indent, 1) : float.MaxValue;
                var baseX = stops[offset];

                var fit = offset;
                while (fit < chars.Count && stops[fit + 1] - baseX <= available) fit++;

                var end = fit;
                if (end < chars.Count)
                {
                    var breakAt = end;
                    while (breakAt > offset && chars[breakAt - 1].Character != ' ') breakAt--;
                    end = breakAt > offset ? breakAt : Math.Max(end, offset + 1);
                }

                var line = BuildLine(chars, offset, end, y, indent, paragraph.Format, stops, fonts, zoom);

                // A centred or right-aligned paragraph shifts its line within what word wrap left available,
                // on top of (not instead of) the indent: the indent still marks where its margin sits.
                if (paragraph.Format.Alignment != HorizontalAlignment.Left)
                {
                    var availableWidth = wrapWidth > 0 ? Math.Max(wrapWidth - indent - paragraph.Format.RightIndent, 0) : line.Width;
                    var shift = paragraph.Format.Alignment == HorizontalAlignment.Center
                        ? Math.Max((availableWidth - line.Width) / 2, 0)
                        : Math.Max(availableWidth - line.Width, 0);

                    if (shift > 0) line = line with { Indent = line.Indent + shift };
                }

                lines.Add(line);

                maxWidth = Math.Max(maxWidth, indent + line.Width);
                y += line.Height;
                tallest = Math.Max(tallest, line.Height);
                offset = end;
                isFirstLine = false;
            }
        }

        if (lines.Count == 0)
        {
            var height = fonts.Get(document.DefaultFormat, zoom).Height;
            lines.Add(new Line(0, 0, 0, height, 0, 0, document.FinalParagraph, []));
            y = height;
            tallest = height;
        }

        Lines = lines;
        LineHeight = tallest;
        TotalHeight = y;
        TotalWidth = maxWidth;
    }

    private sealed record CharEntry(int Position, char Character, float Advance, CharacterFormat Format, int RunIndex);

    private static List<CharEntry> BuildCharacterTable(List<RichRun> runs, int start, RichFonts fonts, float zoom)
    {
        var chars = new List<CharEntry>();
        var pos = start;

        for (var r = 0; r < runs.Count; r++)
        {
            var font = fonts.Get(runs[r].Format, zoom);
            foreach (var character in runs[r].Text)
            {
                chars.Add(new CharEntry(pos, character, AdvanceWidths.Of(font, character), runs[r].Format, r));
                pos++;
            }
        }

        return chars;
    }

    private static Line BuildLine(List<CharEntry> chars, int start, int end, float y, float indent, ParagraphFormat paragraph, float[] stops, RichFonts fonts, float zoom)
    {
        var pieces = new List<Piece>();
        var height = 1f;
        var segmentStart = start;

        for (var i = start + 1; i <= end; i++)
        {
            if (i < end && chars[i].RunIndex == chars[segmentStart].RunIndex) continue;

            var text = new string(chars.GetRange(segmentStart, i - segmentStart).Select(c => c.Character).ToArray());
            var localStops = new float[i - segmentStart + 1];
            for (var k = 0; k < i - segmentStart; k++) localStops[k + 1] = stops[segmentStart + k + 1] - stops[segmentStart];

            pieces.Add(new Piece(chars[segmentStart].Position, text, chars[segmentStart].Format, localStops));
            segmentStart = i;
        }

        foreach (var piece in pieces) height = Math.Max(height, fonts.Get(piece.Format, zoom).Height);

        var width = stops[end] - stops[start];
        var lineStart = chars[start].Position;
        var lineEnd = chars[end - 1].Position + 1;

        return new Line(lineStart, lineEnd, y, height, indent, width, paragraph, pieces);
    }

    public int LineOf(int index)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            var nextStart = i + 1 < Lines.Count ? Lines[i + 1].Start : int.MaxValue;
            if (index < nextStart) return i;
        }

        return Math.Max(Lines.Count - 1, 0);
    }

    public PointF PositionOf(int index)
    {
        if (Lines.Count == 0) return PointF.Empty;

        var lineIndex = LineOf(index);
        var line = Lines[lineIndex];

        return new PointF(line.X(index), line.Y);
    }

    public int IndexAt(PointF point)
    {
        if (Lines.Count == 0) return 0;

        var lineIndex = 0;
        for (var i = 0; i < Lines.Count; i++)
        {
            if (point.Y < Lines[i].Y + Lines[i].Height) { lineIndex = i; break; }

            lineIndex = i;
        }

        return IndexInLine(lineIndex, point.X);
    }

    public int IndexInLine(int lineIndex, float x)
    {
        lineIndex = Math.Clamp(lineIndex, 0, Lines.Count - 1);
        var line = Lines[lineIndex];

        foreach (var piece in line.Pieces)
        {
            var pieceLeft = line.X(piece.Start) - line.Indent;
            for (var i = 0; i < piece.Text.Length; i++)
            {
                var left = line.Indent + pieceLeft + piece.Stops[i];
                var right = line.Indent + pieceLeft + piece.Stops[i + 1];
                if (x < (left + right) / 2) return piece.Start + i;
            }
        }

        return line.End;
    }
}
