using Microsoft.CodeAnalysis.Text;

namespace Basalt.Razor.Vb.Generator;

/// <summary>
/// The SHA-256 of a template as the compiler read it from disk.
/// </summary>
/// <remarks>
/// Taken from the SourceText rather than computed here: the compiler hashes
/// the file's bytes when it loads them, BOM and line endings included, which
/// is what a debugger compares against. Hashing the decoded string instead
/// would disagree with the file whenever it has a BOM or CRLF endings, and a
/// wrong checksum is worse than none — the debugger refuses the file.
/// </remarks>
internal static class TemplateChecksum
{
    public static string? Of(SourceText? text)
    {
        if (text is null || text.ChecksumAlgorithm != SourceHashAlgorithm.Sha256) return null;

        var checksum = text.GetChecksum();

        return checksum.IsDefaultOrEmpty ? null : ExternalSourceWriter.Hex(checksum);
    }
}
