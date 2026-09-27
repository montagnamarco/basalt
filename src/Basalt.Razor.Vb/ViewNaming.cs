using System;
using System.Collections.Generic;
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

            var isPages = string.Equals(name, "Pages", StringComparison.OrdinalIgnoreCase);
            var isViews = string.Equals(name, "Views", StringComparison.OrdinalIgnoreCase);

            if (!isPages && !isViews) continue;

            var root = isPages ? "Pages" : "Views";

            // Inside an MVC area — Areas/Admin/Views — the area is part of the
            // root. Without it Areas/Admin/Views/Home/Index and
            // Views/Home/Index were the same class, and the build failed on
            // the duplicate as soon as a site had both.
            var area = Path.GetDirectoryName(current);
            var areas = string.IsNullOrEmpty(area) ? null : Path.GetDirectoryName(area);

            if (areas is { Length: > 0 } &&
                string.Equals(Path.GetFileName(areas), "Areas", StringComparison.OrdinalIgnoreCase))
                return $"Areas.{Escape(MakeClassName(Path.GetFileName(area)!))}.{root}";

            return root;
        }

        // Not in a recognised layout: Views, which is what a loose template
        // in a project without either folder has always been given.
        return "Views";
    }

    /// <summary>
    /// A component's namespace from the folders between the project and the
    /// file, as the Razor compiler gives a .razor file:
    /// Components/Layout/MainLayout.vbrazor is in Components.Layout.
    /// </summary>
    /// <returns>
    /// Empty for a file beside the project file, which is then in the root
    /// namespace alone; null without a project folder, or for a file outside it.
    /// </returns>
    /// <remarks>
    /// Visual Basic prepends RootNamespace to every Namespace statement, so
    /// only the folders are returned.
    /// </remarks>
    public static string? ProjectFolderNamespaceFor(string path, string? projectDirectory)
    {
        if (projectDirectory is not { Length: > 0 }) return null;

        var folder = projectDirectory.TrimEnd('/', '\\');

        // Followed by a separator: C:\Site is not the folder of C:\Site.Shared\X.vbrazor.
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return null;
        if (path.Length <= folder.Length || path[folder.Length] is not ('/' or '\\')) return null;

        var segments = path.Substring(folder.Length + 1).Split('/', '\\');
        var folders = new List<string>();

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Length > 0) folders.Add(Escape(MakeClassName(segments[index])));
        }

        return string.Join(".", folders);
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

    /// <summary>Whether a name is a Visual Basic keyword.</summary>
    private static bool IsKeyword(string name) => Keywords.Contains(name);

    /// <summary>
    /// Visual Basic's reserved words, all of them: now that components are
    /// namespaced by folder, a folder called Protected or Shared becomes part
    /// of a Namespace statement, and a partial list let it through unescaped.
    /// Escaping a word that did not need it is harmless; missing one is not.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddHandler", "AddressOf", "Alias", "And", "AndAlso", "As", "Boolean", "ByRef", "Byte",
        "ByVal", "Call", "Case", "Catch", "CBool", "CByte", "CChar", "CDate", "CDbl", "CDec",
        "Char", "CInt", "Class", "CLng", "CObj", "Const", "Continue", "CSByte", "CShort",
        "CSng", "CStr", "CType", "CUInt", "CULng", "CUShort", "Date", "Decimal", "Declare",
        "Default", "Delegate", "Dim", "DirectCast", "Do", "Double", "Each", "Else", "ElseIf",
        "End", "EndIf", "Enum", "Erase", "Error", "Event", "Exit", "False", "Finally", "For",
        "Friend", "Function", "Get", "GetType", "GetXMLNamespace", "Global", "GoSub", "GoTo",
        "Handles", "If", "Implements", "Imports", "In", "Inherits", "Integer", "Interface",
        "Is", "IsNot", "Let", "Lib", "Like", "Long", "Loop", "Me", "Mod", "Module",
        "MustInherit", "MustOverride", "My", "MyBase", "MyClass", "Namespace", "Narrowing",
        "New", "Next", "Not", "Nothing", "NotInheritable", "NotOverridable", "Object", "Of",
        "On", "Operator", "Option", "Optional", "Or", "OrElse", "Overloads", "Overridable",
        "Overrides", "ParamArray", "Partial", "Private", "Property", "Protected", "Public",
        "RaiseEvent", "ReadOnly", "ReDim", "REM", "RemoveHandler", "Resume", "Return", "SByte",
        "Select", "Set", "Shadows", "Shared", "Short", "Single", "Static", "Step", "Stop",
        "String", "Structure", "Sub", "SyncLock", "Then", "Throw", "To", "True", "Try",
        "TryCast", "TypeOf", "UInteger", "ULong", "UShort", "Using", "Variant", "Wend", "When",
        "While", "Widening", "With", "WithEvents", "WriteOnly", "Xor",
    };
}
