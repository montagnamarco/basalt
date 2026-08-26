using Basalt.Razor.Vb;

namespace Basalt.Workspace.Web;

/// <summary>
/// The Visual Basic a template compiles to, for the editor to ask about.
/// </summary>
/// <remarks>
/// One place, because the answer depends on which kind of template it is and
/// every provider needs the same answer. A .vbrazor is a Blazor component and
/// a .vbhtml is a view: they carry the same syntax and compile to different
/// classes, so generating the view shape for a component offered the members
/// of RazorPage — a completion list that looks right and names nothing the
/// class actually has.
/// </remarks>
internal static class TemplateGeneration
{
    /// <summary>Whether this path is a Blazor component.</summary>
    public static bool IsComponent(string? path) =>
        path is not null
        && path.EndsWith(".vbrazor", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The generated code and its mappings, whichever kind of template it is.
    /// </summary>
    public static Generated For(
        VbHtmlDocument document, string? filePath, ViewHost host)
    {
        if (IsComponent(filePath))
        {
            var component = VbComponentWriter.WriteWithMap(
                document, "GeneratedComponent", "Basalt.Generated", filePath);

            return new Generated(component.Code, component.Map);
        }

        var view = VbHtmlCodeWriter.WriteWithMap(
            document, "GeneratedView", "Basalt.Generated", filePath, host);

        return new Generated(view.Code, view.Map);
    }

    /// <summary>Generated code, with the map back to the template.</summary>
    public sealed record Generated(string Code, SourceMap Map);
}
