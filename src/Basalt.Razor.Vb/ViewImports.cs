using System;
using System.Linq;

namespace Basalt.Razor.Vb;

/// <summary>
/// The directives a folder applies to every view inside it.
///
/// Razor calls the file _ViewImports.cshtml; here it is _ViewImports.vbhtml.
/// It exists so that a project does not repeat the same Imports at the top of
/// forty views, and so that changing one changes them all.
///
/// Only the directives are taken: markup in a _ViewImports file would have
/// nowhere sensible to go, since the file is not a view.
/// </summary>
public static class ViewImports
{
    /// <summary>The name of the file, as a project writes it.</summary>
    public const string FileName = "_ViewImports.vbhtml";

    /// <summary>The name of the file that sets a default layout.</summary>
    public const string ViewStartFileName = "_ViewStart.vbhtml";

    /// <summary>
    /// Whether a path is one of the two shared files.
    ///
    /// They are not views and must not be generated as classes: doing so
    /// produces a page nobody asked for and a name that collides.
    /// </summary>
    public static bool IsShared(string path)
    {
        var name = Path.GetFileName(path);

        return string.Equals(name, FileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, ViewStartFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies a folder's shared directives to a view.
    ///
    /// The view's own directives win: a template that declares its own model
    /// type means it, and a shared file cannot know better.
    /// </summary>
    public static void ApplyTo(VbHtmlDocument view, VbHtmlDocument shared) =>
        ApplyTo(view, shared, folderBelowShared: "");

    /// <summary>
    /// Applies a shared file to a view that sits <paramref name="folderBelowShared"/>
    /// below it, as a dotted namespace suffix ("Admin.Reports"; empty beside it).
    /// </summary>
    /// <remarks>
    /// @Inject and @Namespace used to stop at the shared file: a service
    /// injected once for every view was undefined in each of them, and the
    /// namespace a folder asked for was ignored. The view's own directives
    /// still win. A namespace carries on down the folders, as C# does:
    /// "@Namespace App.Pages" in Pages makes Pages/Admin/Index
    /// App.Pages.Admin.
    /// </remarks>
    public static void ApplyTo(VbHtmlDocument view, VbHtmlDocument shared, string folderBelowShared)
    {
        foreach (var import in shared.Imports)
            if (!view.Imports.Contains(import))
                view.Imports.Insert(0, import);

        view.ModelType ??= shared.ModelType;
        view.Inherits ??= shared.Inherits;

        foreach (var service in shared.Injected)
        {
            if (!view.Injected.Any(s => string.Equals(s.Name, service.Name, StringComparison.OrdinalIgnoreCase)))
                view.Injected.Add(service);
        }

        if (!string.IsNullOrWhiteSpace(shared.Namespace) && !view.DeclaresNamespace)
        {
            view.Namespace = folderBelowShared.Length == 0
                ? shared.Namespace
                : $"{shared.Namespace}.{folderBelowShared}";
        }
    }

    /// <summary>
    /// The layout a _ViewStart file sets, if it sets one.
    ///
    /// Razor writes it as an assignment inside a code block, and so do we:
    /// Layout = "_Layout.vbhtml".
    /// </summary>
    public static string? LayoutFrom(VbHtmlDocument viewStart)
    {
        // The directive, @Layout "_Layout", which the VBH008 message itself
        // suggests for a _ViewStart.
        if (!string.IsNullOrWhiteSpace(viewStart.Layout)) return viewStart.Layout;

        foreach (var node in viewStart.Nodes)
        {
            if (node is not StatementNode statement) continue;

            foreach (var line in statement.Code.Split('\n'))
            {
                var trimmed = line.Trim();

                if (!trimmed.StartsWith("Layout", StringComparison.OrdinalIgnoreCase)) continue;

                var equals = trimmed.IndexOf('=');

                if (equals < 0) continue;

                var value = trimmed.Substring(equals + 1).Trim().Trim('"');

                if (value.Length > 0) return value;
            }
        }

        return null;
    }
}
