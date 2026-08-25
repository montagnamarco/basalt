using Basalt.Core.Model;

namespace Basalt.Core.Services;

/// <summary>Result of formatting a document.</summary>
public sealed record FormattingResult(string Text, bool Changed);

/// <summary>
/// Result of formatting while typing: the caret must be repositioned because
/// re-indenting a line moves everything that follows it.
/// </summary>
public sealed record TypingFormattingResult(string Text, int Caret, bool Changed);

/// <summary>
/// Automatic formatting for C# and VB.NET source code.
/// </summary>
public interface IFormattingService
{
    /// <summary>Formats an entire document.</summary>
    Task<FormattingResult> FormatAsync(
        string text, SourceLanguage language, CancellationToken ct = default);

    /// <summary>
    /// Formats only the given span, leaving the rest of the document untouched.
    /// Used while typing, where reformatting the whole file would move the
    /// caret and undo the author's deliberate line breaks elsewhere.
    /// </summary>
    Task<FormattingResult> FormatRangeAsync(
        string text, SourceLanguage language, int start, int length, CancellationToken ct = default);

    /// <summary>
    /// Re-indents the single line holding the caret, used after Enter or after
    /// a block-closing token is typed.
    ///
    /// Returns the new text together with where the caret ends up, since
    /// changing a line's indentation shifts every position after it.
    /// </summary>
    Task<TypingFormattingResult> FormatLineAsync(
        string text, SourceLanguage language, int caret, CancellationToken ct = default);

    /// <summary>
    /// Whether typing this character should trigger re-indentation of its line.
    /// Closing braces in C# and block-closing keywords in VB do; ordinary
    /// characters do not.
    /// </summary>
    bool TriggersFormatting(char character, SourceLanguage language);

    /// <summary>
    /// Applies Visual Basic editing conventions to the line holding the caret:
    /// canonical keyword casing, spacing around operators, and indentation.
    ///
    /// The whole line is corrected rather than just the word last typed. In
    /// "end if" the word "end" was finished earlier, and correcting only the
    /// most recent word would leave "end If".
    /// </summary>
    Task<TypingFormattingResult> ApplyTypingConventionsAsync(
        string text, SourceLanguage language, int caret, CancellationToken ct = default);

    /// <summary>
    /// Indentation, in spaces, that a new line at this position should start
    /// with. Used after Enter, where the line is still empty and so carries no
    /// token to anchor formatting to.
    /// </summary>
    Task<int> GetIndentationAsync(
        string text, SourceLanguage language, int position, CancellationToken ct = default);

    /// <summary>
    /// Whether typing this character completes a word, and should therefore
    /// trigger case correction. Space, tab, newline and punctuation do.
    /// </summary>
    bool CompletesWord(char character, SourceLanguage language);
}
