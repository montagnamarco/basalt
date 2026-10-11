using Basalt.Extensibility;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// The markup check reads the markup of a template, not its Visual Basic.
/// </summary>
/// <remarks>
/// The HTML tag scan ran over the whole template, so in a component's @Code block the
/// attribute &lt;Parameter&gt; was an element never closed and "a &lt; b" the start of a
/// tag (HTML001). The provider now blanks the code spans (VbHtmlCodeRegions.CodeSpans)
/// before the scan; without that the first test fails.
/// </remarks>
public sealed class TemplateMarkupInCodeTests
{
    [Fact]
    public async Task AttributesAndComparisonsInACodeBlockAreNotReadAsTags()
    {
        const string component =
            "<h1>@Title</h1>\n\n@Code\n    <Global.Microsoft.AspNetCore.Components.Parameter>\n" +
            "    Public Property Title As String\n\n    Private Function Small(a As Integer, b As Integer) As Boolean\n" +
            "        Return a < b\n    End Function\nEnd Code\n";

        var diagnostics = await new VbHtmlDiagnosticProvider()
            .GetDiagnosticsAsync(new LanguageDocument("/x/C.vbrazor", component));

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("HTML", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnclosedElementInTheMarkupIsStillReportedOnItsLine()
    {
        const string component =
            "<div>\n<h1>@Title</h1>\n\n@Code\n    <Global.Microsoft.AspNetCore.Components.Parameter>\n" +
            "    Public Property Title As String\nEnd Code\n";

        var diagnostics = await new VbHtmlDiagnosticProvider()
            .GetDiagnosticsAsync(new LanguageDocument("/x/C.vbrazor", component));

        var unclosed = Assert.Single(diagnostics, d => d.Id.StartsWith("HTML", StringComparison.Ordinal));
        Assert.Equal(1, unclosed.Range.Start.Line);
    }

    /// <summary>
    /// Every kind of code span is blanked exactly: a raw expression whose own markup
    /// balances, an ElseIf and a Case with a comparison, an attribute directive; and a
    /// real error after them still lands on its line and column (CRLF included).
    /// </summary>
    /// <remarks>
    /// The first version ended an expression's span at Position + text length, so in
    /// @Html.Raw("&lt;b&gt;…&lt;/b&gt;") it hid &lt;b&gt; but not &lt;/b&gt; (a new HTML001), and it left
    /// ElseIf / Case clauses and @Attribute &lt;Authorize&gt; visible. Each case fails without
    /// its span.
    /// </remarks>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task EveryCodeSpanIsHiddenAndARealErrorKeepsItsPosition(string newline)
    {
        var view = string.Join(newline,
            "@Attribute <System.Obsolete>",
            "<p>@(Model.Length < 4)</p>",
            "@If Model.Length < 10 Then",
            "    <p>tiny</p>",
            "@End If",
            "<p>@Html.Raw(\"<b>\" & Model & \"</b>\")</p>",
            "@If Model.Length > 3 Then",
            "    <p>long</p>",
            "@ElseIf Model.Length < 2 Then",
            "    <p>short</p>",
            "@End If",
            "@Select Case Model.Length",
            "    @Case Is < 5",
            "        <p>small</p>",
            "@End Select",
            "  <span>",
            "");

        var diagnostics = await new VbHtmlDiagnosticProvider()
            .GetDiagnosticsAsync(new LanguageDocument("/x/V.vbhtml", view));

        var html = diagnostics.Where(d => d.Id.StartsWith("HTML", StringComparison.Ordinal)).ToList();
        var unclosed = Assert.Single(html);
        Assert.Equal(16, unclosed.Range.Start.Line);
        Assert.Equal(3, unclosed.Range.Start.Column);
    }
}
