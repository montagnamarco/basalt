namespace Basalt.Tests;

/// <summary>
/// The Blazor component generator, run the way the compiler runs it.
/// </summary>
public class ComponentGeneratorTests
{
    // Rooted in the temporary folder rather than at C:\: a backslash is no
    // separator off Windows, and _Imports.vbrazor went unrecognised there.
    private static readonly string Site = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Site");

    private static string InSite(params string[] parts) => System.IO.Path.Combine([Site, .. parts]);

    private static readonly string Path = InSite("Components", "Open.vbrazor");

    private static readonly string CounterPath = InSite("Components", "Counter.vbrazor");

    [Fact]
    public void ReportsWhatTheParserFoundWrongOnTheTemplate()
    {
        // An @If never closed. The parser says so; the generator used to drop
        // what it said, and the build failed later on generated code, or
        // rendered half the markup and said nothing at all.
        var outcome = GeneratorRun.Run("VbComponentGenerator",
            (Path, "<p>start</p>\n@If True Then\n    <p>never closed</p>\n"));

        Assert.Null(outcome.Exception);

        var problem = Assert.Single(outcome.Diagnostics, d => d.Id == "VBRZ013");

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(Path, problem.Location.GetLineSpan().Path);
        Assert.Equal(1, problem.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void WarnsRatherThanFailsOnADirectiveNotSupportedYet()
    {
        // @preservewhitespace is skipped by the parser today. The component still
        // builds, as it always did; failing the build over it would break
        // projects that compiled before the parser's findings were reported.
        var outcome = GeneratorRun.Run("VbComponentGenerator",
            (Path, "@preservewhitespace true\n<p>hi</p>\n"));

        Assert.Null(outcome.Exception);
        Assert.DoesNotContain(outcome.Diagnostics,
            d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains(outcome.Diagnostics, d => d.Id == "VBRZ014");
    }

    [Fact]
    public void AComponentCompilesUnderTheProjectsOptionStrictOn()
    {
        var outcome = GeneratorRun.Run("VbComponentGenerator", null, optionStrict: true, properties: null,
            (Path, """
                @Page "/count"
                <p>Count: @count</p>
                <button onclick="@AddressOf Increment">+1</button>
                <input value="@name" />
                @Functions
                    Private count As Integer
                    Private name As String = ""
                    Private Sub Increment()
                        count += 1
                    End Sub
                @End Functions
                """));

        Assert.Empty(outcome.CompilationErrors);
        Assert.Contains("Option Strict On", Assert.Single(outcome.Sources).Value);
    }

    private static GeneratorRun.Outcome Run(string[] code, params (string Path, string Text)[] templates) =>
        GeneratorRun.Run("VbComponentGenerator", null, optionStrict: false, properties: null, code, templates);

    [Fact]
    public void ACodeBehindAddsToTheSameClass()
    {
        // Counter.vbrazor.vb declaring Public Class Counter: Visual Basic lets
        // one of two declarations leave Partial out, so the generated one must
        // carry it; it did not, and the two collided (BC30179).
        var outcome = Run(
            ["""
             Namespace Components
                 Public Class Counter
                     Private ReadOnly Property Label As String = "from the code-behind"
                 End Class
             End Namespace
             """],
            (CounterPath, "<p>@Label</p>\n"));

        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ACodeBlockAtTheTopDeclaresMembersAsBlazorsCodeDoes()
    {
        // @code in a .razor holds members; a @Code block at the top of a
        // .vbrazor was written into BuildRenderTree, where "Private" does
        // not compile.
        var outcome = Run([],
            (CounterPath, """
                <p>@count</p>
                @Code
                    Private count As Integer = 5

                    Private Sub Increment()
                        count += 1
                    End Sub
                End Code
                """));

        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ImportsVbrazorLendsItsDirectivesAndIsNotAComponent()
    {
        var outcome = Run(
            ["""
             Namespace Components
                 Public Class MainLayout
                     Inherits Global.Microsoft.AspNetCore.Components.LayoutComponentBase
                 End Class
             End Namespace
             """],
            (InSite("Components", "_Imports.vbrazor"), """
                @Imports System.Text
                @Inject Microsoft.Extensions.Logging.ILoggerFactory Loggers
                @Layout "MainLayout"
                """),
            (InSite("Components", "Pages", "Counter.vbrazor"),"<p>@(New StringBuilder(\"x\").ToString()) @(Loggers IsNot Nothing)</p>\n"));

        Assert.Empty(outcome.CompilationErrors);

        var counter = Assert.Single(outcome.Sources).Value;

        Assert.Contains("Components.Layout(GetType(MainLayout))", counter);
        Assert.DoesNotContain(outcome.Sources.Keys, key => key.Contains("_Imports", StringComparison.Ordinal));
    }

    [Fact]
    public void NamespaceAttributeAndImplementsShapeTheClass()
    {
        var outcome = Run([],
            (CounterPath, """
                @Namespace Shop.Widgets
                @Attribute <Global.Microsoft.AspNetCore.Components.StreamRendering>
                @Implements System.IDisposable
                <p>hi</p>
                @Functions
                    Public Sub Dispose() Implements System.IDisposable.Dispose
                    End Sub
                @End Functions
                """));

        Assert.Empty(outcome.CompilationErrors);

        var code = Assert.Single(outcome.Sources).Value;

        Assert.Contains("Namespace Shop.Widgets", code);
        Assert.Contains("<Global.Microsoft.AspNetCore.Components.StreamRendering>", code);
        Assert.Contains("Implements System.IDisposable", code);
    }

    [Fact]
    public void ATagWithTwoExpressionAttributesIsOneTag()
    {
        // The router, as a Blazor app's Routes writes it. After the first
        // expression the child content was opened, " " written into it as
        // markup and the second parameter given to the wrong frame: the
        // component did not compile. A literal attribute after an expression
        // one was written into the page as text.
        var outcome = Run([],
            (CounterPath, """
                @Imports Microsoft.AspNetCore.Components.Routing
                <Router AppAssembly="@GetType(Counter).Assembly" Found="@Found" />
                <button onclick="@AddressOf Go" class="big">go</button>
                @Functions
                    Private ReadOnly Property Found As Microsoft.AspNetCore.Components.RenderFragment(Of Microsoft.AspNetCore.Components.RouteData) =
                        Function(data) Sub(builder)
                                       End Sub

                    Private Sub Go()
                    End Sub
                @End Functions
                """));

        Assert.Empty(outcome.CompilationErrors);

        var code = Assert.Single(outcome.Sources).Value;

        Assert.DoesNotContain("ChildContent", code);
        Assert.Contains("AddAttribute(", code);
        Assert.Contains("\"class\", \"big\"", code);
        Assert.DoesNotContain("class=", code);
        AssertBalanced(code);
    }

    [Fact]
    public void ExpressionAttributesLeaveTheTreeBalanced()
    {
        // An unbalanced render tree compiles and fails only when rendered,
        // so the opens and closes are counted. A void element reached
        // through an expression attribute was never closed, and every later
        // sibling ended up inside the <input>.
        var outcome = Run([],
            (CounterPath, """
                <input value="@name" title="@name">
                <p>after</p>
                <Counter Label="@name" Other="@name">
                    <b>child</b>
                </Counter>
                @Functions
                    Private name As String = ""
                    <Microsoft.AspNetCore.Components.Parameter> Public Property Label As String
                    <Microsoft.AspNetCore.Components.Parameter> Public Property Other As String
                    <Microsoft.AspNetCore.Components.Parameter> Public Property ChildContent As Microsoft.AspNetCore.Components.RenderFragment
                @End Functions
                """));

        Assert.Empty(outcome.CompilationErrors);

        var code = Assert.Single(outcome.Sources).Value;

        AssertBalanced(code);
        Assert.Contains("ChildContent", code);
    }

    private static void AssertBalanced(string code)
    {
        static int Count(string text, string what) =>
            (text.Length - text.Replace(what, "", StringComparison.Ordinal).Length) / what.Length;

        Assert.Equal(Count(code, ".OpenElement("), Count(code, ".CloseElement()"));
        Assert.Equal(Count(code, ".OpenComponent("), Count(code, ".CloseComponent()"));
    }

    [Fact]
    public void ADoctypeIsMarkupAndACommentIsDropped()
    {
        // An App component begins with <!DOCTYPE html>; read as a tag it
        // opened an element nothing closed, and the app did not render.
        var outcome = Run([],
            (CounterPath, "<!DOCTYPE html>\n<!-- <p>not a paragraph</p> -->\n<html><body></body></html>\n"));

        Assert.Empty(outcome.CompilationErrors);

        var code = Assert.Single(outcome.Sources).Value;

        Assert.Contains("\"<!DOCTYPE html>\"", code);
        Assert.DoesNotContain("OpenElement(1, \"!DOCTYPE\")", code);
        Assert.DoesNotContain("not a paragraph", code);
    }

    [Fact]
    public void AMemberBlockMayOpenWithACommentOrARegion()
    {
        var outcome = Run([],
            (CounterPath, """
                <p>@count</p>
                @Code
                    ' The state the button changes.
                    #Region "State"
                    Private count As Integer
                    #End Region
                End Code
                """));

        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void ACodeBlockThatRunsStatementsStaysInTheRenderTree()
    {
        // Only declarations become members. A Dim with no As clause, and a
        // block the parser split around markup, are statements: moved into
        // the class, the first failed under Option Strict and the second
        // did not compile at all.
        var outcome = GeneratorRun.Run("VbComponentGenerator", null, optionStrict: true, properties: null,
            (CounterPath, """
                @Code
                    Dim total = 3
                End Code
                <ul>
                @Code
                    For index = 1 To total
                        @<li>@index</li>
                    Next
                End Code
                </ul>
                """));

        Assert.Empty(outcome.CompilationErrors);
    }

    [Fact]
    public void TheNearestImportsFileWins()
    {
        // Applied outermost first, the outermost used to keep what it set:
        // Admin's own layout was ignored for MainLayout.
        (string, string)[] layouts =
        [
            (InSite("Components", "Layout", "MainLayout.vbrazor"), "@Inherits Microsoft.AspNetCore.Components.LayoutComponentBase\n@Body\n"),
            (InSite("Components", "Layout", "AdminLayout.vbrazor"), "@Inherits Microsoft.AspNetCore.Components.LayoutComponentBase\n@Body\n"),
        ];

        var outcome = Run([],
            [
                .. layouts,
                (InSite("Components", "_Imports.vbrazor"), "@Imports Components.Layout\n@Layout \"MainLayout\"\n"),
                (InSite("Components", "Admin", "_Imports.vbrazor"), "@Layout \"AdminLayout\"\n@Attribute <Global.Microsoft.AspNetCore.Authorization.Authorize>\n"),
                (InSite("Components", "Admin", "Users.vbrazor"), "<p>users</p>\n"),
                (InSite("Components", "Home.vbrazor"), "<p>home</p>\n"),
            ]);

        Assert.Empty(outcome.CompilationErrors);

        var users = outcome.Sources.Single(s => s.Key.StartsWith("Users.", StringComparison.Ordinal)).Value;
        var home = outcome.Sources.Single(s => s.Key.StartsWith("Home.", StringComparison.Ordinal)).Value;

        Assert.Contains("Layout(GetType(AdminLayout))", users);
        Assert.Contains("Layout(GetType(MainLayout))", home);

        // A folder-wide @Attribute reaches the folder's components, and only them.
        Assert.Contains("Authorization.Authorize>", users);
        Assert.DoesNotContain("Authorize", home);
    }

    [Fact]
    public void RenderModeBecomesTheAttributeBlazorReads()
    {
        var outcome = Run([],
            (CounterPath, "@rendermode InteractiveServer\n<p>hi</p>\n"));

        Assert.Empty(outcome.CompilationErrors);
        Assert.DoesNotContain(outcome.Diagnostics, d => d.Id == "VBRZ014");
        Assert.DoesNotContain(outcome.Diagnostics, d => d.GetMessage().Contains("@rendermode"));

        var code = Assert.Single(outcome.Sources).Value;

        Assert.Contains("Inherits Global.Microsoft.AspNetCore.Components.RenderModeAttribute", code);
        Assert.Contains("<Counter.__PrivateComponentRenderModeAttribute>", code);
    }

    [Fact]
    public void RenderModeIsMappedToItsExpression()
    {
        const string Template = "@rendermode InteractiveWebAssembly\n<p>hi</p>\n";

        var document = Basalt.Razor.Vb.VbHtmlParser.Parse(Template);
        var generated = Basalt.Razor.Vb.VbComponentWriter.WriteWithMap(document, "Counter", "Components", CounterPath);

        var original = Template.IndexOf("InteractiveWebAssembly", StringComparison.Ordinal);
        var written = generated.Code.IndexOf("Return InteractiveWebAssembly", StringComparison.Ordinal) + "Return ".Length;

        Assert.Equal(written, generated.Map.ToGenerated(original));
    }

    [Fact]
    public void TiesTheGeneratedCodeToTheTemplate()
    {
        var outcome = GeneratorRun.Run("VbComponentGenerator",
            (Path, "<p>@(1 + 1)</p>\n"));

        Assert.Null(outcome.Exception);

        var code = Assert.Single(outcome.Sources).Value;

        Assert.Contains($"#ExternalSource(\"{Path}\", 1)", code, StringComparison.Ordinal);
        Assert.Contains($"#ExternalChecksum(\"{Path}\"", code, StringComparison.Ordinal);
    }
}
