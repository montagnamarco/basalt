using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Basalt.Tests;

/// <summary>
/// Documentation comments typed and continued in a real window: Enter inside
/// a <c>'''</c> comment carries it on, and <c>'''</c> above a type or member
/// writes its skeleton.
/// </summary>
public sealed class VbTypingTestsDocumentation
{
    // ---- journey: Enter inside a ''' documentation comment --------------

    /// <summary>
    /// Enter at the end of a <c>'''</c> line starts the next line with
    /// <c>''' </c> at the same indentation, so the comment carries on.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicEnterInput</c> inserted only the
    /// indentation of the line the caret was on, so the next line was a bare
    /// indent and the documentation comment ended.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("''' <summary>Gets the name.")]
    [InlineData("''' Gets the name.")]
    [InlineData("'''")]
    public async Task EnterInsideADocumentationCommentContinuesItWithTheSameIndentation(string comment)
    {
        var source = "Class Widget\n    " + comment + "\n    Public Property Name As String\nEnd Class\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter(comment);

        session.Press(Key.Enter, PhysicalKey.Enter);

        var expected = source.Replace(comment + "\n", comment + "\n    ''' \n", StringComparison.Ordinal);
        Assert.True(await session.WaitForTextAsync(expected),
            $"Enter inside '{comment}' produced:\n{session.Editor.Text}");
        var caretLine = session.Editor.Document.GetLineByOffset(session.Editor.CaretOffset);
        Assert.Equal(3, caretLine.LineNumber);
        Assert.Equal(caretLine.EndOffset, session.Editor.CaretOffset);
    }

    /// <summary>
    /// Enter before the marker (Home, then Enter) splits the line as Enter
    /// always does. It does not start a second marker in front of the
    /// comment's own marker.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicDocumentationContinuation.PrefixFor</c>
    /// checked only that the line began with a marker, so the caret position
    /// was ignored and the result read <c>''' ''' summary</c>.
    /// </remarks>
    [AvaloniaFact]
    public async Task EnterBeforeTheMarkerSplitsTheLineInsteadOfContinuingTheComment()
    {
        const string comment = "''' <summary>Gets the name.";
        var source = "Class Widget\n    " + comment + "\n    Public Property Name As String\nEnd Class\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAt(source.IndexOf(comment, StringComparison.Ordinal));

        session.Press(Key.Enter, PhysicalKey.Enter);

        // The newline Enter types is already the expected text, so waiting for
        // the text would return before the line's own work is applied. Let the
        // whole Enter finish first.
        await session.Host.SettleAsync(60);

        var expected = source.Replace("    " + comment, "    \n    " + comment, StringComparison.Ordinal);
        Assert.Equal(expected, session.Editor.Text);
        Assert.DoesNotContain("''' '''", session.Editor.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Enter after an ordinary comment does not turn it into documentation: the
    /// continuation is for <c>'''</c> only.
    /// </summary>
    [AvaloniaFact]
    public async Task EnterAfterAPlainCommentDoesNotStartADocumentationComment()
    {
        const string source = "Class Widget\n    ' a plain note\n    Public Property Name As String\nEnd Class\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter("' a plain note");

        session.Press(Key.Enter, PhysicalKey.Enter);

        Assert.True(await session.WaitUntilAsync(() => session.Editor.Document.LineCount == 6),
            $"Enter after a plain comment produced:\n{session.Editor.Text}");
        Assert.DoesNotContain("'''", session.Editor.Text, StringComparison.Ordinal);
    }

    // ---- journey: ''' above a type writes its documentation -------------

    /// <summary>
    /// Typing <c>'''</c> on the line above a Class, Module, Structure or
    /// Interface writes the summary skeleton, as it does above a method. A
    /// generic type gets a typeparam tag; a type takes no param or returns.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicDocumentationCommentService.Generate</c>
    /// only accepted methods, constructors, properties and events, so the
    /// trigger line was left as a bare <c>'''</c>.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("Public Class Widget", "End Class", "<summary>")]
    [InlineData("Class Box(Of T)", "End Class", "<typeparam name=\"T\"></typeparam>")]
    [InlineData("Module Helpers", "End Module", "<summary>")]
    [InlineData("Public Structure Point", "End Structure", "<summary>")]
    [InlineData("Public Interface IShape", "End Interface", "<summary>")]
    public async Task TypingThreeApostrophesAboveATypeWritesItsDocumentationSkeleton(
        string declaration, string end, string expectedTag)
    {
        const string before = "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n";
        var source = before + "\n" + declaration + "\n    \n" + end + "\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAt(before.Length);

        session.Type("'");
        session.Type("'");
        session.Type("'");

        Assert.True(await session.WaitUntilAsync(() => session.Editor.Text.Contains("''' <summary>", StringComparison.Ordinal)
                && session.Editor.Text.Contains(expectedTag, StringComparison.Ordinal)),
            $"typing ''' above '{declaration}' produced:\n{session.Editor.Text}");
        Assert.DoesNotContain("<param name", session.Editor.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<returns>", session.Editor.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Typing <c>'''</c> above a Property writes the summary and a value tag,
    /// as it always has.
    /// </summary>
    [AvaloniaFact]
    public async Task TypingThreeApostrophesAboveAPropertyWritesItsSummaryAndValue()
    {
        const string source = "Class Widget\n    \n    Public Property Name As String\nEnd Class\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAt(source.IndexOf("    \n", StringComparison.Ordinal) + 4);

        session.Type("'");
        session.Type("'");
        session.Type("'");

        Assert.True(await session.WaitUntilAsync(() => session.Editor.Text.Contains("''' <summary>", StringComparison.Ordinal)
                && session.Editor.Text.Contains("''' <value></value>", StringComparison.Ordinal)),
            $"typing ''' above a property produced:\n{session.Editor.Text}");
    }
}
