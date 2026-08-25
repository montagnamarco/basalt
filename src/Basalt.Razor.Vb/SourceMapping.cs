namespace Basalt.Razor.Vb;

/// <summary>A stretch of text, as an offset and a length.</summary>
public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool Contains(int position) => position >= Start && position < End;
}

/// <summary>
/// One place where the generated Visual Basic came from the template.
///
/// The pair is what lets a position travel in either direction: an error in
/// the generated code becomes a position in the template, and a caret in the
/// template becomes a position the language service can be asked about.
/// </summary>
public sealed record SourceMapping(
    SourceSpan Original,
    SourceSpan Generated,
    int OriginalLine,
    int GeneratedLine);

/// <summary>
/// How hard to try when a position does not fall exactly inside a mapping.
/// </summary>
public enum MappingBehavior
{
    /// <summary>Only a position inside a mapping maps at all.</summary>
    Strict,

    /// <summary>A position touching a mapping is pulled into it.</summary>
    Inclusive,

    /// <summary>
    /// A position between two mappings maps to the next one along.
    ///
    /// Deliberately forwards and never backwards: an error in generated code
    /// belongs to what comes after it in the template, not before. Razor made
    /// the same choice for the same reason.
    /// </summary>
    Inferred
}

/// <summary>
/// The table that ties a generated document back to its template.
///
/// Kept sorted twice, by original and by generated position, so a lookup in
/// either direction is a binary search rather than a scan. That matters: the
/// editor asks on every keystroke.
/// </summary>
public sealed class SourceMap
{
    private readonly SourceMapping[] _byOriginal;
    private readonly SourceMapping[] _byGenerated;

    public SourceMap(IEnumerable<SourceMapping> mappings)
    {
        var all = mappings.ToArray();

        _byOriginal = [.. all.OrderBy(m => m.Original.Start)];
        _byGenerated = [.. all.OrderBy(m => m.Generated.Start)];
    }

    /// <summary>Every mapping, in template order.</summary>
    public IReadOnlyList<SourceMapping> Mappings => _byOriginal;

    public static SourceMap Empty { get; } = new([]);

    /// <summary>
    /// Where a position in the generated code sits in the template.
    ///
    /// Null when it sits in code the template did not write — the class
    /// header, the closing statements — which is the honest answer: that code
    /// belongs to nobody.
    /// </summary>
    public int? ToOriginal(int generatedPosition, MappingBehavior behavior = MappingBehavior.Strict)
    {
        var found = Find(_byGenerated, generatedPosition, m => m.Generated, behavior);

        if (found is not { } mapping) return null;

        // The same offset within the mapping, so a position halfway through
        // an expression lands halfway through it in the template.
        var offset = Portable.Clamp(
            generatedPosition - mapping.Generated.Start, 0, mapping.Original.Length);

        return mapping.Original.Start + offset;
    }

    /// <summary>Where a position in the template sits in the generated code.</summary>
    public int? ToGenerated(int originalPosition, MappingBehavior behavior = MappingBehavior.Strict)
    {
        var found = Find(_byOriginal, originalPosition, m => m.Original, behavior);

        if (found is not { } mapping) return null;

        var offset = Portable.Clamp(
            originalPosition - mapping.Original.Start, 0, mapping.Generated.Length);

        return mapping.Generated.Start + offset;
    }


    /// <summary>
    /// Where a template position sits in the generated code, counting lines
    /// rather than characters.
    /// </summary>
    /// <remarks>
    /// The plain offset arithmetic assumes the two texts run in step, and
    /// they do not: the writer indents every line it emits, so by the second
    /// line of a Code block the generated text is a dozen characters ahead
    /// and a caret lands on the wrong token. Forty-two characters of template
    /// came out as sixty-seven of Visual Basic in the case that found this.
    ///
    /// Lines survive indentation, so the position is decomposed into a line
    /// and a column within the mapping, and recomposed on the other side.
    /// </remarks>
    public int? ToGenerated(
        int originalPosition, string originalText, string generatedText,
        MappingBehavior behavior = MappingBehavior.Strict)
    {
        var found = Find(_byOriginal, originalPosition, m => m.Original, behavior);

        if (found is not { } mapping) return null;

        var start = Portable.Clamp(mapping.Original.Start, 0, originalText.Length);
        var caret = Portable.Clamp(originalPosition, start, originalText.Length);

        // How far into the mapping the caret is, in lines and in characters
        // along its own line.
        var lines = 0;
        var lastBreak = start;

        for (var i = start; i < caret; i++)
        {
            if (originalText[i] != '\n') continue;

            lines++;
            lastBreak = i + 1;
        }

        var column = caret - lastBreak;

        // The same place on the other side: forward that many line breaks,
        // then that many characters along — past whatever indent the writer
        // put there.
        var at = Portable.Clamp(mapping.Generated.Start, 0, generatedText.Length);

        for (var remaining = lines; remaining > 0 && at < generatedText.Length; at++)
            if (generatedText[at] == '\n') remaining--;

        var indent = at;

        while (indent < generatedText.Length &&
               (generatedText[indent] == ' ' || generatedText[indent] == '\t'))
            indent++;

        // The indent is skipped on every line, the first included: a mapping
        // that begins at the start of a written line has the writer's indent
        // in front of it, and assuming otherwise put the caret a dozen
        // characters early — on "ToString" when it was resting on "Length".
        //
        // Except where the entry begins mid-line, which is where the writer
        // had already placed something: then there is no indent of its own to
        // step over, and skipping the following spaces would walk past the
        // very text the entry covers.
        var beginsLine = mapping.Generated.Start == 0
                      || generatedText[mapping.Generated.Start - 1] == '\n';

        var target = (lines == 0 && !beginsLine ? at : indent) + column;

        return Portable.Clamp(target, 0, generatedText.Length);
    }

    /// <summary>The generated line a template line produced, if any.</summary>
    public int? ToGeneratedLine(int originalLine)
    {
        foreach (var mapping in _byOriginal)
            if (mapping.OriginalLine == originalLine) return mapping.GeneratedLine;

        return null;
    }

    /// <summary>The template line a generated line came from, if any.</summary>
    public int? OriginalLineOf(int generatedLine)
    {
        foreach (var mapping in _byGenerated)
            if (mapping.GeneratedLine == generatedLine) return mapping.OriginalLine;

        return null;
    }

    /// <summary>
    /// The mapping a position falls in, following the behaviour asked for.
    /// </summary>
    private static SourceMapping? Find(
        SourceMapping[] sorted,
        int position,
        Func<SourceMapping, SourceSpan> spanOf,
        MappingBehavior behavior)
    {
        if (sorted.Length == 0) return null;

        // Binary search for the last mapping that starts at or before the
        // position: that is the only one that can contain it.
        var low = 0;
        var high = sorted.Length - 1;
        var candidate = -1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (spanOf(sorted[middle]).Start <= position)
            {
                candidate = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (candidate >= 0)
        {
            var span = spanOf(sorted[candidate]);

            if (span.Contains(position)) return sorted[candidate];

            // Inclusive also takes a position sitting just past the end,
            // which is where a caret is when something has just been typed.
            if (behavior != MappingBehavior.Strict && position == span.End)
                return sorted[candidate];
        }

        // Inferred looks forward to the next mapping, and never back.
        if (behavior == MappingBehavior.Inferred)
        {
            var next = candidate + 1;

            if (next < sorted.Length) return sorted[next];
        }

        return null;
    }
}
