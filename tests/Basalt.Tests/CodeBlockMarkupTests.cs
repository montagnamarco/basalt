using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Tests;

/// <summary>
/// Markup written inside a <c>@Code</c> block, as Visual Basic views for
/// MVC 5 write it.
/// </summary>
/// <remarks>
/// <c>@&lt;li&gt;@item&lt;/li&gt;</c> and <c>@:text</c> inside a loop in a code
/// block. The block used to go to the compiler whole, "@" included, and the
/// first such line failed to compile — the most common thing a view ported
/// from .NET Framework contains.
/// </remarks>
public class CodeBlockMarkupTests
{
    private static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(VbHtmlView).Assembly.Location),
    ];

    /// <summary>Generates, compiles and runs a standalone view, returning its output.</summary>
    private static string Render(string template)
    {
        var document = VbHtmlParser.Parse(template);

        Assert.Empty(document.Diagnostics);

        var code = VbHtmlCodeWriter.WriteWithMap(document, "View", "Generated", "/Views/View.vbhtml").Code;

        var compilation = VisualBasicCompilation.Create(
            "View" + Guid.NewGuid().ToString("N"),
            [VisualBasicSyntaxTree.ParseText(code)],
            References,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithGlobalImports(GlobalImport.Parse("Microsoft.VisualBasic", "System")));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);

        Assert.True(emitted.Success, string.Join("\n",
            emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n\n" + code);

        var type = System.Reflection.Assembly.Load(stream.ToArray()).GetType("Generated.View")!;
        var view = (VbHtmlView)Activator.CreateInstance(type)!;

        return view.Render();
    }

    [Fact]
    public void AnElementInALoopIsWrittenForEachItem()
    {
        var html = Render("""
            <ul>
            @Code
                For Each item In New String() {"a", "b"}
                    @<li>@item</li>
                Next
            End Code
            </ul>
            """);

        Assert.Contains("<li>a</li>", html);
        Assert.Contains("<li>b</li>", html);
    }

    [Fact]
    public void ALineOfTextIsWrittenWithItsValues()
    {
        var html = Render("""
            @Code
                Dim total = 2 + 3
                @:Total: @total
            End Code
            """);

        Assert.Contains("Total: 5", html);
    }

    [Fact]
    public void AnElementInABlockWritesItsValues()
    {
        // @<p> in an @If used to write "@name" as the two characters.
        var html = Render("""
            @Code
                Dim name = "Ada"
            End Code
            @If name.Length > 0 Then
                @<p>Hello @name</p>
            End If
            """);

        Assert.Contains("<p>Hello Ada</p>", html);
    }

    [Fact]
    public void TheCodeAroundTheMarkupStillMapsToItsLines()
    {
        // Each run of statements between the markup is its own mapped
        // region, starting on the line its first statement is on.
        const string template = "@Code\n    Dim a = 1\n    @<p>@a</p>\n    Dim b = 2\nEnd Code\n";

        var document = VbHtmlParser.Parse(template);
        var statements = document.Nodes.OfType<StatementNode>().ToList();

        Assert.Equal(2, statements.Count);
        Assert.Equal(2, statements[0].BodyLine);
        Assert.Equal("Dim a = 1", statements[0].Code);
        Assert.Equal(4, statements[1].BodyLine);
        Assert.Equal("Dim b = 2", statements[1].Code);
        Assert.Equal(template.IndexOf("Dim b", StringComparison.Ordinal), statements[1].BodyPosition);
    }

    [Fact]
    public void EachRunOfCodeIsPlacedWhereItStarts()
    {
        const string template = "@Code\n    Dim a = 1\n    @<p>@a</p>\n    Dim b = 2\nEnd Code\n";

        var statements = VbHtmlParser.Parse(template).Nodes.OfType<StatementNode>().ToList();

        // The first carries the keyword; the second is placed at its own code,
        // so whatever looks for it from its position finds it and not the first.
        Assert.False(statements[0].IsContinuation);
        Assert.Equal(0, statements[0].Position);
        Assert.Equal(template.IndexOf("Dim a", StringComparison.Ordinal), statements[0].BodyPosition);

        Assert.True(statements[1].IsContinuation);
        Assert.Equal(template.IndexOf("Dim b", StringComparison.Ordinal), statements[1].Position);
    }

    [Fact]
    public void FormattingABlockWithMarkupInItKeepsEveryPiece()
    {
        // The pieces shared one position, and the formatter applied its edits
        // with offsets that no longer held, corrupting the file.
        var result = Basalt.Workspace.Web.VbHtmlFormattingProvider.Format(
            "@Code\nDim a=1\n@<p>@a</p>\nDim b=2\nEnd Code\n", caret: 0);

        Assert.Contains("Dim a = 1", result.Text);
        Assert.Contains("Dim b = 2", result.Text);
        Assert.Contains("@<p>@a</p>", result.Text);
        Assert.Contains("End Code", result.Text);
    }

    [Fact]
    public void AVoidElementEndsAtItsOwnTag()
    {
        // "<br>" has no closing tag; waiting for one swallowed End Code and
        // the rest of the page.
        var document = VbHtmlParser.Parse("@Code\n    @<br>\nEnd Code\n<p>after</p>\n");

        Assert.Empty(document.Diagnostics);
        Assert.Contains(document.Nodes.OfType<HtmlNode>(), h => h.Text.Contains("<p>after</p>"));
    }

    [Fact]
    public void AComparisonInsideAnElementIsNotATag()
    {
        var html = Render("""
            @Code
                For i = 2 To 4
                    @<li>@(i < 3)</li>
                Next
            End Code
            """);

        Assert.Contains("<li>True</li>", html);
        Assert.Contains("<li>False</li>", html);
    }

    [Fact]
    public void AnElementLeftOpenDoesNotSwallowTheBlock()
    {
        var document = VbHtmlParser.Parse("@Code\n    @<li>open\n    Dim x = 1\nEnd Code\n<p>after</p>\n");

        Assert.Empty(document.Diagnostics);
        Assert.Contains(document.Nodes.OfType<StatementNode>(), s => s.Code == "Dim x = 1");
    }

    [Fact]
    public void ACommentInsideAnElementProducesNothing()
    {
        var html = Render("@If True Then\n    @<p>a@* hidden *@b</p>\nEnd If\n");

        Assert.Contains("<p>ab</p>", html);
        Assert.DoesNotContain("hidden", html);
    }

    [Fact]
    public void AStyleInsideAnElementIsLeftAsWritten()
    {
        var html = Render("@If True Then\n    @<div><style>@media print { p { color: red } }</style></div>\nEnd If\n");

        Assert.Contains("@media print", html);
    }

    [Fact]
    public void StyleContentIsLeftAsWritten()
    {
        // "@media" is CSS, not an expression; it compiled before this change
        // and must still.
        var html = Render("@If True Then\n    @<style>@media print { p { color: red } }</style>\nEnd If\n");

        Assert.Contains("@media print", html);
    }
}
