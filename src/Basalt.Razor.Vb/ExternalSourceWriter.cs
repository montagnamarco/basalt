using System;
using System.Collections.Generic;
using System.Text;

namespace Basalt.Razor.Vb;

/// <summary>
/// The one place generated Visual Basic is tied back to its template.
/// </summary>
/// <remarks>
/// Views and components used to carry a copy each, and the copies drifted: the
/// component one added one to a line number that was already counted from
/// one, so every breakpoint in a .vbrazor bound a line low, and it doubled the
/// backslashes of a Windows path, which Visual Basic string literals do not
/// escape — the debugger was sent looking for a file that does not exist.
/// Neither fault crashes anything; both make the debugger quietly wrong.
///
/// #ExternalSource maps by line, not by column: Visual Basic has no
/// column-accurate form of it, unlike the C# #line the Razor compiler uses.
/// </remarks>
internal static class ExternalSourceWriter
{
    /// <summary>
    /// The GUID Visual Basic and the portable PDB use to name SHA-256.
    /// </summary>
    public const string Sha256Algorithm = "{8829d00f-11b8-4213-878b-770e8597ac16}";

    /// <summary>
    /// Writes something that came from the template, wrapped in a pragma and
    /// recorded in the mapping table.
    /// </summary>
    /// <remarks>
    /// The two are produced here together and nowhere else, so a mapping
    /// without its pragma — or the other way round — cannot happen.
    ///
    /// <paramref name="originalLine"/> is one-based, as the parser counts and
    /// as #ExternalSource expects. <paramref name="offset"/> skips whatever
    /// the first generated line puts in front of the template's text, so a
    /// caret lands on the matching character rather than at the start of the
    /// statement carrying it.
    ///
    /// A multi-line fragment must be written line for line, blank lines
    /// included: the n-th generated line is recorded as the n-th template
    /// line, and a line dropped in between moves everything after it.
    /// </remarks>
    public static void WriteMapped(
        StringBuilder builder,
        List<SourceMapping> mappings,
        string? filePath,
        int originalPosition,
        int originalLength,
        int originalLine,
        Action write,
        int offset = 0,
        (int OriginalStart, int GeneratedOffset, int Length)? keyword = null)
    {
        if (filePath is null)
        {
            write();
            return;
        }

        builder.AppendLine($"#ExternalSource({PragmaPath(filePath)}, {originalLine})");

        var generatedStart = builder.Length;
        var generatedLine = CountLines(builder, generatedStart);

        write();

        var generatedLength = builder.Length - generatedStart;

        // A directive has to start its own line, and a fragment written with
        // Append rather than AppendLine leaves the builder mid-line: the
        // directive was glued to the end of the expression and the generated
        // file did not compile.
        if (builder.Length > 0 && builder[builder.Length - 1] != '\n')
            builder.AppendLine();

        builder.AppendLine("#End ExternalSource");

        mappings.Add(new SourceMapping(
            new SourceSpan(originalPosition, originalLength),
            new SourceSpan(generatedStart + offset, Math.Max(0, generatedLength - offset)),
            originalLine,
            generatedLine));

        // A keyword written in front of the mapped text — "Await" — mapped
        // onto its own place in the template, so an error about it lands
        // there and not nowhere.
        if (keyword is { } extra)
        {
            mappings.Add(new SourceMapping(
                new SourceSpan(extra.OriginalStart, extra.Length),
                new SourceSpan(generatedStart + extra.GeneratedOffset, extra.Length),
                originalLine,
                generatedLine));
        }

        // A multi-line region needs one entry per line. Without them a caret
        // anywhere below the first line of a Code block mapped to nothing,
        // and every delegated feature went quiet there.
        var writtenLines = LinesIn(builder, generatedStart, generatedLength);

        for (var line = 1; line < writtenLines; line++)
        {
            mappings.Add(new SourceMapping(
                new SourceSpan(originalPosition, originalLength),
                new SourceSpan(generatedStart, generatedLength),
                originalLine + line,
                generatedLine + line));
        }
    }

    /// <summary>
    /// The #ExternalChecksum line for a template, or nothing without a checksum.
    /// </summary>
    /// <remarks>
    /// It lets a debugger check that the file it opens is the one the code
    /// was built from, instead of stopping on a line of a template that has
    /// changed since. The checksum has to be of the file's bytes as they are
    /// on disk, which only the caller that read the file can supply.
    /// </remarks>
    /// <param name="sha256">The SHA-256 of the file as hexadecimal digits.</param>
    public static void WriteChecksum(StringBuilder builder, string? filePath, string? sha256)
    {
        if (filePath is null || string.IsNullOrEmpty(sha256)) return;

        builder.AppendLine(
            $"#ExternalChecksum({PragmaPath(filePath)}, \"{Sha256Algorithm}\", \"{sha256}\")");
    }

    /// <summary>Bytes as the lowercase hexadecimal digits a checksum is written in.</summary>
    public static string Hex(IEnumerable<byte> bytes)
    {
        var hex = new StringBuilder();

        foreach (var value in bytes) hex.Append(value.ToString("x2"));

        return hex.ToString();
    }

    /// <summary>
    /// A file path as a pragma can carry it.
    /// </summary>
    /// <remarks>
    /// Visual Basic strings have no escapes, so a backslash is written as it
    /// is; only a quote needs doubling, or it would end the string early and
    /// turn the rest of the generated file into nonsense.
    /// </remarks>
    public static string PragmaPath(string path) =>
        "\"" + path.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// The lines of a block of code, blank ones kept.
    /// </summary>
    /// <remarks>
    /// Blank lines used to be dropped, which is harmless to the compiler and
    /// fatal to the mapping: every line after the first blank one was
    /// attributed to the template line above it, and a breakpoint there bound
    /// one line early.
    /// </remarks>
    public static IEnumerable<string> LinesOf(string code)
    {
        foreach (var line in code.Split('\n'))
            yield return line.TrimEnd('\r');
    }

    /// <summary>How many lines have been written so far, counted from one.</summary>
    public static int CountLines(StringBuilder builder, int upTo)
    {
        var lines = 1;

        for (var i = 0; i < upTo; i++)
            if (builder[i] == '\n') lines++;

        return lines;
    }

    /// <summary>How many lines a run of the builder holds.</summary>
    private static int LinesIn(StringBuilder builder, int start, int length)
    {
        var lines = 1;
        var end = start + length;

        for (var at = start; at < end && at < builder.Length; at++)
            if (builder[at] == '\n' && at + 1 < end) lines++;

        return lines;
    }
}
