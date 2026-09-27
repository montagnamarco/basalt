namespace Basalt.Tests;

/// <summary>
/// Components written knowing the parameters of the components they use:
/// named RenderFragment parameters, RenderFragment(Of T) with a context,
/// typed literal values and EventCallback parameters.
/// </summary>
/// <remarks>
/// The generator learns the parameters from the compilation before it writes
/// any render tree, from .vbrazor components, Visual Basic classes and
/// referenced libraries alike. Every template here is compiled, and its
/// render tree's opens and closes counted.
/// </remarks>
public class TypedComponentTests
{
    private static readonly string Site = Path.Combine(Path.GetTempPath(), "Site");

    private static (string, string) Component(string name, string text) =>
        (Path.Combine(Site, "Components", name + ".vbrazor"), text);

    private static IReadOnlyDictionary<string, string> Compile(string[] code, params (string Path, string Text)[] templates)
    {
        var outcome = GeneratorRun.Run("VbComponentGenerator", Site, optionStrict: true, properties: null, code, templates);

        Assert.Null(outcome.Exception);
        Assert.Empty(outcome.CompilationErrors);

        foreach (var source in outcome.Sources.Values) AssertBalanced(source);

        return outcome.Sources;
    }

    private static string SourceOf(IReadOnlyDictionary<string, string> sources, string className) =>
        Assert.Single(sources, s => s.Key.StartsWith(className + ".", StringComparison.Ordinal)).Value;

    private static void AssertBalanced(string code)
    {
        static int Count(string text, string what) =>
            (text.Length - text.Replace(what, "", StringComparison.Ordinal).Length) / what.Length;

        Assert.Equal(Count(code, ".OpenElement("), Count(code, ".CloseElement()"));
        Assert.Equal(Count(code, ".OpenComponent("), Count(code, ".CloseComponent()"));
    }

    private const string Parameter = "<Microsoft.AspNetCore.Components.Parameter>";

    [Fact]
    public void AnElementNamedAfterAFragmentParameterIsThatParameter()
    {
        // <Header> inside <Card> was opened as a component called Header,
        // which did not exist, and the build failed.
        var sources = Compile([],
            Component("Card", $"""
                <div>@Header</div><div>@Body</div>
                @Code
                    {Parameter} Public Property Header As Microsoft.AspNetCore.Components.RenderFragment
                    {Parameter} Public Property Body As Microsoft.AspNetCore.Components.RenderFragment
                End Code
                """),
            Component("Page", """
                <Card>
                    <Header><b>Title</b></Header>
                    <Body>Some text</Body>
                </Card>
                """));

        var page = SourceOf(sources, "Page");

        Assert.Contains("\"Header\", CType(Sub(__child1", page);
        Assert.Contains("\"Body\", CType(Sub(__child1", page);
        Assert.DoesNotContain("OpenComponent(Of Header)", page);
        Assert.DoesNotContain("\"ChildContent\"", page);
    }

    [Fact]
    public void AFragmentOfTNamesItsValueContextOrWhatContextSays()
    {
        var sources = Compile([],
            Component("Lister", $"""
                <ul>
                @For Each item In Items
                    @Row(item)
                Next
                </ul>
                @Code
                    {Parameter} Public Property Items As String() = {"{}"}
                    {Parameter} Public Property Row As Microsoft.AspNetCore.Components.RenderFragment(Of String)
                End Code
                """),
            Component("Echo", $"""
                <p>@ChildContent("x")</p>
                @Code
                    {Parameter} Public Property ChildContent As Microsoft.AspNetCore.Components.RenderFragment(Of String)
                End Code
                """),
            Component("Page", """
                <Lister Items="@names">
                    <Row Context="name"><li>@name</li></Row>
                </Lister>
                <Echo>@context.ToUpper()</Echo>
                @Code
                    Private names As String() = {"a", "b"}
                End Code
                """));

        var page = SourceOf(sources, "Page");

        Assert.Contains("CType(Function(name As String) Sub(__child1", page);
        Assert.Contains("CType(Function(context As String) Sub(__child1", page);
    }

    [Fact]
    public void AGenericComponentsFragmentTakesTheTypeTheTagWrote()
    {
        // A generic component written in Visual Basic, used with its type
        // argument in the tag: RenderFragment(Of TItem) takes an Integer.
        var sources = Compile(
            [
                """
                Imports System.Collections.Generic
                Imports Microsoft.AspNetCore.Components
                Imports Microsoft.AspNetCore.Components.Rendering

                Namespace Components
                    Public Class Grid(Of TItem)
                        Inherits ComponentBase

                        <Parameter> Public Property Items As IEnumerable(Of TItem)
                        <Parameter> Public Property Row As RenderFragment(Of TItem)

                        Protected Overrides Sub BuildRenderTree(builder As RenderTreeBuilder)
                            For Each item In Items
                                builder.AddContent(0, Row(item))
                            Next
                        End Sub
                    End Class
                End Namespace
                """,
            ],
            Component("Page", """
                <Grid(Of Integer) Items="@numbers">
                    <Row><span>@(context * 2)</span></Row>
                </Grid>
                @Code
                    Private numbers As Integer() = {1, 2}
                End Code
                """));

        Assert.Contains("Function(context As Integer) Sub(__child1", SourceOf(sources, "Page"));
    }

    [Fact]
    public void ALiteralForATypedParameterIsVisualBasic()
    {
        // Count="5" for an Integer was the text "5": it compiled, and failed
        // at render with an invalid cast.
        var sources = Compile([],
            Component("Stepper", $"""
                <p>@Count @Label @Big</p>
                @Code
                    {Parameter} Public Property Count As Integer
                    {Parameter} Public Property Label As String = ""
                    {Parameter} Public Property Big As Boolean
                End Code
                """),
            Component("Page", """
                <Stepper count="5" Label="five" Big="true" />
                """));

        var page = SourceOf(sources, "Page");

        Assert.Contains("\"Count\", 5)", page);
        Assert.Contains("\"Label\", \"five\")", page);
        Assert.Contains("\"Big\", true)", page);
    }

    [Fact]
    public void AnEventCallbackParameterTakesALambda()
    {
        var sources = Compile([],
            Component("Saver", $"""
                <button @onclick="AddressOf Save">save</button>
                @Code
                    {Parameter} Public Property OnSave As Microsoft.AspNetCore.Components.EventCallback
                    {Parameter} Public Property OnText As Microsoft.AspNetCore.Components.EventCallback(Of String)

                    Private Async Function Save() As Global.System.Threading.Tasks.Task
                        Await OnSave.InvokeAsync()
                        Await OnText.InvokeAsync("saved")
                    End Function
                End Code
                """),
            Component("Page", """
                <Saver OnSave="Sub() saves += 1" OnText="Sub(text) last = text" />
                <p>@saves @last</p>
                @Code
                    Private saves As Integer
                    Private last As String = ""
                End Code
                """));

        var page = SourceOf(sources, "Page");

        Assert.Contains("\"OnSave\", Global.Microsoft.AspNetCore.Components.EventCallback.Factory.Create(Me, Sub() saves += 1))", page);
        Assert.Contains("EventCallback.Factory.Create(Of String)(Me, Sub(text) last = text))", page);
    }

    [Fact]
    public void AParameterWhoseTypeDoesNotResolveDoesNotStopTheGenerator()
    {
        // An unresolved type has no namespace; reading it threw, and every
        // component of the project disappeared with the exception.
        var outcome = GeneratorRun.Run("VbComponentGenerator", Site, optionStrict: true, properties: null, [],
            Component("Broken", $"""
                <p>@Thing</p>
                @Code
                    {Parameter} Public Property Thing As Missing.Kind
                End Code
                """),
            Component("Page", "<Broken Thing=\"Nothing\" />\n"));

        Assert.Null(outcome.Exception);
        Assert.Equal(2, outcome.Sources.Count);
    }

    private static readonly (string, string) Card = Component("Card", $"""
        <div>@Header</div><div>@ChildContent</div>
        @Code
            {Parameter} Public Property Header As Microsoft.AspNetCore.Components.RenderFragment
            {Parameter} Public Property ChildContent As Microsoft.AspNetCore.Components.RenderFragment
        End Code
        """);

    [Fact]
    public void AnHtmlElementIsNotAFragmentParameterOfTheSameNameInLowerCase()
    {
        // <header> is HTML; matched without regard to case it became the
        // Header parameter and lost its class.
        var page = SourceOf(Compile([], Card,
            Component("Page", """<Card><header class="top">x</header></Card>""")), "Page");

        Assert.Contains("OpenElement(", page);
        Assert.Contains("\"class\", \"top\"", page);
        Assert.Contains("\"ChildContent\"", page);
    }

    [Fact]
    public void ContentStartsWhereverItStarts()
    {
        // With content opened lazily, an @If first, a component with nothing
        // inside and components inside components must all still balance and
        // compile, and HTML after the content began is still HTML.
        var page = SourceOf(Compile([], Card,
            Component("Page", """
                <Card></Card>
                <Card>
                    @If shown Then
                        @<p>shown</p>
                    End If
                </Card>
                <Card><Card><b>inner</b></Card></Card>
                <Card><p>first</p><header>html, after content began</header></Card>
                @Code
                    Private shown As Boolean = True
                End Code
                """)), "Page");

        Assert.Contains("__child2", page);
    }

    [Fact]
    public void AGenericComponentsCallbackAndFragmentTakeTheTagsTypes()
    {
        // EventCallback(Of TValue) and RenderFragment(Of TKey) typed by the
        // tag's arguments, each to its own parameter.
        var sources = Compile(
            [
                """
                Imports Microsoft.AspNetCore.Components

                Namespace Components
                    Public Class Pair(Of TKey, TValue)
                        Inherits ComponentBase

                        <Parameter> Public Property OnPick As EventCallback(Of TValue)
                        <Parameter> Public Property Row As RenderFragment(Of TKey)
                    End Class
                End Namespace
                """,
            ],
            Component("Page", """
                <Pair(Of Integer, String) OnPick="Sub(text) last = text">
                    <Row>@(context + 1)</Row>
                </Pair>
                @Code
                    Private last As String = ""
                End Code
                """));

        var page = SourceOf(sources, "Page");

        Assert.Contains("Create(Of String)(Me, Sub(text) last = text)", page);
        Assert.Contains("Function(context As Integer)", page);
    }

    /// <summary>A catalog that knows one component, for the writer on its own.</summary>
    private sealed class OneComponent(Basalt.Razor.Vb.ComponentShape shape) : Basalt.Razor.Vb.IComponentCatalog
    {
        public Basalt.Razor.Vb.ComponentShape? Find(string tagName, int typeArgumentCount) =>
            tagName == "Stepper" ? shape : null;
    }

    [Fact]
    public void ATypedLiteralAndAnEventCallbackAreMappedToTheTemplate()
    {
        // Count="42" and OnStep="Sub() steps += 1" are Visual Basic in the
        // generated code now, so a caret on them has to land on the same
        // characters there.
        const string Page = "<Stepper Count=\"42\" OnStep=\"Sub() steps += 1\" />\n";

        var shape = new Basalt.Razor.Vb.ComponentShape("Global.Components.Stepper", [],
        [
            new("Count", "Integer", Basalt.Razor.Vb.ParameterKind.Value, null),
            new("OnStep", "Global.Microsoft.AspNetCore.Components.EventCallback", Basalt.Razor.Vb.ParameterKind.EventCallback, null),
        ]);

        var generated = Basalt.Razor.Vb.VbComponentWriter.WriteWithMap(
            Basalt.Razor.Vb.VbHtmlParser.Parse(Page), "Page", "Components",
            Path.Combine(Site, "Components", "Page.vbrazor"), catalog: new OneComponent(shape));

        var count = Page.IndexOf("42", StringComparison.Ordinal);
        var countWritten = generated.Code.IndexOf("\"Count\", 42", StringComparison.Ordinal) + "\"Count\", ".Length;

        Assert.Equal(countWritten, generated.Map.ToGenerated(count));

        var steps = Page.IndexOf("steps", StringComparison.Ordinal);
        var stepsWritten = generated.Code.IndexOf("Sub() steps", StringComparison.Ordinal) + "Sub() ".Length;

        Assert.Equal(stepsWritten, generated.Map.ToGenerated(steps));
    }

    [Fact]
    public void AComponentWithTypeParamIsGeneric()
    {
        // @typeparam was refused as unsupported: a generic component had to be
        // written in a .vb file by hand.
        var sources = Compile([],
            Component("Grid", $"""
                @typeparam TItem As {"{IComparable}"}
                <ul>
                @For Each item In Items
                    @<li>@Row(item)</li>
                Next
                </ul>
                @Code
                    {Parameter} Public Property Items As Global.System.Collections.Generic.IEnumerable(Of TItem)
                    {Parameter} Public Property Row As Microsoft.AspNetCore.Components.RenderFragment(Of TItem)
                End Code
                """),
            Component("Page", """
                <Grid(Of String) Items="@names">
                    <Row>@context.Length</Row>
                </Grid>
                @Code
                    Private names As String() = {"a", "bb"}
                End Code
                """));

        Assert.Contains("Partial Public Class Grid(Of TItem As {IComparable})", SourceOf(sources, "Grid"));
        Assert.Contains("Function(context As String)", SourceOf(sources, "Page"));
    }

    [Fact]
    public void TypeArgumentsNamingTheParentsOwnParametersAreSubstitutedAllAtOnce()
    {
        // <Pair(Of TValue, TKey)> inside a component generic over TKey and
        // TValue: substituted one after the other, both became TKey.
        var sources = Compile([],
            Component("Pair", $"""
                @typeparam TKey
                @typeparam TValue
                <p>@Row(Nothing)</p>
                @Code
                    {Parameter} Public Property OnPick As Microsoft.AspNetCore.Components.EventCallback(Of TValue)
                    {Parameter} Public Property Row As Microsoft.AspNetCore.Components.RenderFragment(Of TKey)
                End Code
                """),
            Component("Outer", """
                @typeparam TKey
                @typeparam TValue
                <Pair(Of TValue, TKey) OnPick="Sub(key) lastKey = key">
                    <Row>@context</Row>
                </Pair>
                @Code
                    Private lastKey As TKey
                End Code
                """));

        var outer = SourceOf(sources, "Outer");

        Assert.Contains("Create(Of TKey)(Me, Sub(key) lastKey = key)", outer);
        Assert.Contains("Function(context As TValue)", outer);
    }

    [Fact]
    public void AFrameworkComponentsParametersAreKnownToo()
    {
        // NavLink comes from a referenced library: Match takes an enum, so the
        // literal is Visual Basic, not the text "NavLinkMatch.All".
        var sources = Compile([],
            Component("Page", """
                @Imports Microsoft.AspNetCore.Components.Routing
                <NavLink href="" Match="NavLinkMatch.All">Home</NavLink>
                """));

        Assert.Contains("\"Match\", NavLinkMatch.All)", SourceOf(sources, "Page"));
    }
}
