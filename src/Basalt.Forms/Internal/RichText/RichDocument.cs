namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// The formatted content of a RichTextBox: a flat list of runs whose texts,
/// concatenated, are the plain text TextBoxBase's inherited editor also
/// holds. Character formatting lives on each run; paragraph formatting on
/// the run that carries a paragraph break (or, for the last paragraph, on
/// <see cref="FinalParagraph"/>, since it has no break to carry it).
/// </summary>
internal sealed class RichDocument
{
    /// <summary>A paragraph as enumerated: its text range (the break included) and its format.</summary>
    public sealed record Paragraph(int Start, int End, IReadOnlyList<RichRun> Runs, ParagraphFormat Format);

    private readonly List<RichRun> _runs = [];

    public RichDocument(CharacterFormat defaultFormat)
    {
        DefaultFormat = defaultFormat;
        FinalParagraph = new ParagraphFormat();
    }

    /// <summary>The format new text takes when nothing more specific applies: an empty document's, or a freshly cleared one's.</summary>
    public CharacterFormat DefaultFormat { get; set; }

    /// <summary>The format of the paragraph after the last break, including the whole document when there is none.</summary>
    public ParagraphFormat FinalParagraph { get; private set; }

    public IReadOnlyList<RichRun> Runs => _runs;

    public int Length => _runs.Sum(run => run.Length);

    public string PlainText => string.Concat(_runs.Select(run => run.Text));

    public void Clear()
    {
        _runs.Clear();
        FinalParagraph = new ParagraphFormat();
    }

    /// <summary>Replaces the whole document's runs with ones read elsewhere (the RTF reader, or LoadFile); used wholesale, never merged.</summary>
    public void Load(IEnumerable<RichRun> runs, ParagraphFormat finalParagraph)
    {
        _runs.Clear();
        _runs.AddRange(runs);
        FinalParagraph = finalParagraph;
    }

    /// <summary>Inserts another document's runs (clones, so the two never alias) at <paramref name="position"/>; used for SelectedRtf.</summary>
    public void InsertFragment(int position, RichDocument fragment)
    {
        SplitAt(position);
        var insertAt = IndexStartingAt(position);
        var clones = fragment._runs.Select(run => new RichRun(run.Text, run.Format, run.ParagraphBreak?.Clone())).ToList();
        _runs.InsertRange(insertAt, clones);
    }

    /// <summary>A deep copy: the runs and their paragraph marks never alias the original's, for undo snapshots.</summary>
    public RichDocument Clone()
    {
        var clone = new RichDocument(DefaultFormat);
        clone.Load(_runs.Select(run => new RichRun(run.Text, run.Format, run.ParagraphBreak?.Clone())), FinalParagraph.Clone());
        return clone;
    }

    /// <summary>Replaces this document's content with a deep copy of another's; used to restore an undo snapshot in place.</summary>
    public void ReplaceWith(RichDocument other)
    {
        DefaultFormat = other.DefaultFormat;
        Load(other._runs.Select(run => new RichRun(run.Text, run.Format, run.ParagraphBreak?.Clone())), other.FinalParagraph.Clone());
    }

    /// <summary>Whether any character in [start, start + length) is marked protected: WinForms rejects an edit that touches one.</summary>
    public bool AnyProtected(int start, int length)
    {
        if (length <= 0) return false;

        var pos = 0;
        foreach (var run in _runs)
        {
            if (pos < start + length && pos + run.Length > start && run.Format.Protected) return true;

            pos += run.Length;
        }

        return false;
    }

    /// <summary>Replaces the whole document with unformatted text, as TextBoxBase.Text's setter does.</summary>
    public void SetPlainText(string text)
    {
        Clear();
        Insert(0, text, DefaultFormat);
    }

    /// <summary>Splits the run that spans <paramref name="position"/> in two, so edits never reformat part of an untouched run.</summary>
    public void SplitAt(int position)
    {
        if (position <= 0) return;

        var pos = 0;
        for (var i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            if (position < pos + run.Length)
            {
                var offset = position - pos;
                if (offset == 0) return;

                var first = new RichRun(run.Text[..offset], run.Format);
                var second = new RichRun(run.Text[offset..], run.Format, run.ParagraphBreak);
                _runs[i] = first;
                _runs.Insert(i + 1, second);
                return;
            }

            pos += run.Length;
        }
    }

    /// <summary>The index in <see cref="Runs"/> whose text starts exactly at <paramref name="position"/>, or <see cref="Runs"/>.Count at the end.</summary>
    private int IndexStartingAt(int position)
    {
        var pos = 0;
        for (var i = 0; i < _runs.Count; i++)
        {
            if (pos == position) return i;

            pos += _runs[i].Length;
        }

        return _runs.Count;
    }

    /// <summary>Inserts text at <paramref name="position"/>, splitting it into runs at every paragraph break it contains.</summary>
    public void Insert(int position, string text, CharacterFormat format)
    {
        if (text.Length == 0) return;

        SplitAt(position);
        var insertAt = IndexStartingAt(position);
        var paragraphFormat = GetParagraphFormatAt(position);

        var newRuns = new List<RichRun>();
        var segmentStart = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] is not ('\r' or '\n'))
            {
                i++;
                continue;
            }

            if (i > segmentStart) newRuns.Add(new RichRun(text[segmentStart..i], format));

            var breakLength = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            newRuns.Add(new RichRun(text.Substring(i, breakLength), format, paragraphFormat.Clone()));
            i += breakLength;
            segmentStart = i;
        }

        if (segmentStart < text.Length) newRuns.Add(new RichRun(text[segmentStart..], format));

        _runs.InsertRange(insertAt, newRuns);
    }

    /// <summary>Removes the text in [<paramref name="start"/>, start + <paramref name="length"/>).</summary>
    public void Remove(int start, int length)
    {
        if (length <= 0) return;

        SplitAt(start);
        SplitAt(start + length);

        var pos = 0;
        var i = 0;
        while (i < _runs.Count)
        {
            var run = _runs[i];
            if (pos >= start && pos + run.Length <= start + length)
            {
                _runs.RemoveAt(i);
                continue;
            }

            pos += run.Length;
            i++;
        }
    }

    /// <summary>The format of the character immediately before <paramref name="position"/>, or the first character's when at the start.</summary>
    public CharacterFormat FormatBeforeCaret(int position)
    {
        if (position <= 0) return _runs.Count > 0 ? _runs[0].Format : DefaultFormat;

        var pos = 0;
        foreach (var run in _runs)
        {
            if (position <= pos + run.Length) return run.Format;

            pos += run.Length;
        }

        return _runs.Count > 0 ? _runs[^1].Format : DefaultFormat;
    }

    public ParagraphFormat GetParagraphFormatAt(int position)
    {
        var pos = 0;
        foreach (var run in _runs)
        {
            var end = pos + run.Length;
            if (run.IsParagraphBreak && position < end) return run.ParagraphBreak!;

            pos = end;
        }

        return FinalParagraph;
    }

    /// <summary>Every paragraph in document order, each with the runs that make it up (the closing break included).</summary>
    public IReadOnlyList<Paragraph> Paragraphs()
    {
        var result = new List<Paragraph>();
        var runs = new List<RichRun>();
        var start = 0;
        var pos = 0;

        foreach (var run in _runs)
        {
            runs.Add(run);
            pos += run.Length;

            if (!run.IsParagraphBreak) continue;

            result.Add(new Paragraph(start, pos, runs, run.ParagraphBreak!));
            runs = [];
            start = pos;
        }

        result.Add(new Paragraph(start, pos, runs, FinalParagraph));
        return result;
    }

    /// <summary>Applies <paramref name="mutate"/> to the character format of every run fully inside [start, start + length).</summary>
    public void SetCharacterFormat(int start, int length, Func<CharacterFormat, CharacterFormat> mutate)
    {
        if (length <= 0) return;

        SplitAt(start);
        SplitAt(start + length);

        var pos = 0;
        foreach (var run in _runs)
        {
            if (pos >= start && pos + run.Length <= start + length) run.Format = mutate(run.Format);

            pos += run.Length;
        }
    }

    /// <summary>The value of a character format field across [start, start + length): the common value, or null when it varies.</summary>
    public T? CommonCharacterValue<T>(int start, int length, Func<CharacterFormat, T> select) where T : struct
    {
        T? common = null;
        var touched = false;
        var pos = 0;

        foreach (var run in _runs)
        {
            if (pos >= start && pos + run.Length <= start + length && run.Length > 0)
            {
                touched = true;
                var value = select(run.Format);
                if (common is null) common = value;
                else if (!common.Value.Equals(value)) return null;
            }

            pos += run.Length;
        }

        return touched ? common : null;
    }

    /// <summary>Applies <paramref name="mutate"/> to every paragraph the range [start, start + length) touches (the one holding the caret when it is empty).</summary>
    public void SetParagraphFormat(int start, int length, Action<ParagraphFormat> mutate)
    {
        var selectionEnd = start + length;

        foreach (var paragraph in Paragraphs())
        {
            var overlaps = length == 0
                ? start >= paragraph.Start && start <= paragraph.End
                : paragraph.Start < selectionEnd && paragraph.End > start;

            if (overlaps) mutate(paragraph.Format);
        }
    }

    public T? CommonParagraphValue<T>(int start, int length, Func<ParagraphFormat, T> select) where T : struct
    {
        var selectionEnd = start + length;
        T? common = null;
        var touched = false;

        foreach (var paragraph in Paragraphs())
        {
            var overlaps = length == 0
                ? start >= paragraph.Start && start <= paragraph.End
                : paragraph.Start < selectionEnd && paragraph.End > start;

            if (!overlaps) continue;

            touched = true;
            var value = select(paragraph.Format);
            if (common is null) common = value;
            else if (!common.Value.Equals(value)) return null;
        }

        return touched ? common : null;
    }
}
