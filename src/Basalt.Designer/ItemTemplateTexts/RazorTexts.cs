using System.Net;

namespace Basalt.Designer;

/// <summary>
/// The Razor-for-Visual-Basic items, in the syntax README Part II documents:
/// <c>.vbhtml</c> views, a <c>.vbrazor</c> Blazor component, a <c>.vbpage</c>
/// page, and the controllers that serve them.
///
/// Each is written to be accepted by the same parser the compiler runs
/// (<c>VbHtmlParser</c>, <c>VbPageParser</c>), and the tests run them through
/// the real generators.
/// </summary>
internal static class RazorTexts
{
    /// <summary>A name inside a Visual Basic string literal: a quote is written twice.</summary>
    private static string VbString(string text) => text.Replace("\"", "\"\"");

    public static string MvcView(string name) => $"""
        @ModelType Object

        @Code
            ViewData("Title") = "{VbString(name)}"
        End Code

        <h1>@ViewData("Title")</h1>

        """;

    /// <summary>
    /// The page every view is wrapped in: RenderBody is where the view goes,
    /// and the Scripts section is optional, so a view that has none still renders.
    /// </summary>
    public static string Layout(string name) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>@ViewData("Title") - {WebUtility.HtmlEncode(name)}</title>
        </head>
        <body>
            <main>
                @RenderBody()
            </main>

            @RenderSection("Scripts", False)
        </body>
        </html>

        """;

    public static string PartialView(string name) => $"""
        @ModelType Object

        <div class="{WebUtility.HtmlEncode(name.TrimStart('_').ToLowerInvariant())}">
            @Model
        </div>

        """;

    public static string MvcController(string name) => $"""
        Imports Microsoft.AspNetCore.Mvc

        Public Class {name}
            Inherits Controller

            Public Function Index() As IActionResult
                Return View()
            End Function

        End Class

        """;

    public static string ApiController(string name) => $$"""
        Imports Microsoft.AspNetCore.Mvc

        <ApiController>
        <Route("api/[controller]")>
        Public Class {{name}}
            Inherits ControllerBase

            <HttpGet>
            Public Function GetAll() As IEnumerable(Of String)
                Return New String() {"value1", "value2"}
            End Function

        End Class

        """;

    /// <summary>
    /// A component with one parameter. <c>Parameter</c> is the attribute of
    /// <c>Microsoft.AspNetCore.Components</c>, which every component imports.
    /// </summary>
    public static string BlazorComponent(string name) => $"""
        <div class="{name.ToLowerInvariant()}">
            <h3>@Title</h3>
        </div>

        @Code
            <Parameter>
            Public Property Title As String = "{VbString(name)}"
        End Code

        """;

    public static string VbPage(string name)
    {
        var title = WebUtility.HtmlEncode(name);

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <title>{title}</title>
            </head>
            <body>
                <h1>{title}</h1>
                <p>It is <%= DateTime.Now.ToString("HH:mm") %>.</p>
            </body>
            </html>

            """;
    }
}
