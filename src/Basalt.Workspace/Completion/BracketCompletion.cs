namespace Basalt.Workspace.Completion;

/// <summary>What typing a character should do to the text.</summary>
public readonly record struct BracketAction(string Insert, int CaretOffset, int SkipLength)
{
    /// <summary>Nothing to do beyond inserting the character itself.</summary>
    public static readonly BracketAction None = new("", 0, 0);

    public bool IsNone => Insert.Length == 0 && SkipLength == 0;
}

/// <summary>
/// Closing brackets and quotes as they are opened.
///
/// The rules are the ones that stay out of the way: a bracket closes only
/// where a closing one would be expected anyway, and typing the closing
/// character over one just inserted steps past it rather than doubling it.
/// </summary>
public static class BracketCompletion
{
    private static readonly Dictionary<char, char> Pairs = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
        ['"'] = '"'
    };

    /// <summary>
    /// Decides what typing a character should do.
    ///
    /// <paramref name="caret"/> is where the character is about to go.
    /// </summary>
    public static BracketAction ForTypedCharacter(string text, int caret, char typed)
    {
        if (caret < 0 || caret > text.Length) return BracketAction.None;

        // Typing the closing character where one already sits steps over it,
        // which is what the user means having typed the pair a moment ago.
        if (caret < text.Length && text[caret] == typed && IsClosing(typed))
            return new BracketAction("", 0, 1);

        if (!Pairs.TryGetValue(typed, out var closing)) return BracketAction.None;

        if (typed == '"')
        {
            // A quote in the middle of a word is an apostrophe or a closing
            // quote, not the start of a string.
            if (caret > 0 && IsWordCharacter(text[caret - 1])) return BracketAction.None;
            if (IsInsideString(text, caret)) return BracketAction.None;
        }

        // Closing before ordinary text would put the bracket in the wrong
        // place: "(" before "abc" is usually wrapping what follows.
        if (caret < text.Length && IsWordCharacter(text[caret])) return BracketAction.None;

        return new BracketAction(closing.ToString(), -1, 0);
    }

    /// <summary>
    /// Whether deleting the character before the caret should take its pair
    /// with it.
    ///
    /// Only when the two are adjacent, which is the case the completion itself
    /// created and the one backspace is expected to undo.
    /// </summary>
    public static bool ShouldDeletePair(string text, int caret)
    {
        if (caret <= 0 || caret >= text.Length) return false;

        return Pairs.TryGetValue(text[caret - 1], out var closing) && text[caret] == closing;
    }

    private static bool IsClosing(char c) => c is ')' or ']' or '}' or '"';

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Whether a position is inside a string literal.
    ///
    /// Counted from the start of the line, since a Visual Basic string cannot
    /// span one. Two quotes together are an escaped quote, not a closed and
    /// reopened string.
    /// </summary>
    private static bool IsInsideString(string text, int caret)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        var inside = false;

        for (var i = lineStart; i < caret; i++)
        {
            if (text[i] != '"') continue;

            if (i + 1 < caret && text[i + 1] == '"')
            {
                i++;
                continue;
            }

            inside = !inside;
        }

        return inside;
    }
}
