namespace Basalt.Extensibility;

/// <summary>
/// What a suggestion offers to write for you.
/// </summary>
/// <param name="Text">
/// The text as it would be inserted, which may run over several lines.
/// </param>
/// <param name="Position">Where in the document it belongs.</param>
public sealed record InlineSuggestion(string Text, int Position)
{
    /// <summary>The first line, which is what the editor draws.</summary>
    /// <remarks>
    /// A suggestion of several lines is shown one line at a time: drawing all
    /// of it would push the code below it down the screen and back up again on
    /// every keystroke, which is unreadable however good the suggestion is.
    /// </remarks>
    public string FirstLine =>
        Text.Replace("\r\n", "\n").Split('\n')[0];

    /// <summary>
    /// The first word, for accepting a suggestion a piece at a time.
    /// </summary>
    /// <remarks>
    /// A suggestion is often right at the start and wrong further along, and
    /// taking the first word is how someone keeps the useful part without
    /// deleting the rest afterwards.
    /// </remarks>
    public string FirstWord
    {
        get
        {
            var line = FirstLine;
            var at = 0;

            // Past any leading space, then to the end of the word: a
            // suggestion beginning with a space would otherwise accept as
            // nothing at all and look broken.
            while (at < line.Length && char.IsWhiteSpace(line[at])) at++;
            while (at < line.Length && !char.IsWhiteSpace(line[at])) at++;

            return line.Substring(0, at);
        }
    }
}

/// <summary>
/// Something that suggests what to write next.
/// </summary>
/// <remarks>
/// An interface rather than a Copilot client directly, because the editor
/// should not know which service answered — and because a provider that
/// returns a fixed answer is the only way to test the editor's half without a
/// network and an account.
/// </remarks>
public interface IInlineSuggestionProvider
{
    /// <summary>Whether this provider is able to answer at all.</summary>
    /// <remarks>
    /// Asked before the request, so an editor with no provider signed in does
    /// not pay for a round trip that can only fail.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>
    /// What might come next at this position, or null.
    /// </summary>
    /// <param name="filePath">Which file, so the provider can see its kind.</param>
    /// <param name="text">The document as it stands.</param>
    /// <param name="position">Where the caret is.</param>
    Task<InlineSuggestion?> SuggestAsync(
        string filePath, string text, int position, CancellationToken ct = default);
}
