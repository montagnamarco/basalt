using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Basalt.Tests;

/// <summary>
/// Enter and typing in Visual Basic, checked against what Visual Studio does,
/// with the keys typed into a real window. Each test is one journey; the
/// regression tests that fail without a fix live beside the fix's journey.
/// </summary>
public sealed class VbTypingTests
{
    // ---- journey: Enter after a constructor writes its End Sub ----------

    /// <summary>
    /// Enter after <c>Sub New</c> writes the indented body line and End Sub, as
    /// it does after any other Sub.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicBlockCompleter.ClosingFor</c> had no case
    /// for <c>ConstructorBlock</c>, so a constructor was treated as a block
    /// that needs no closing line.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("Sub New()")]
    [InlineData("Public Sub New(x As Integer)")]
    public async Task EnterAfterAConstructorWritesItsEndSub(string declaration)
    {
        var source = "Class Widget\n    " + declaration + "\n    Public Sub Run()\n    End Sub\nEnd Class\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter(declaration);

        session.Press(Key.Enter, PhysicalKey.Enter);

        var expected = source.Replace(declaration + "\n", declaration + "\n        \n    End Sub\n", StringComparison.Ordinal);
        Assert.True(await session.WaitForTextAsync(expected),
            $"Enter after '{declaration}' produced:\n{session.Editor.Text}");
        var caretLine = session.Editor.Document.GetLineByOffset(session.Editor.CaretOffset);
        Assert.Equal(3, caretLine.LineNumber);
        Assert.Equal(caretLine.EndOffset, session.Editor.CaretOffset);
    }

    // ---- journey: Enter after a multi-line lambda header ----------------

    /// <summary>
    /// Enter at the end of a multi-line lambda header writes the indented body
    /// and its End Sub or End Function, the way a Sub or Function declaration
    /// gets one.
    /// </summary>
    /// <remarks>
    /// The lambda header belongs to an expression inside a declaration, which
    /// <c>VisualBasicBlockCompleter.GetClosingFor</c> finds through the lambda
    /// node rather than the statement that starts the line.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("Dim f = Sub()", "End Sub")]
    [InlineData("Dim f = Function(x As Integer)", "End Function")]
    public async Task EnterAfterAMultiLineLambdaHeaderWritesItsBodyAndEnd(string header, string end)
    {
        var source = "Module Program\n    Sub Main()\n        " + header + "\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter(header);

        session.Press(Key.Enter, PhysicalKey.Enter);

        var expected = source.Replace(header + "\n", header + "\n            \n        " + end + "\n", StringComparison.Ordinal);
        Assert.True(await session.WaitForTextAsync(expected),
            $"Enter after '{header}' produced:\n{session.Editor.Text}");
        var caretLine = session.Editor.Document.GetLineByOffset(session.Editor.CaretOffset);
        Assert.Equal(4, caretLine.LineNumber);
        Assert.Equal(caretLine.EndOffset, session.Editor.CaretOffset);
    }

    /// <summary>
    /// A multi-line lambda passed as an argument is closed inside its call, as
    /// Visual Studio does: Enter writes "End Sub)" with the parenthesis that the
    /// header line left open.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicBlockCompleter</c> wrote "End Sub" alone,
    /// leaving the call unclosed.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("list.ForEach(Sub(x)", "End Sub)")]
    [InlineData("Dim picked = list.Select(Function(x As Integer)", "End Function)")]
    public async Task EnterAfterALambdaArgumentClosesItsCall(string header, string end)
    {
        var source = "Module Program\n    Sub Main()\n        Dim list As New System.Collections.Generic.List(Of Integer)\n        " +
            header + "\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter(header);

        session.Press(Key.Enter, PhysicalKey.Enter);

        var expected = source.Replace(header + "\n", header + "\n            \n        " + end + "\n", StringComparison.Ordinal);
        Assert.True(await session.WaitForTextAsync(expected),
            $"Enter after '{header}' produced:\n{session.Editor.Text}");
    }

    /// <summary>
    /// Lines written inside a multi-line lambda body keep the lambda's
    /// indentation: the next Enter indents one level deeper than the header,
    /// not at the header's column.
    /// </summary>
    [AvaloniaFact]
    public async Task LinesTypedInsideALambdaBodyStayIndentedInsideIt()
    {
        const string source = "Module Program\n    Sub Main()\n        Dim f = Sub()\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter("Dim f = Sub()");
        session.Press(Key.Enter, PhysicalKey.Enter);
        Assert.True(await session.WaitUntilAsync(() => session.Editor.Text.Contains("End Sub\n    End Sub", StringComparison.Ordinal)),
            $"Enter after the lambda header produced:\n{session.Editor.Text}");

        session.Type("x = 1");
        await session.Host.SettleAsync(3);
        session.Press(Key.Enter, PhysicalKey.Enter);

        Assert.True(await session.WaitUntilAsync(() => session.LineText(5).Length == 12 && session.LineText(4).Trim() == "x = 1"),
            $"Enter after a line in the lambda produced:\n{session.Editor.Text}");
        Assert.Equal("            x = 1", session.LineText(4));
        Assert.Equal("            ", session.LineText(5));
    }

    /// <summary>
    /// An Else typed inside an If inside a lambda body moves to the If's column
    /// when the caret leaves the line, as it does outside a lambda.
    /// </summary>
    /// <remarks>
    /// Before the fix, every formatter change inside a lambda body was dropped, so
    /// block continuations (Else, Case, Catch, End If) stayed where they were typed.
    /// </remarks>
    [AvaloniaFact]
    public async Task LeavingAnElseInsideALambdaMovesItToTheIfColumn()
    {
        const string source =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n            If True Then\n" +
            "                Console.WriteLine(\"a\")\n                Else\n            End If\n" +
            "        End Sub\n    End Sub\nEnd Module\n";
        var expected = source.Replace("                Else\n", "            Else\n", StringComparison.Ordinal);
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter("Else");

        session.Press(Key.Down, PhysicalKey.ArrowDown);

        Assert.True(await session.WaitForTextAsync(expected),
            $"leaving the Else line produced:\n{session.Editor.Text}");
    }

    /// <summary>
    /// Leaving a continuation line inside a lambda twice leaves it where it is:
    /// the formatter does not move continuation lines, so the lambda layout must
    /// not move them either.
    /// </summary>
    /// <remarks>
    /// Before the fix, the layout rebased every line it was given, so each time the
    /// caret left the continuation line it moved 8 columns to the left.
    /// </remarks>
    [AvaloniaFact]
    public async Task LeavingAContinuationLineInsideALambdaTwiceKeepsItWhereItIs()
    {
        const string source =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n            Dim total = 1 +\n" +
            "                2\n        End Sub\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter("                2");

        session.Press(Key.Down, PhysicalKey.ArrowDown);
        session.Press(Key.Up, PhysicalKey.ArrowUp);
        session.Press(Key.Down, PhysicalKey.ArrowDown);
        await session.Host.SettleAsync(10);

        Assert.Equal("                2", session.LineText(5));
        Assert.Equal(source, session.Editor.Text);
    }
}
