using System.Drawing;
using System.Text;

namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// Reads the RTF subset <see cref="RtfWriter"/> writes, and tolerates the
/// rest of the format well enough not to crash on it: unknown control words
/// are skipped, ignorable destinations ({\*...}) are skipped whole, and
/// groups push and pop the character formatting in effect, as real RTF does.
/// </summary>
internal static class RtfReader
{
    private enum TokenType { EndOfFile, Open, Close, Word, Symbol, Hex, Text }

    private readonly record struct Token(TokenType Type, string Text, int? Number);

    /// <summary>Parses <paramref name="rtf"/> into a fresh document; plain text (no \rtf1 header) becomes one paragraph of plain text.</summary>
    public static RichDocument Read(string rtf, CharacterFormat defaultFormat)
    {
        var document = new RichDocument(defaultFormat);

        if (!rtf.TrimStart().StartsWith('{'))
        {
            document.SetPlainText(rtf);
            return document;
        }

        var runs = new List<RichRun>();
        var fontTable = new Dictionary<int, string> { [0] = defaultFormat.FontFamily };
        var colorTable = new List<Color>();

        var pos = 0;
        var currentFormat = defaultFormat;
        var currentParagraph = new ParagraphFormat();
        var pendingText = new StringBuilder();

        void Flush()
        {
            if (pendingText.Length == 0) return;

            runs.Add(new RichRun(pendingText.ToString(), currentFormat));
            pendingText.Clear();
        }

        var formatStack = new Stack<CharacterFormat>();
        var skipStack = new Stack<int>();
        var unicodeSkip = 1;

        while (true)
        {
            var token = NextToken(rtf, ref pos);
            if (token.Type == TokenType.EndOfFile) break;

            switch (token.Type)
            {
                case TokenType.Open:
                {
                    var lookahead = pos;
                    var next = NextToken(rtf, ref lookahead);

                    if (next is { Type: TokenType.Word, Text: "fonttbl" })
                    {
                        pos = lookahead;
                        ReadFontTable(rtf, ref pos, fontTable);
                    }
                    else if (next is { Type: TokenType.Word, Text: "colortbl" })
                    {
                        pos = lookahead;
                        ReadColorTable(rtf, ref pos, colorTable);
                    }
                    else if (next is { Type: TokenType.Word, Text: "pntext" } or { Type: TokenType.Symbol, Text: "*" })
                    {
                        Flush();

                        // Only {\pntext ...} marks a bullet; any other ignorable group leaves the paragraph as it is.
                        if (next is { Type: TokenType.Word, Text: "pntext" }) currentParagraph.Bullet = true;
                        pos = lookahead;
                        SkipGroup(rtf, ref pos);
                    }
                    else
                    {
                        Flush();
                        formatStack.Push(currentFormat);
                        skipStack.Push(unicodeSkip);
                    }

                    break;
                }

                case TokenType.Close:
                    Flush();
                    if (formatStack.Count > 0) currentFormat = formatStack.Pop();
                    if (skipStack.Count > 0) unicodeSkip = skipStack.Pop();
                    break;

                case TokenType.Text:
                    pendingText.Append(token.Text);
                    break;

                case TokenType.Hex:
                    if (token.Number is { } code) pendingText.Append((char)code);
                    break;

                // \ucN: how many fallback characters follow each \uN, scoped to the group like a format.
                case TokenType.Word when token.Text == "uc":
                    unicodeSkip = Math.Max(token.Number ?? 1, 0);
                    break;

                // \uN is a UTF-16 code unit (negative above 32767, as RTF parameters are signed 16-bit);
                // a surrogate pair arrives as two \u escapes, as our writer emits them.
                case TokenType.Word when token.Text == "u":
                {
                    var value = token.Number ?? 0;
                    pendingText.Append((char)(ushort)(value < 0 ? value + 65536 : value));
                    SkipFallback(rtf, ref pos, unicodeSkip);
                    break;
                }

                case TokenType.Word:
                    currentFormat = ApplyWord(rtf, ref pos, token, currentFormat, currentParagraph, fontTable, colorTable, Flush, out var paragraphBreak, out var literal);
                    if (literal is not null) pendingText.Append(literal);
                    if (paragraphBreak is not null)
                    {
                        runs.Add(new RichRun(token.Text == "line" ? "\n" : "\r\n", currentFormat, currentParagraph.Clone()));
                        currentParagraph = currentParagraph.Clone();
                    }

                    break;

                case TokenType.Symbol:
                    pendingText.Append(token.Text);
                    break;
            }
        }

        Flush();
        document.Load(runs, currentParagraph.Clone());
        return document;
    }

    /// <summary>Applies one control word; returns the format in effect afterwards, and a non-null clone when the word closed a paragraph.</summary>
    private static CharacterFormat ApplyWord(
        string rtf, ref int pos, Token token, CharacterFormat format, ParagraphFormat paragraph,
        Dictionary<int, string> fontTable, List<Color> colorTable, Action flush, out ParagraphFormat? paragraphBreak, out string? literal)
    {
        paragraphBreak = null;
        literal = null;
        var value = token.Number ?? 1;

        switch (token.Text)
        {
            case "par" or "line":
                flush();
                paragraphBreak = paragraph;
                return format;

            case "pard":
                flush();
                paragraph.Alignment = HorizontalAlignment.Left;
                paragraph.Indent = 0;
                paragraph.RightIndent = 0;
                paragraph.HangingIndent = 0;
                paragraph.Bullet = false;
                paragraph.Tabs = [];
                return format;

            case "ql": paragraph.Alignment = HorizontalAlignment.Left; return format;
            case "qc": paragraph.Alignment = HorizontalAlignment.Center; return format;
            case "qr": paragraph.Alignment = HorizontalAlignment.Right; return format;
            case "li": paragraph.Indent = ToPixels(value); return format;
            case "ri": paragraph.RightIndent = ToPixels(value); return format;
            case "fi": paragraph.HangingIndent = Math.Max(-ToPixels(value), 0); return format;
            case "tx": paragraph.Tabs = [.. paragraph.Tabs, ToPixels(value)]; return format;
            case "tab": literal = "\t"; return format;

            case "b": flush(); return format with { Bold = token.Number != 0 };
            case "i": flush(); return format with { Italic = token.Number != 0 };
            case "ul": flush(); return format with { Underline = token.Number != 0 };
            case "ulnone": flush(); return format with { Underline = false };
            case "strike": flush(); return format with { Strikeout = token.Number != 0 };
            case "protect": flush(); return format with { Protected = token.Number != 0 };
            case "up": flush(); return format with { CharOffset = value };
            case "dn": flush(); return format with { CharOffset = -value };
            case "fs": flush(); return format with { FontSize = value / 2f };

            case "f":
                flush();
                return fontTable.TryGetValue(value, out var family) ? format with { FontFamily = family } : format;

            case "cf":
                flush();
                return format with { Color = value <= 0 || value >= colorTable.Count ? Color.Empty : colorTable[value] };

            case "cb":
                flush();
                return format with { BackColor = value <= 0 || value >= colorTable.Count ? Color.Empty : colorTable[value] };

            case "highlight":
                flush();
                return format with { BackColor = HighlightPalette.ElementAtOrDefault(value) };

            default:
                return format; // An unrecognised control word (\rtf1, \ansi, \deff0, \viewkind4, ...) changes nothing we track.
        }
    }

    private static readonly Color[] HighlightPalette =
    [
        Color.Empty, Color.Black, Color.Blue, Color.Cyan, Color.LightGreen, Color.Magenta, Color.Red, Color.Yellow, Color.White,
    ];

    private static int ToPixels(int twips) => (int)Math.Round(twips / 15f);

    /// <summary>
    /// Skips the plain fallback a reader without Unicode would show instead of a \uN escape: as many
    /// characters as \ucN says, a \'hh escape counting as one, and never past the end of the group.
    /// </summary>
    private static void SkipFallback(string rtf, ref int pos, int count)
    {
        if (pos < rtf.Length && rtf[pos] == ' ') pos++;

        for (var skipped = 0; skipped < count && pos < rtf.Length; skipped++)
        {
            if (rtf[pos] is '{' or '}') return;

            if (rtf[pos] == '\\' && pos + 1 < rtf.Length && rtf[pos + 1] == '\'')
            {
                pos = Math.Min(pos + 4, rtf.Length);
                continue;
            }

            pos++;
        }
    }

    private static void ReadFontTable(string rtf, ref int pos, Dictionary<int, string> fontTable)
    {
        var depth = 1;

        while (depth > 0)
        {
            var token = NextToken(rtf, ref pos);
            if (token.Type == TokenType.EndOfFile) return;
            if (token.Type == TokenType.Open) { depth++; continue; }
            if (token.Type == TokenType.Close) { depth--; continue; }

            if (token.Type != TokenType.Word || !token.Text.StartsWith('f') || token.Number is not { } index) continue;

            var name = new StringBuilder();
            while (true)
            {
                var next = NextToken(rtf, ref pos);
                if (next.Type == TokenType.Close) { depth--; break; }
                if (next.Type != TokenType.Text) continue;

                var semicolon = next.Text.IndexOf(';');
                name.Append(semicolon >= 0 ? next.Text[..semicolon] : next.Text);
                if (semicolon >= 0) continue;
            }

            fontTable[index] = name.ToString().Trim();
        }
    }

    private static void ReadColorTable(string rtf, ref int pos, List<Color> colorTable)
    {
        var depth = 1;
        var red = 0;
        var green = 0;
        var blue = 0;
        var hasAny = false;

        while (depth > 0)
        {
            var token = NextToken(rtf, ref pos);
            if (token.Type == TokenType.EndOfFile) return;
            if (token.Type == TokenType.Open) { depth++; continue; }
            if (token.Type == TokenType.Close) { depth--; continue; }

            switch (token.Type)
            {
                case TokenType.Word when token.Text == "red": red = token.Number ?? 0; hasAny = true; break;
                case TokenType.Word when token.Text == "green": green = token.Number ?? 0; hasAny = true; break;
                case TokenType.Word when token.Text == "blue": blue = token.Number ?? 0; hasAny = true; break;
                case TokenType.Text:
                    foreach (var character in token.Text)
                    {
                        if (character != ';') continue;

                        colorTable.Add(hasAny ? Color.FromArgb(red, green, blue) : Color.Empty);
                        red = green = blue = 0;
                        hasAny = false;
                    }

                    break;
            }
        }
    }

    private static void SkipGroup(string rtf, ref int pos)
    {
        var depth = 1;

        while (depth > 0)
        {
            var token = NextToken(rtf, ref pos);
            if (token.Type == TokenType.EndOfFile) return;
            if (token.Type == TokenType.Open) depth++;
            else if (token.Type == TokenType.Close) depth--;
        }
    }

    private static Token NextToken(string rtf, ref int pos)
    {
        while (pos < rtf.Length && (rtf[pos] == '\r' || rtf[pos] == '\n')) pos++;
        if (pos >= rtf.Length) return new Token(TokenType.EndOfFile, string.Empty, null);

        var ch = rtf[pos];
        if (ch == '{') { pos++; return new Token(TokenType.Open, "{", null); }
        if (ch == '}') { pos++; return new Token(TokenType.Close, "}", null); }

        if (ch != '\\')
        {
            var start = pos;
            while (pos < rtf.Length && rtf[pos] is not ('\\' or '{' or '}' or '\r' or '\n')) pos++;

            return new Token(TokenType.Text, rtf[start..pos], null);
        }

        pos++;
        if (pos >= rtf.Length) return new Token(TokenType.EndOfFile, string.Empty, null);

        if (rtf[pos] == '\'')
        {
            pos++;

            // Up to two hex digits; a truncated or malformed escape yields no character instead of an exception.
            var start = pos;
            while (pos < rtf.Length && pos - start < 2 && Uri.IsHexDigit(rtf[pos])) pos++;

            var hex = rtf[start..pos];
            return new Token(TokenType.Hex, hex, hex.Length == 0 ? null : Convert.ToInt32(hex, 16));
        }

        if (!char.IsLetter(rtf[pos]))
        {
            var symbol = rtf[pos];
            pos++;
            return new Token(TokenType.Symbol, symbol.ToString(), null);
        }

        var nameStart = pos;
        while (pos < rtf.Length && char.IsLetter(rtf[pos])) pos++;
        var name = rtf[nameStart..pos];

        int? number = null;
        if (pos < rtf.Length && (rtf[pos] == '-' || char.IsDigit(rtf[pos])))
        {
            var numberStart = pos;
            if (rtf[pos] == '-') pos++;
            while (pos < rtf.Length && char.IsDigit(rtf[pos])) pos++;
            number = int.Parse(rtf[numberStart..pos]);
        }

        if (pos < rtf.Length && rtf[pos] == ' ') pos++;

        return new Token(TokenType.Word, name, number);
    }
}
