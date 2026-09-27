using static Basalt.Tests.ComponentRendering;

namespace Basalt.Tests;

/// <summary>
/// Visual Basic components rendered by Blazor itself, checked by the markup
/// they produce.
/// </summary>
/// <remarks>
/// The other component tests read the generated code; these run it. A render
/// tree that compiles can still be one Blazor rejects, or one that puts
/// content in the wrong place, and only rendering it says which. Events are
/// exercised in a browser by the end-to-end tests.
/// </remarks>
public class ComponentRenderingTests
{
    private const string Parameter = "<Parameter>";

    private static readonly (string, string) Card = Component("Card", $"""
        <section><header>@Header</header><div class="body">@ChildContent</div></section>
        @Code
            {Parameter} Public Property Header As RenderFragment
            {Parameter} Public Property ChildContent As RenderFragment
        End Code
        """);

    [Fact]
    public async Task NamedFragmentsRenderWhereTheComponentPutsThem()
    {
        var html = await RenderAsync("Page", [], Card,
            Component("Page", """
                <Card>
                    <Header><b>Orders</b></Header>
                    <ChildContent><p>Three open</p></ChildContent>
                </Card>
                """));

        Assert.Contains("<header><b>Orders</b></header>", html);
        Assert.Contains("<div class=\"body\"><p>Three open</p></div>", html);
    }

    [Fact]
    public async Task ChildContentWithoutANameIsTheComponentsContent()
    {
        var html = await RenderAsync("Page", [], Card,
            Component("Page", "<Card><p>plain</p></Card>\n"));

        Assert.Contains("<div class=\"body\"><p>plain</p></div>", html);
    }

    [Fact]
    public async Task AFragmentOfTHandsItsValueToTheContent()
    {
        var html = await RenderAsync("Page", [],
            Component("Grid", $"""
                @typeparam TItem
                <ul>
                @For Each item In Items
                    @<li>@Row(item)</li>
                Next
                </ul>
                @Code
                    {Parameter} Public Property Items As IEnumerable(Of TItem)
                    {Parameter} Public Property Row As RenderFragment(Of TItem)
                End Code
                """),
            Component("Page", """
                <Grid Items="@numbers">
                    <Row Context="n"><span>@(n * 10)</span></Row>
                </Grid>
                @Code
                    Private numbers As Integer() = {1, 2, 3}
                End Code
                """));

        Assert.Contains("<li><span>10</span></li><li><span>20</span></li><li><span>30</span></li>", html.Replace("\n", "").Replace(" ", ""));
    }

    [Fact]
    public async Task TypedParametersArriveAsTheirTypes()
    {
        var html = await RenderAsync("Page", [],
            Component("Stepper", $"""
                <p>@(Count * 2) @(If(Big, "big", "small")) @Label</p>
                @Code
                    {Parameter} Public Property Count As Integer
                    {Parameter} Public Property Big As Boolean
                    {Parameter} Public Property Label As String = ""
                End Code
                """),
            Component("Page", """<Stepper Count="21" Big="True" Label="done" />"""));

        Assert.Contains("<p>42 big done</p>", html);
    }

    [Fact]
    public async Task ACascadedValueArrivesInADescendant()
    {
        var html = await RenderAsync("Page",
            [
                """
                Namespace Components
                    Public Class Theme
                        Public Property Name As String = ""
                    End Class
                End Namespace
                """,
            ],
            Component("Child", """
                <p>@(If(Current Is Nothing, "none", Current.Name))</p>
                @Code
                    <CascadingParameter> Public Property Current As Theme
                End Code
                """),
            Component("Page", """
                <CascadingValue Value="@theme"><div><Child /></div></CascadingValue>
                @Code
                    Private theme As New Theme With {.Name = "dark"}
                End Code
                """));

        Assert.Contains("<p>dark</p>", html);
    }

    [Fact]
    public async Task MixedAttributesAndDirectiveAttributesRender()
    {
        var html = await RenderAsync("Page", [],
            Component("Page", """
                <div class="card @kind" title="@kind" @key="kind" @attributes="extra">x</div>
                <input type="checkbox" @bind="done" />
                @Code
                    Private kind As String = "wide"
                    Private done As Boolean = True
                    Private extra As New Dictionary(Of String, Object) From {{"data-id", "7"}}
                End Code
                """));

        Assert.Contains("class=\"card wide\"", html);
        Assert.Contains("title=\"wide\"", html);
        Assert.Contains("data-id=\"7\"", html);
        Assert.Contains("checked", html);
    }

    [Fact]
    public async Task ComponentsInsideComponentsRenderInsideEachOther()
    {
        var html = await RenderAsync("Page", [], Card,
            Component("Page", "<Card><Card><em>deep</em></Card></Card>\n"));

        // The inner Card's own line break follows its </section>.
        Assert.Contains("<div class=\"body\"><section><header></header><div class=\"body\"><em>deep</em></div></section></div>", html.Replace("\n", ""));
    }
}
