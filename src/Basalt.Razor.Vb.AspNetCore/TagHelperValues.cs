using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.Runtime.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Basalt.Razor.Vb.AspNetCore;

/// <summary>
/// How attribute values written with @expressions reach a tag helper, for the
/// code the Visual Basic view generator writes.
/// </summary>
/// <remarks>
/// The C# compiler writes these inline with helpers of the page base class;
/// kept here instead so the generated Visual Basic stays short, and so the
/// rules live in one place: literal text is markup already, a value that is
/// IHtmlContent is written as it is, anything else is encoded.
/// </remarks>
public static class TagHelperValues
{
    /// <summary>
    /// An HTML attribute value built from pieces: markup as written, and the
    /// values of the expressions between them.
    /// </summary>
    /// <param name="parts">Literal markup as <see cref="HtmlString"/>, values as they are.</param>
    public static IHtmlContent Html(params object?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var content = new HtmlContentBuilder();

        foreach (var part in parts)
        {
            switch (part)
            {
                case null:
                    break;

                case IHtmlContent html:
                    content.AppendHtml(html);
                    break;

                default:
                    content.Append(Convert.ToString(part, CultureInfo.CurrentCulture));
                    break;
            }
        }

        return content;
    }

    /// <summary>
    /// A string property's value built from pieces, as a string.
    /// </summary>
    /// <remarks>
    /// An IHtmlContent piece is written out rather than turned into its type
    /// name, which is what Convert.ToString did with it.
    /// </remarks>
    public static string Text(params object?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        using var writer = new StringWriter(CultureInfo.CurrentCulture);

        foreach (var part in parts)
        {
            switch (part)
            {
                case null:
                    break;

                case HtmlString markup:
                    writer.Write(markup.Value);
                    break;

                case IHtmlContent html:
                    html.WriteTo(writer, HtmlEncoder.Default);
                    break;

                default:
                    writer.Write(Convert.ToString(part, CultureInfo.CurrentCulture));
                    break;
            }
        }

        return writer.ToString();
    }

    /// <summary>
    /// Adds an HTML attribute whose whole value is one expression, the way a
    /// .cshtml treats it: left out when Nothing or False, written as its own
    /// name when True.
    /// </summary>
    /// <remarks>
    /// <c>&lt;input asp-for="Name" disabled="@locked"&gt;</c> rendered
    /// <c>disabled="False"</c>, which the browser reads as disabled.
    /// </remarks>
    public static void AddConditional(
        TagHelperExecutionContext context, string name, object? value, HtmlAttributeValueStyle style)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (value is null or false) return;

        context.AddHtmlAttribute(name, value is true ? new HtmlString(name) : value, style);
    }
}
