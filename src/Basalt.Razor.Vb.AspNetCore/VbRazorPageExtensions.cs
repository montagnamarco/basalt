using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Razor;

namespace Basalt.Razor.Vb.AspNetCore;

/// <summary>
/// Helpers the generated views call, for pages served by ASP.NET Core.
/// </summary>
public static class VbRazorPageExtensions
{
    /// <summary>
    /// Writes an attribute whose whole value is one expression, leaving it out
    /// when the value is Nothing or False.
    /// </summary>
    /// <remarks>
    /// <c>disabled="@isLocked"</c> must produce no attribute at all when the
    /// value is False: for a boolean attribute the browser reads its presence,
    /// not its value, so writing <c>disabled="False"</c> disables the control.
    /// True renders the attribute's own name, which is how
    /// <c>disabled="disabled"</c> is written.
    /// </remarks>
    public static void WriteAttribute(this RazorPageBase page, string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (value is null or false) return;

        page.Output.Write(' ');
        page.Output.Write(name);
        page.Output.Write("=\"");

        if (value is true)
            page.Output.Write(name);
        else if (value is IHtmlContent content)
            content.WriteTo(page.Output, page.HtmlEncoder);
        else
            page.Write(value);

        page.Output.Write('"');
    }
}
