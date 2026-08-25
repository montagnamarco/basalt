using System.Text;

namespace Basalt.Razor.Vb;

/// <summary>
/// What a template's path says about the class the generator writes.
///
/// Here rather than inside the generator so it can be tested: referencing
/// the generator assembly from the test project hangs the test host, since
/// the generator carries its own copy of these sources and the duplicate
/// types break discovery.
/// </summary>
public static class ViewNaming
{
    /// <summary>
    /// Namespace suffix taken from the folders below the views root.
    ///
    /// MVC gives each controller its own view folder, and two controllers
    /// commonly both have an Index view. Mirroring the folder structure keeps
    /// those apart, as Razor's own generated types are kept apart.
    /// </summary>
    public static string FolderNamespaceFor(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return "";

        var segments = new List<string>();

        for (var current = directory; !string.IsNullOrEmpty(current);
             current = Path.GetDirectoryName(current))
        {
            var name = Path.GetFileName(current);
            if (string.IsNullOrEmpty(name)) break;

            // Stop at the folder the views live under; what lies above it
            // belongs to the project, not to the view's namespace.
            if (string.Equals(name, "Views", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Pages", StringComparison.OrdinalIgnoreCase))
                return segments.Count == 0 ? "" : string.Join(".", segments);

            segments.Insert(0, MakeClassName(name!));
        }

        // No views root above it: the file is not in a recognised layout, so
        // the flat namespace is used rather than inventing one from the path.
        return "";
    }

    /// <summary>
    /// The folder a template's namespace is rooted at: Views or Pages.
    /// </summary>
    /// <remarks>
    /// Razor Pages live under Pages and MVC views under Views, and a page's
    /// namespace follows its own root the way the C# generator's does. Rooting
    /// everything at Views put pages in the wrong namespace, and the attribute
    /// that registers them named a type that was not there.
    /// </remarks>
    public static string RootFolderFor(string path)
    {
        for (var current = Path.GetDirectoryName(path); !string.IsNullOrEmpty(current);
             current = Path.GetDirectoryName(current))
        {
            var name = Path.GetFileName(current);
            if (string.IsNullOrEmpty(name)) break;

            if (string.Equals(name, "Pages", StringComparison.OrdinalIgnoreCase)) return "Pages";
            if (string.Equals(name, "Views", StringComparison.OrdinalIgnoreCase)) return "Views";
        }

        // Not in a recognised layout: Views, which is what a loose template
        // in a project without either folder has always been given.
        return "Views";
    }

    public static string MakeClassName(string fileName)
    {
        var builder = new StringBuilder();

        foreach (var c in fileName)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        var name = builder.ToString();

        // An identifier cannot start with a digit.
        if (name.Length == 0 || char.IsDigit(name[0])) name = "_" + name;

        return name;
    }

    /// <summary>
    /// A class name as it must be written in Visual Basic source.
    /// </summary>
    /// <remarks>
    /// Error.vbhtml is in every MVC project ever scaffolded, and Error is a
    /// Visual Basic keyword: the generated class would not compile, and the
    /// message pointed at generated code the user never wrote. Brackets are
    /// how Visual Basic escapes a keyword used as a name.
    /// </remarks>
    public static string Escape(string className) =>
        IsKeyword(className) ? $"[{className}]" : className;

    /// <summary>
    /// Whether a name is a Visual Basic keyword.
    /// </summary>
    /// <remarks>
    /// Only the ones a view file is plausibly named after. A full keyword list
    /// would be longer and no more correct: a template called Shadows is not
    /// a case worth carrying a table for.
    /// </remarks>
    private static bool IsKeyword(string name) => name.ToLowerInvariant() switch
    {
        "error" or "new" or "class" or "module" or "structure" or "interface" or
        "enum" or "delegate" or "event" or "property" or "sub" or "function" or
        "shared" or "default" or "option" or "select" or "stop" or "step" or
        "end" or "next" or "loop" or "then" or "else" or "each" or "in" or
        "is" or "not" or "and" or "or" or "xor" or "true" or "false" or
        "nothing" or "me" or "my" or "global" or "single" or "double" or
        "date" or "string" or "object" or "boolean" or "byte" or "char" or
        "decimal" or "integer" or "long" or "short" or "handles" or "imports"
            => true,
        _ => false,
    };
}
