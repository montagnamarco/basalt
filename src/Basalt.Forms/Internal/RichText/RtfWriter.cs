using System.Drawing;
using System.Globalization;
using System.Text;

namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// Writes the RTF subset the contract asks for: fonttbl, colortbl, the
/// character properties \b \i \ul \strike \fs \cf \cb \f \up \dn \protect,
/// the paragraph properties \pard \ql \qc \qr \li \ri \fi \tx, a \pntext
/// bullet marker, \tab, and \uN/\'hh escapes for anything outside printable
/// ASCII. No OLE objects, tables or images: this writer only ever sees text runs.
/// </summary>
internal static class RtfWriter
{
    /// <summary>1/1440 of an inch, RTF's unit for indents and tab stops; WinForms' own indent properties are pixels at 96 DPI.</summary>
    private const int TwipsPerPixel = 15;

    public static string Write(RichDocument document) => Write(document.Paragraphs(), document.DefaultFormat);

    /// <summary>The RTF of one range, for SelectedRtf: the same grammar, standing alone as its own little document.</summary>
    public static string WriteRange(RichDocument document, int start, int length)
    {
        document.SplitAt(start);
        document.SplitAt(start + length);

        var paragraphs = new List<RichDocument.Paragraph>();
        foreach (var paragraph in document.Paragraphs())
        {
            if (paragraph.End <= start || paragraph.Start >= start + length) continue;

            var runs = paragraph.Runs.Where(run => RunStart(paragraph, run) >= start && RunStart(paragraph, run) + run.Length <= start + length).ToList();
            paragraphs.Add(new RichDocument.Paragraph(paragraph.Start, paragraph.End, runs, paragraph.Format));
        }

        return Write(paragraphs, document.DefaultFormat);
    }

    private static int RunStart(RichDocument.Paragraph paragraph, RichRun run)
    {
        var pos = paragraph.Start;
        foreach (var candidate in paragraph.Runs)
        {
            if (candidate == run) return pos;

            pos += candidate.Length;
        }

        return pos;
    }

    private static string Write(IReadOnlyList<RichDocument.Paragraph> paragraphs, CharacterFormat defaultFormat)
    {
        var fontTable = new List<string> { defaultFormat.FontFamily };
        var colorTable = new List<Color> { Color.Empty };
        CollectTables(paragraphs, fontTable, colorTable);

        var text = new StringBuilder();
        text.Append("{\\rtf1\\ansi\\deff0\\uc1\r\n");

        text.Append("{\\fonttbl");
        for (var i = 0; i < fontTable.Count; i++) text.Append($"{{\\f{i} {EscapeControlChars(fontTable[i])};}}");
        text.Append("}\r\n");

        text.Append("{\\colortbl;");
        for (var i = 1; i < colorTable.Count; i++) text.Append($"\\red{colorTable[i].R}\\green{colorTable[i].G}\\blue{colorTable[i].B};");
        text.Append("}\r\n");

        CharacterFormat? last = null;

        foreach (var paragraph in paragraphs)
        {
            WriteParagraphProperties(text, paragraph.Format);
            if (paragraph.Format.Bullet) text.Append("{\\pntext\\'b7\\tab}");

            var hasBreak = false;

            foreach (var run in paragraph.Runs)
            {
                if (run.IsParagraphBreak) { hasBreak = true; continue; }

                WriteCharacterProperties(text, run.Format, last, fontTable, colorTable);
                EncodeText(text, run.Text);
                last = run.Format;
            }

            // RichDocument.Paragraphs() always ends with one virtual paragraph that carries no break of its
            // own (the text after the last \r\n, or the whole document when there is none): writing \par for
            // it too would make the reader manufacture a trailing line break that was never in the text.
            if (hasBreak) text.Append("\\par\r\n");
        }

        text.Append('}');
        return text.ToString();
    }

    private static void CollectTables(IReadOnlyList<RichDocument.Paragraph> paragraphs, List<string> fontTable, List<Color> colorTable)
    {
        foreach (var paragraph in paragraphs)
        {
            foreach (var run in paragraph.Runs)
            {
                if (!fontTable.Contains(run.Format.FontFamily)) fontTable.Add(run.Format.FontFamily);
                if (run.Format.Color != Color.Empty && !colorTable.Contains(run.Format.Color)) colorTable.Add(run.Format.Color);
                if (run.Format.BackColor != Color.Empty && !colorTable.Contains(run.Format.BackColor)) colorTable.Add(run.Format.BackColor);
            }
        }
    }

    private static void WriteParagraphProperties(StringBuilder text, ParagraphFormat format)
    {
        text.Append("\\pard");
        text.Append(format.Alignment switch { HorizontalAlignment.Center => "\\qc", HorizontalAlignment.Right => "\\qr", _ => "\\ql" });
        text.Append($"\\li{format.Indent * TwipsPerPixel}");
        text.Append($"\\ri{format.RightIndent * TwipsPerPixel}");
        text.Append($"\\fi{-format.HangingIndent * TwipsPerPixel}");
        foreach (var tab in format.Tabs) text.Append($"\\tx{tab * TwipsPerPixel}");
        text.Append(' ');
    }

    private static void WriteCharacterProperties(StringBuilder text, CharacterFormat format, CharacterFormat? last, List<string> fontTable, List<Color> colorTable)
    {
        var start = text.Length;

        if (last is null || format.FontFamily != last.FontFamily) text.Append($"\\f{fontTable.IndexOf(format.FontFamily)}");
        if (last is null || format.FontSize != last.FontSize) text.Append(CultureInfo.InvariantCulture, $"\\fs{(int)Math.Round(format.FontSize * 2)}");
        if (last is null || format.Bold != last.Bold) text.Append(format.Bold ? "\\b1" : "\\b0");
        if (last is null || format.Italic != last.Italic) text.Append(format.Italic ? "\\i1" : "\\i0");
        if (last is null || format.Underline != last.Underline) text.Append(format.Underline ? "\\ul1" : "\\ulnone");
        if (last is null || format.Strikeout != last.Strikeout) text.Append(format.Strikeout ? "\\strike1" : "\\strike0");
        if (last is null || format.Color != last.Color) text.Append($"\\cf{(format.Color == Color.Empty ? 0 : colorTable.IndexOf(format.Color))}");
        if (last is null || format.BackColor != last.BackColor) text.Append($"\\cb{(format.BackColor == Color.Empty ? 0 : colorTable.IndexOf(format.BackColor))}");
        if (last is null || format.CharOffset != last.CharOffset) text.Append(format.CharOffset switch { 0 => "\\up0\\dn0", > 0 => $"\\up{format.CharOffset}", _ => $"\\dn{-format.CharOffset}" });
        if (last is null || format.Protected != last.Protected) text.Append(format.Protected ? "\\protect1" : "\\protect0");

        // A space the tokenizer consumes as the last control word's own delimiter: without it, a word's
        // digits would run straight into the text that follows. Omitted when nothing was written at all,
        // so that two runs with identical formatting do not gain a stray space between their texts.
        if (text.Length > start) text.Append(' ');
    }

    private static void EncodeText(StringBuilder text, string value)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\' or '{' or '}':
                    text.Append('\\').Append(character);
                    break;

                case '\t':
                    text.Append("\\tab ");
                    break;

                case <= (char)127:
                    text.Append(character);
                    break;

                default:
                    // \uc1 (written once, at the head of the document) says one plain character follows as the legacy fallback.
                    text.Append(CultureInfo.InvariantCulture, $"\\u{(short)character} ?");
                    break;
            }
        }
    }

    private static string EscapeControlChars(string value) => value.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
}
