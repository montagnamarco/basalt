namespace Basalt.Razor.Vb;

/// <summary>Where a directive means something.</summary>
[System.Flags]
public enum DirectiveTarget
{
    /// <summary>A .vbhtml view or Razor Page.</summary>
    View = 1,

    /// <summary>A .vbrazor component.</summary>
    Component = 2,

    Both = View | Component
}

/// <summary>A directive the parser reads, as an editor offers it.</summary>
/// <param name="Name">As written after the "@".</param>
/// <param name="Insertion">What choosing it writes after the "@".</param>
/// <param name="Description">One line on what it does.</param>
/// <param name="Target">The files it belongs in.</param>
/// <param name="Sample">A complete use, "@" included, that the parser reads as this directive.</param>
public sealed record DirectiveInfo(
    string Name, string Insertion, string Description, DirectiveTarget Target, string Sample);

/// <summary>
/// Every directive and block the parser reads after an "@", for an editor to
/// offer.
/// </summary>
/// <remarks>
/// Kept beside the parser and checked against it: each sample is parsed in
/// the tests and must come back as the directive it names, so an entry the
/// parser does not read cannot be offered. The language server offered seven
/// of these from a list of its own, and the IDE none.
/// </remarks>
public static class VbHtmlDirectives
{
    public static IReadOnlyList<DirectiveInfo> All { get; } =
    [
        new("ModelType", "ModelType ", "Declares the type of the view's model.",
            DirectiveTarget.View, "@ModelType Customer"),
        new("Imports", "Imports ", "Imports a namespace into the template.",
            DirectiveTarget.Both, "@Imports System.Text"),
        new("Inherits", "Inherits ", "Sets the class the template derives from.",
            DirectiveTarget.Both, "@Inherits BaseView"),
        new("Inject", "Inject ", "Asks the container for a service: the type, then the name.",
            DirectiveTarget.Both, "@Inject IClock Clock"),
        new("Implements", "Implements ", "Adds an interface the generated class implements.",
            DirectiveTarget.Both, "@Implements IDisposable"),
        new("Attribute", "Attribute ", "Puts an attribute on the generated class.",
            DirectiveTarget.Both, "@Attribute Authorize"),
        new("Namespace", "Namespace ", "Sets the namespace of the generated class.",
            DirectiveTarget.Both, "@Namespace Site.Pages"),
        new("Page", "Page \"/\"", "Makes the template routable, with its route.",
            DirectiveTarget.Both, "@Page \"/counter\""),
        new("Layout", "Layout \"_Layout\"", "Sets the layout the view renders inside.",
            DirectiveTarget.View, "@Layout \"_Layout\""),
        new("addTagHelper", "addTagHelper *, ", "Brings tag helpers from an assembly into scope.",
            DirectiveTarget.View, "@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers"),
        new("removeTagHelper", "removeTagHelper ", "Takes tag helpers out of scope.",
            DirectiveTarget.View, "@removeTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers"),
        new("tagHelperPrefix", "tagHelperPrefix ", "Requires a prefix on elements tag helpers apply to.",
            DirectiveTarget.View, "@tagHelperPrefix th:"),
        new("typeparam", "typeparam ", "Makes the component generic in a type parameter.",
            DirectiveTarget.Component, "@typeparam TItem"),
        new("rendermode", "rendermode InteractiveServer", "Sets how the component runs interactively.",
            DirectiveTarget.Component, "@rendermode InteractiveServer"),
        new("Code", "Code\n    \nEnd Code", "A block of Visual Basic.",
            DirectiveTarget.Both, "@Code\n    Dim x = 1\nEnd Code"),
        new("Functions", "Functions\n    \nEnd Functions", "Members declared for the template's class.",
            DirectiveTarget.Both, "@Functions\n    Function One() As Integer\n        Return 1\n    End Function\nEnd Functions"),
        new("Section", "Section Scripts\n    \nEnd Section", "Markup the layout renders where it asks for the section.",
            DirectiveTarget.View, "@Section Scripts\n    <p>x</p>\nEnd Section"),

        // Blocks: markup under Visual Basic's own control statements.
        new("If", "If  Then\n\nEnd If", "Markup shown when a condition holds.",
            DirectiveTarget.Both, "@If True Then\n    <p>x</p>\nEnd If"),
        new("For Each", "For Each item In \n\nNext", "Markup repeated for each item.",
            DirectiveTarget.Both, "@For Each item In New Integer() {1}\n    <p>x</p>\nNext"),
        new("While", "While \n\nEnd While", "Markup repeated while a condition holds.",
            DirectiveTarget.Both, "@While False\n    <p>x</p>\nEnd While"),
        new("Select Case", "Select Case \n    Case \nEnd Select", "Markup chosen between cases.",
            DirectiveTarget.Both, "@Select Case 1\n    Case 1\n        <p>x</p>\nEnd Select"),
    ];

    /// <summary>The directives that belong in a file, by its extension.</summary>
    public static IEnumerable<DirectiveInfo> For(string? filePath)
    {
        var target = filePath is not null && filePath.EndsWith(".vbrazor", System.StringComparison.OrdinalIgnoreCase)
            ? DirectiveTarget.Component
            : DirectiveTarget.View;

        return All.Where(directive => (directive.Target & target) != 0);
    }

    /// <summary>
    /// Whether a caret sits where a directive is written: an "@" at the start
    /// of a line, then nothing but the start of a name.
    /// </summary>
    public static bool IsDirectivePosition(string text, int position)
    {
        var at = Portable.Clamp(position, 0, text.Length);
        var start = at;

        while (start > 0 && (char.IsLetter(text[start - 1]))) start--;

        if (start == 0 || text[start - 1] != '@') return false;

        var lineStart = start - 1;

        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t') lineStart--;

        return lineStart == 0 || text[lineStart - 1] == '\n';
    }
}
