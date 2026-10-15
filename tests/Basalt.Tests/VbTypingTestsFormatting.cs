using Avalonia.Input;
using Basalt.Workspace;
using Avalonia.Headless.XUnit;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Format Document on Visual Basic text typed in lower case: keywords take
/// their canonical casing, while strings and comments are left as written.
/// </summary>
public sealed class VbTypingTestsFormatting
{
    /// <summary>
    /// Edit, Format Document rewrites <c>dim</c>, <c>as</c>, <c>integer</c>,
    /// <c>if</c>, <c>then</c> and <c>end if</c> in their canonical spelling. A
    /// keyword inside a string literal or a comment is not a token and is kept.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>RoslynFormattingService.FormatAsync</c> ran only the
    /// Roslyn formatter, which fixes spacing and indentation but never
    /// keyword casing, so the lower-case words survived Format Document.
    /// </remarks>
    [AvaloniaFact]
    public async Task FormatDocumentFixesKeywordCasingAndLeavesStringsAndCommentsAlone()
    {
        const string source =
            "Module Program\n    Sub Main()\n        dim x as integer = 1\n        if x = 1 then\n" +
            "            Console.WriteLine(\"if x then\") ' end if\n        end if\n    End Sub\nEnd Module\n";
        const string expected =
            "Module Program\n    Sub Main()\n        Dim x As Integer = 1\n        If x = 1 Then\n" +
            "            Console.WriteLine(\"if x then\") ' end if\n        End If\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        var vm = (MainWindowViewModel)session.Window.DataContext!;

        // Edit, Format Document runs this command; the menu item binds to it.
        await vm.FormatDocumentCommand.ExecuteAsync(null);

        Assert.True(await session.WaitForTextAsync(expected),
            $"Format Document produced:\n{session.Editor.Text}");
    }

    /// <summary>
    /// A key typed while Format Document is still working survives it. The
    /// formatter's answer was computed from the text before the key, so writing
    /// it back would erase the key: the answer is discarded instead.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>MainWindowViewModel.FormatDocumentAsync</c> assigned
    /// <c>document.Text</c> unconditionally, and the editor replaced its whole
    /// text with that answer.
    /// </remarks>
    [AvaloniaFact]
    public async Task ATypedKeyWhileFormatDocumentRunsSurvivesIt()
    {
        const string source = "Module Program\n    Sub Main()\n        dim x as integer = 1\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.FormattingGateForTests = () => release.Task;

        var format = vm.FormatDocumentCommand.ExecuteAsync(null);
        session.CaretAfter("        ");
        session.Type("q");
        release.SetResult();
        await session.WaitUntilAsync(() => format.IsCompleted);

        Assert.True(session.Editor.Text.Contains("qdim x as integer = 1", StringComparison.Ordinal),
            $"the key typed during Format Document was lost:\n{session.Editor.Text}");
    }

    /// <summary>
    /// Format Document leaves a lambda where Visual Studio lays it out: the body
    /// one indent under the line that holds the header, and End Sub or End
    /// Function at that line's indentation. Nested blocks and nested lambdas
    /// keep their columns too, so nothing moves.
    /// </summary>
    /// <remarks>
    /// Before the fix, Roslyn's formatter placed each body under the Sub or
    /// Function keyword (column 20 here, where Visual Studio uses 12), so Format
    /// on Save moved every lambda body that Enter had written.
    /// </remarks>
    [AvaloniaFact]
    public async Task FormatDocumentLeavesNestedLambdasWhereTheyAre()
    {
        const string laid =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n            If True Then\n" +
            "                Console.WriteLine(\"a\")\n            End If\n" +
            "            Dim g = Function(x As Integer)\n                Return x\n" +
            "            End Function\n        End Sub\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(laid);
        var vm = (MainWindowViewModel)session.Window.DataContext!;

        await vm.FormatDocumentCommand.ExecuteAsync(null);
        await session.Host.SettleAsync(3);

        Assert.Equal(laid, session.Editor.Text);
    }

    /// <summary>
    /// The lambda layout moves the body of a lambda, but a line that starts inside
    /// an XML literal is text: its leading spaces are data, and they stay as written.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicLambdaBodyIndentation.Apply</c> shifted every
    /// non-blank body line, including the XML text lines.
    /// </remarks>
    [Fact]
    public void TheLambdaLayoutLeavesXmlLiteralTextAsItIs()
    {
        const string source =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n                    Dim x = <root>\n" +
            "    some text\n</root>\n        End Sub\n    End Sub\nEnd Module\n";
        const string expected =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n            Dim x = <root>\n" +
            "    some text\n</root>\n        End Sub\n    End Sub\nEnd Module\n";

        Assert.Equal(expected, VisualBasicLambdaBodyIndentation.Apply(source, 4));
    }

    /// <summary>
    /// A key typed while the line under the caret is being formatted survives:
    /// the formatter's line was computed from the text before the key, and
    /// writing it back would put the old line into the typed one's place.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>CodeEditor.FormatCurrentLineAsync</c> replaced the
    /// caret's line with the formatter's answer unconditionally.
    /// </remarks>
    [AvaloniaFact]
    public async Task ATypedKeyWhileTheCaretLineIsFormattedSurvivesIt()
    {
        const string source = "Module Program\n    Sub Main()\n        Dim x=1\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.FormattingGateForTests = () => release.Task;
        var code = session.Window.CodeEditorForTests(session.FilePath)!;

        session.CaretAfter("        ");
        var format = code.FormatCurrentLineAsync();
        session.Type("q");
        release.SetResult();
        await session.WaitUntilAsync(() => format.IsCompleted);

        Assert.True(session.Editor.Text.Contains("qDim x=1", StringComparison.Ordinal),
            $"the key typed while the line was formatted was lost:\n{session.Editor.Text}");
    }

    /// <summary>
    /// A key typed while the selection is being formatted survives: the selection's
    /// answer replaces the whole document, so it must not replace a newer one.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>CodeEditor.FormatSelectionAsync</c> replaced the whole
    /// document with the formatter's answer unconditionally.
    /// </remarks>
    [AvaloniaFact]
    public async Task ATypedKeyWhileTheSelectionIsFormattedSurvivesIt()
    {
        const string source = "Module Program\n    Sub Main()\n        Dim x=1\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        var vm = (MainWindowViewModel)session.Window.DataContext!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.FormattingGateForTests = () => release.Task;
        var code = session.Window.CodeEditorForTests(session.FilePath)!;

        // Typing over the selection keeps the caret on its line, so only the
        // selection's own answer can change the text: it must not arrive.
        session.Editor.Select(source.IndexOf("Dim x=1", StringComparison.Ordinal), "Dim x=1".Length);
        var format = code.FormatSelectionAsync();
        session.Type("q");
        release.SetResult();
        await session.WaitUntilAsync(() => format.IsCompleted);
        await session.Host.SettleAsync(5);

        Assert.True(session.Editor.Text.Contains("        q\n", StringComparison.Ordinal),
            $"the key typed while the selection was formatted was lost:\n{session.Editor.Text}");
        Assert.DoesNotContain("Dim x = 1", session.Editor.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Roslyn's VB 14 parser reads a string whose text goes on to the next line as
    /// one string literal token, so its second line is data inside a token.
    /// </summary>
    [Fact]
    public void AStringLiteralCanContinueOntoTheNextLineInVisualBasic14()
    {
        const string source = "Dim sql = \"SELECT *\n    FROM t\"\n";
        var root = Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(source,
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicParseOptions(
                Microsoft.CodeAnalysis.VisualBasic.LanguageVersion.VisualBasic14)).GetRoot();

        var literal = root.DescendantTokens().Single(token =>
            token.RawKind == (int)Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.StringLiteralToken);
        Assert.Contains("\n    FROM", literal.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Format Document keeps the second line of a multi-line string inside a lambda
    /// exactly as written, although the statement before it is re-indented.
    /// </summary>
    /// <remarks>
    /// Before the fix, <c>VisualBasicLambdaBodyIndentation.Apply</c> shifted every
    /// body line, the string's second line included.
    /// </remarks>
    [AvaloniaFact]
    public async Task FormatDocumentKeepsTheSecondLineOfAMultiLineStringInALambda()
    {
        const string source =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n                    Dim sql = \"SELECT *\n" +
            "    FROM t\"\n        End Sub\n    End Sub\nEnd Module\n";
        const string expected =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n            Dim sql = \"SELECT *\n" +
            "    FROM t\"\n        End Sub\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        var vm = (MainWindowViewModel)session.Window.DataContext!;

        await vm.FormatDocumentCommand.ExecuteAsync(null);

        Assert.True(await session.WaitForTextAsync(expected),
            $"Format Document produced:\n{session.Editor.Text}");
    }

    /// <summary>
    /// Leaving the second line of a multi-line string inside a lambda keeps it as
    /// written: the caret's line is laid out, but never the text of the string.
    /// </summary>
    [AvaloniaFact]
    public async Task LeavingTheSecondLineOfAMultiLineStringInALambdaKeepsIt()
    {
        const string source =
            "Module Program\n    Sub Main()\n        Dim f = Sub()\n            Dim sql = \"SELECT *\n" +
            "    FROM t\"\n        End Sub\n    End Sub\nEnd Module\n";
        using var session = await VbTypingSession.OpenAsync(source);
        session.CaretAfter("    FROM t\"");

        session.Press(Key.Down, PhysicalKey.ArrowDown);
        await session.Host.SettleAsync(10);

        Assert.Equal(source, session.Editor.Text);
    }
}
