using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Blazor's directive attributes in a .vbrazor: @onclick and the other
/// events, @bind and its modifiers, @ref, @key, @attributes, @formname,
/// @rendermode on a component, and values mixing text and expressions.
/// </summary>
/// <remarks>
/// Each template is compiled, not just written: an event handler that does
/// not resolve, or a binder no overload takes, compiles to nothing useful and
/// says so only at build time. The render tree is also counted, because an
/// unbalanced one compiles and fails only when rendered.
/// </remarks>
public class DirectiveAttributeTests
{
    private static readonly string Component =
        Path.Combine(Path.GetTempPath(), "Site", "Components", "Panel.vbrazor");

    private static string Compile(string template)
    {
        var outcome = GeneratorRun.Run("VbComponentGenerator", null, optionStrict: true, properties: null,
            (Component, template));

        Assert.Empty(outcome.CompilationErrors);

        var code = Assert.Single(outcome.Sources).Value;

        AssertBalanced(code);

        return code;
    }

    private static void AssertBalanced(string code)
    {
        static int Count(string text, string what) =>
            (text.Length - text.Replace(what, "", StringComparison.Ordinal).Length) / what.Length;

        Assert.Equal(Count(code, ".OpenElement("), Count(code, ".CloseElement()"));
        Assert.Equal(Count(code, ".OpenComponent("), Count(code, ".CloseComponent()"));
    }

    [Fact]
    public void AnEventTakesALambda()
    {
        var code = Compile("""
            <button @onclick="Sub() count += 1">+</button>
            <p>@count</p>
            @Code
                Private count As Integer
            End Code
            """);

        Assert.Contains("\"onclick\", Global.Microsoft.AspNetCore.Components.EventCallback.Factory.Create(Of Global.Microsoft.AspNetCore.Components.Web.MouseEventArgs)(Me, Sub() count += 1)", code);
        Assert.DoesNotContain("\"@onclick\"", code);
    }

    [Fact]
    public void AnEventTakesAMethodTheArgumentsOrATask()
    {
        var code = Compile("""
            <button @onclick="AddressOf Increment">a</button>
            <button @onclick="AddressOf Clicked">b</button>
            <button @onclick="AddressOf SaveAsync">c</button>
            <button @onclick="@(Sub() count += 1)">d</button>
            <input @oninput="Sub(e) text = CStr(e.Value)" />
            <input @onkeydown="Sub(e As KeyboardEventArgs) text = e.Key" />
            <form @onsubmit="AddressOf Increment"></form>
            @Code
                Private count As Integer
                Private text As String = ""

                Private Sub Increment()
                    count += 1
                End Sub

                Private Sub Clicked(e As MouseEventArgs)
                    count = CInt(e.ClientX)
                End Sub

                Private Async Function SaveAsync() As Global.System.Threading.Tasks.Task
                    Await Global.System.Threading.Tasks.Task.Yield()
                End Function
            End Code
            """);

        // Handlers, not strings: written as a literal the page compiled and
        // the button did nothing.
        Assert.DoesNotContain("\"@on", code);
        Assert.Contains("Create(Of Global.Microsoft.AspNetCore.Components.ChangeEventArgs)(Me, Sub(e) text = CStr(e.Value))", code);
        Assert.Contains("Create(Of Global.Microsoft.AspNetCore.Components.Web.KeyboardEventArgs)", code);
        Assert.Contains("Create(Of Global.System.EventArgs)(Me, AddressOf Increment)", code);
    }

    [Fact]
    public void PreventDefaultAndStopPropagationAreFlagsOnTheEvent()
    {
        var code = Compile("""
            <a href="x" @onclick="Sub() count += 1" @onclick:preventDefault @onclick:stopPropagation="halt">go</a>
            @Code
                Private count As Integer
                Private halt As Boolean = True
            End Code
            """);

        Assert.Contains("AddEventPreventDefaultAttribute(", code);
        Assert.Contains("\"onclick\", True)", code);
        Assert.Contains("AddEventStopPropagationAttribute(", code);
        Assert.Contains("\"onclick\", halt)", code);
    }

    [Fact]
    public void ACheckboxBindsItsCheckedState()
    {
        var code = Compile("""
            <input type="checkbox" @bind="done" />
            @Code
                Private done As Boolean
            End Code
            """);

        Assert.Contains("\"checked\", Global.Microsoft.AspNetCore.Components.BindConverter.FormatValue(done)", code);
        Assert.DoesNotContain("\"value\"", code);
    }

    [Fact]
    public void BindingTakesItsEventFormatAndCulture()
    {
        var code = Compile("""
            <input @bind="text" @bind:event="oninput" />
            <input type="date" @bind="day" @bind:format="yyyy-MM-dd" />
            <input @bind="amount" @bind:culture="Global.System.Globalization.CultureInfo.InvariantCulture" />
            @Code
                Private text As String = ""
                Private day As Date
                Private amount As Decimal
            End Code
            """);

        Assert.Contains("\"oninput\"", code);
        Assert.Contains("format:=\"yyyy-MM-dd\"", code);
        Assert.Contains("culture:=Global.System.Globalization.CultureInfo.InvariantCulture", code);
        Assert.DoesNotContain("@bind", code);
    }

    [Fact]
    public void BindingRunsSomethingAfterAndTakesAGetterAndSetter()
    {
        var code = Compile("""
            <input @bind="text" @bind:after="AddressOf Saved" />
            <input @bind:get="text" @bind:set="AddressOf SetText" />
            @Code
                Private text As String = ""
                Private saves As Integer

                Private Sub Saved()
                    saves += 1
                End Sub

                Private Sub SetText(value As String)
                    text = value.Trim()
                End Sub
            End Code
            """);

        Assert.Contains("Create(Me, AddressOf Saved).InvokeAsync()", code);
        Assert.Contains("Me, AddressOf SetText, text)", code);
        Assert.DoesNotContain("@bind", code);
    }

    [Fact]
    public void AComponentBindingCarriesItsExpressionForValidation()
    {
        // InputText throws without ValueExpression: it names the field a
        // validation message belongs to.
        var code = Compile("""
            <Microsoft.AspNetCore.Components.Forms.InputText @bind-Value="name" />
            @Code
                Private name As String = ""
            End Code
            """);

        Assert.Contains("\"ValueChanged\"", code);
        Assert.Contains("\"ValueExpression\"", code);
    }

    [Fact]
    public void RefKeyAttributesAndFormNameAreWritten()
    {
        var code = Compile("""
            <input @ref="box" @key="id" @attributes="extra" />
            <form @formname="login" @onsubmit="Sub() count += 1"></form>
            <Panel @ref="child" />
            @Code
                Private box As Global.Microsoft.AspNetCore.Components.ElementReference
                Private child As Panel
                Private id As Integer = 1
                Private count As Integer
                Private extra As New Global.System.Collections.Generic.Dictionary(Of String, Object)
            End Code
            """);

        Assert.Contains("AddElementReferenceCapture(", code);
        Assert.Contains("AddComponentReferenceCapture(", code);
        Assert.Contains("SetKey(id)", code);
        Assert.Contains("AddMultipleAttributes(", code);
        Assert.Contains("AddNamedEvent(\"onsubmit\", \"login\")", code);
    }

    [Fact]
    public void AComponentCanBeGivenARenderMode()
    {
        var code = Compile("""<Panel @rendermode="InteractiveServer" />""");

        Assert.Contains("AddComponentRenderMode(InteractiveServer)", code);
    }

    [Fact]
    public void AValueMixingTextAndExpressionsIsOneAttribute()
    {
        var code = Compile("""
            <div class="box @kind wide" title="@kind">x</div>
            @Code
                Private kind As String = "a"
            End Code
            """);

        Assert.Contains("\"class\", \"box \" & Global.System.Convert.ToString(__a0) & \" wide\"", code);
        Assert.Contains("Dim __a0 = kind", code);
        Assert.Contains("\"title\", kind", code);
        Assert.DoesNotContain("class=", code);
    }

    [Fact]
    public void ABareAttributeBeforeAnotherIsItsOwn()
    {
        // "disabled class=..." was read as one attribute called
        // "disabled class".
        var code = Compile("""<button disabled class="big">x</button>""");

        Assert.Contains("\"disabled\", True", code);
        Assert.Contains("\"class\", \"big\"", code);
    }

    [Fact]
    public void AnEventHandlerIsMappedToTheTemplate()
    {
        // A caret on "count" inside the handler reaches the same word in the
        // generated code, so completion and breakpoints work there.
        const string Template = "<button @onclick=\"Sub() count += 1\">+</button>\n@Code\n    Private count As Integer\nEnd Code\n";

        var generated = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(Template), "Panel", "Components", Component);

        var original = Template.IndexOf("count", StringComparison.Ordinal);
        var written = generated.Code.IndexOf("Sub() count", StringComparison.Ordinal) + "Sub() ".Length;

        Assert.Equal(written, generated.Map.ToGenerated(original));
    }

    [Fact]
    public void MappingsSurviveAnEscapedAtSignAMemberBlockAndCrLf()
    {
        // Markup around "@@", or around a @Code block moved into the class,
        // is not one run in the template; joined as though it were, every
        // position after it was read a character or several lines early.
        const string Template =
            "<p>@@home</p>\r\n" +
            "@Code\r\n    Private count As Integer\r\nEnd Code\r\n" +
            "<button @onclick=\"Sub() count += 1\">+</button>\r\n" +
            "<div class=\"a @kind\">\r\n" +
            "</div>\r\n" +
            "@Functions\r\n    Private kind As String = \"\"\r\n@End Functions\r\n";

        var generated = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(Template), "Panel", "Components", Component);

        var handler = Template.IndexOf("count +=", StringComparison.Ordinal);
        var handlerWritten = generated.Code.IndexOf("Sub() count", StringComparison.Ordinal) + "Sub() ".Length;

        Assert.Equal(handlerWritten, generated.Map.ToGenerated(handler));

        var kind = Template.IndexOf("@kind", StringComparison.Ordinal) + 1;
        var kindWritten = generated.Code.IndexOf("Dim __a0 = kind", StringComparison.Ordinal) + "Dim __a0 = ".Length;

        Assert.Equal(kindWritten, generated.Map.ToGenerated(kind));

        // And on the right line: the button is on the template's fifth.
        var pragma = generated.Code.LastIndexOf("#ExternalSource(", handlerWritten, StringComparison.Ordinal);

        Assert.Contains(", 5)", generated.Code.Substring(pragma, generated.Code.IndexOf('\n', pragma) - pragma));
    }

    [Fact]
    public void AnEscapedAtSignInTheSameRunDoesNotShiftTheMapping()
    {
        // "@@" is one character in the page and two in the template; kept in
        // the same run of markup, everything after it mapped a character early.
        const string Template = "<p>@@home</p><button @onclick=\"Sub() count += 1\">+</button>\n@Code\n    Private count As Integer\nEnd Code\n";

        var generated = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(Template), "Panel", "Components", Component);

        var original = Template.IndexOf("count +=", StringComparison.Ordinal);
        var written = generated.Code.IndexOf("Sub() count", StringComparison.Ordinal) + "Sub() ".Length;

        Assert.Equal(written, generated.Map.ToGenerated(original));
        Assert.Matches(@"AddMarkupContent\(\d+, ""@""\)", generated.Code);
    }

    [Fact]
    public void AComponentsReferenceFollowsItsChildContent()
    {
        // Blazor takes an attribute only straight after the component or
        // another attribute: a reference capture before ChildContent made
        // <Panel @ref="p">text</Panel> fail to render.
        var code = Compile("""
            <Panel @ref="child" @key="1">text</Panel>
            @Code
                Private child As Panel
                <Microsoft.AspNetCore.Components.Parameter> Public Property ChildContent As Microsoft.AspNetCore.Components.RenderFragment
            End Code
            """);

        var content = code.IndexOf("RenderFragment))", StringComparison.Ordinal);

        Assert.True(content > 0);
        Assert.True(code.IndexOf("AddComponentReferenceCapture(", StringComparison.Ordinal) > content);
        Assert.True(code.IndexOf("SetKey(", StringComparison.Ordinal) > content);
    }

    [Fact]
    public void DirectiveAttributesWorkInsideBlocks()
    {
        // The children of an @If or a loop were written one node at a time,
        // so a tag with a directive attribute there came out as text.
        var code = Compile("""
            <ul>
            @For Each item In items
                @<li><button @onclick="Sub() picked = item">@item</button></li>
            Next
            </ul>
            @If picked IsNot Nothing Then
                @<input @bind="picked" @bind:event="oninput" />
            End If
            @Code
                Private items As String() = {"a", "b"}
                Private picked As String
            End Code
            """);

        Assert.Contains("Create(Of Global.Microsoft.AspNetCore.Components.Web.MouseEventArgs)(Me, Sub() picked = item)", code);
        Assert.Contains("\"oninput\"", code);
    }

    [Fact]
    public void AnAtSignInProseFollowedByAColonIsStillAnExpression()
    {
        var code = Compile("""
            <p>Users @online: 5</p>
            @Code
                Private online As Integer = 3
            End Code
            """);

        Assert.Contains("= online", code);
    }

    [Fact]
    public void AComponentWithContentInsideAnotherCompiles()
    {
        // Each ChildContent lambda took a parameter called __child, and Visual
        // Basic refuses a lambda parameter that hides an outer one.
        var code = Compile("""
            <Panel><p>outer</p><Panel><p>inner</p><Panel>deepest</Panel></Panel></Panel>
            @Code
                <Microsoft.AspNetCore.Components.Parameter> Public Property ChildContent As Microsoft.AspNetCore.Components.RenderFragment
            End Code
            """);

        Assert.Contains("__child3", code);
    }

    [Fact]
    public void AnAtSignInProseIsNotADirective()
    {
        // Only "@on..." in a tag, before "=" or ":", is an attribute.
        var code = Compile("""
            <p>Write to @online for news.</p>
            @Code
                Private online As String = "us"
            End Code
            """);

        Assert.Contains("AddContent(", code);
    }
}
