using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.VisualBasic;

namespace Basalt.Workspace;

/// <summary>One image or icon listed by "Project resource file" in the Select Resource dialog.</summary>
public sealed record WinFormsProjectResourceEntry(string Name, bool IsIcon, byte[] Bytes)
{
    // The dialog's ListBox shows entries by their default rendering; a record's
    // generated ToString would otherwise dump the whole byte array into the UI.
    public override string ToString() => Name;
}

/// <summary>
/// The project model side of Visual Studio's "Project resource file" choice: the project a
/// WinForms designer form belongs to, its <c>My Project\Resources.resx</c> and the
/// <c>Resources.Designer.vb</c> module generated from it.
///
/// Kept apart from <see cref="Basalt.Shell.Controls.WinFormsImageResourceDialog"/> (which has
/// no notion of a project) and from Avalonia, so every rule here is testable as plain file IO.
/// </summary>
public static class WinFormsProjectResources
{
    /// <summary>The resx "type" Visual Studio writes for a file-backed (as opposed to embedded) resource.</summary>
    public const string ResXFileRefTypeName = "System.Resources.ResXFileRef, System.Windows.Forms";

    // The real System.Drawing assembly identity Visual Studio writes into a ResXFileRef value,
    // regardless of what assembly actually resolves System.Drawing.Bitmap/Icon at run time.
    private const string BitmapAssemblyQualifiedName =
        "System.Drawing.Bitmap, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
    private const string IconAssemblyQualifiedName =
        "System.Drawing.Icon, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";

    /// <summary>
    /// The .vbproj a WinForms designer form belongs to, found the same way
    /// <see cref="ExplorerFileOperations.ProjectContaining"/> does for any other file, but
    /// narrowed to .vbproj: a form with no .vbproj above it (a .csproj, or no project at all)
    /// has no My Project folder and no Project resource file to offer.
    /// </summary>
    public static string? FindProject(string formPath)
    {
        var project = ExplorerFileOperations.ProjectContaining(formPath);
        return project is not null && project.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ? project : null;
    }

    /// <summary>
    /// The project's &lt;RootNamespace&gt;, or its file name when the element is absent (the
    /// MSBuild default). An explicit but empty element (&lt;RootNamespace&gt;&lt;/RootNamespace&gt;)
    /// is a deliberate "no root namespace" in Visual Basic, not the same as the element being
    /// missing altogether: it is returned as "", the same distinction
    /// <see cref="Basalt.Workspace.Projects.VbProjectFile.SetPropertyAllowingEmpty"/> preserves
    /// when writing it back.
    /// </summary>
    public static string RootNamespace(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var element = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "RootNamespace");
        return element is null ? Path.GetFileNameWithoutExtension(projectPath) : element.Value.Trim();
    }

    public static string MyProjectDirectory(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath) ?? "", "My Project");

    public static string ResourcesResxPath(string projectPath) =>
        Path.Combine(MyProjectDirectory(projectPath), "Resources.resx");

    public static string ResourcesDesignerVbPath(string projectPath) =>
        Path.Combine(MyProjectDirectory(projectPath), "Resources.Designer.vb");

    /// <summary>Where Import copies the picked file: a "Resources" folder next to the project, as Visual Studio does.</summary>
    public static string ImportedFilesDirectory(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath) ?? "", "Resources");

    /// <summary>
    /// Every Image or Icon entry of the project's Resources.resx, resolved to its bytes:
    /// a ResXFileRef entry is read from the file it points at (relative to My Project), an
    /// embedded "bytearray" entry is decoded in place, exactly as
    /// <see cref="WinFormsImageResources"/> already does for a form's own resx.
    /// Empty when the project has no Resources.resx yet.
    /// </summary>
    public static IReadOnlyList<WinFormsProjectResourceEntry> ListEntries(string projectPath)
    {
        var resxPath = ResourcesResxPath(projectPath);
        if (!File.Exists(resxPath)) return [];

        var document = new WinFormsResourceDocument(File.ReadAllText(resxPath));
        var resxDirectory = Path.GetDirectoryName(resxPath)!;
        var entries = new List<WinFormsProjectResourceEntry>();

        foreach (var entry in document.Entries)
        {
            if (entry.TypeName is { } typeName && typeName.StartsWith("System.Resources.ResXFileRef", StringComparison.Ordinal))
            {
                var parts = entry.Value.Split(';');
                if (parts.Length < 2) continue;
                var declaredType = parts[1].Trim();
                if (!WinFormsImageResources.IsImageTypeName(declaredType)) continue;

                var relative = parts[0].Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var absolute = Path.GetFullPath(Path.Combine(resxDirectory, relative));
                if (!File.Exists(absolute)) continue;

                entries.Add(new WinFormsProjectResourceEntry(entry.Name, IsIconTypeName(declaredType), File.ReadAllBytes(absolute)));
            }
            else if (WinFormsImageResources.TryDecode(entry, out var bytes))
            {
                entries.Add(new WinFormsProjectResourceEntry(entry.Name, IsIconTypeName(entry.TypeName), bytes));
            }
        }

        return entries;
    }

    private static bool IsIconTypeName(string? typeName)
    {
        if (typeName is null) return false;
        var separator = typeName.IndexOf(',');
        var declared = (separator < 0 ? typeName : typeName[..separator]).Trim();
        return declared == "System.Drawing.Icon";
    }

    /// <summary>
    /// Imports a file into the project the way Visual Studio's "Project resource file" Import…
    /// does: copies it into a "Resources" folder next to the project under a unique name, adds a
    /// ResXFileRef entry to Resources.resx (creating the file, Resources.Designer.vb and the
    /// project items that wire them together the first time), and adds the strongly-typed
    /// property to Resources.Designer.vb.
    ///
    /// Every text edit is computed first, from the files already on disk; only once all three
    /// (Resources.resx, Resources.Designer.vb and the .vbproj) have been rewritten is the file
    /// itself copied in, last. A Resources.resx or Resources.Designer.vb that does not match the
    /// shape Visual Studio writes (hand-edited into something this method cannot find its way
    /// around) throws a <see cref="FormatException"/> before anything is written, rather than
    /// leaving a copied file with no resx entry pointing at it, or a half-written Designer.vb.
    ///
    /// Undo note: the caller (<see cref="Basalt.Shell.Controls.WinFormsDesignerSession"/>) covers
    /// only the Designer.vb property assignment in its undo stack, the same as every other edit
    /// there. Resources.resx, Resources.Designer.vb and the copied file are not undone, matching
    /// Visual Studio: importing a project resource is not something "Edit > Undo" ever reverts
    /// there either, only the statement that used it.
    /// </summary>
    public static WinFormsProjectResourceEntry Import(string projectPath, string sourceFilePath, bool isIcon)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentException.ThrowIfNullOrEmpty(sourceFilePath);

        var myProjectDirectory = MyProjectDirectory(projectPath);
        var resourcesDirectory = ImportedFilesDirectory(projectPath);

        var resxPath = ResourcesResxPath(projectPath);
        var resxText = File.Exists(resxPath) ? File.ReadAllText(resxPath) : WinFormsResourceDocumentTemplate.Empty;
        var reservedNames = new[] { "ResourceManager", "Culture" };
        var existingNames = new WinFormsResourceDocument(resxText).Entries.Select(e => e.Name).Concat(reservedNames);

        var extension = Path.GetExtension(sourceFilePath);
        var name = UniqueName(SanitizeIdentifier(Path.GetFileNameWithoutExtension(sourceFilePath)), existingNames);

        var targetFilePath = Path.Combine(resourcesDirectory, name + extension);
        var suffix = 1;
        while (File.Exists(targetFilePath))
        {
            targetFilePath = Path.Combine(resourcesDirectory, $"{name}{++suffix}{extension}");
        }
        if (suffix > 1) name = $"{name}{suffix}";

        // Every edit is a pure text transform computed up front: a malformed Resources.resx or
        // Resources.Designer.vb throws here, before Directory.CreateDirectory or any File.Write
        // has run, so a rejected import leaves the project exactly as it was.
        var relativeValue = Path.GetRelativePath(myProjectDirectory, targetFilePath).Replace('/', '\\');
        var assemblyQualifiedType = isIcon ? IconAssemblyQualifiedName : BitmapAssemblyQualifiedName;
        var updatedResxText = AppendResxEntry(resxText, name, relativeValue, assemblyQualifiedType);

        var designerPath = ResourcesDesignerVbPath(projectPath);
        var designerText = File.Exists(designerPath) ? File.ReadAllText(designerPath) : BuildDesignerSkeleton(RootNamespace(projectPath));
        var updatedDesignerText = AppendDesignerProperty(designerText, name, isIcon);

        var projectText = File.ReadAllText(projectPath);
        var updatedProjectText = AddProjectItemsIfMissing(projectText);

        Directory.CreateDirectory(myProjectDirectory);
        Directory.CreateDirectory(resourcesDirectory);
        File.WriteAllText(resxPath, updatedResxText);
        File.WriteAllText(designerPath, updatedDesignerText);
        if (updatedProjectText != projectText) File.WriteAllText(projectPath, updatedProjectText);

        // Copied last: every edit above already succeeded, so this is the only step left
        // that can still fail (a locked or read-only source), and it leaves nothing behind
        // to clean up when it does.
        File.Copy(sourceFilePath, targetFilePath);

        return new WinFormsProjectResourceEntry(name, isIcon, File.ReadAllBytes(targetFilePath));
    }

    /// <summary>
    /// Strips characters a VB identifier (and so a resx name read back as a property) cannot
    /// contain, then escapes the result the way Visual Studio's StronglyTypedResourceBuilder
    /// does when the sanitized name is still unusable as a member: a VB reserved word (checked
    /// through the same compiler <see cref="SyntaxFacts"/> the language server's rename command
    /// uses, rather than a second, hand-kept list that could drift out of sync with it), or one
    /// of the two members every Resources module already declares (ResourceManager, Culture) that
    /// are not themselves resx entries, so <see cref="UniqueName"/> never sees them.
    /// </summary>
    private static string SanitizeIdentifier(string name)
    {
        var characters = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var sanitized = new string(characters);
        if (sanitized.Length == 0) sanitized = "Resource";
        if (!(char.IsLetter(sanitized[0]) || sanitized[0] == '_')) sanitized = "_" + sanitized;
        return IsReservedIdentifier(sanitized) ? "_" + sanitized : sanitized;
    }

    private static bool IsReservedIdentifier(string name) =>
        SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(name)) ||
        string.Equals(name, "ResourceManager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Culture", StringComparison.OrdinalIgnoreCase);

    /// <summary>The given name, or the same name with an incrementing number appended until no existing entry matches.</summary>
    private static string UniqueName(string baseName, IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;

        var suffix = 1;
        string candidate;
        do
        {
            candidate = baseName + ++suffix;
        } while (taken.Contains(candidate));
        return candidate;
    }

    /// <summary>
    /// The resx closing root tag, matched regardless of the whitespace a hand-formatted file
    /// might have inside it (<c>&lt;/root&gt;</c>, <c>&lt;/root &gt;</c>): real resx files never
    /// vary this, but a literal search still has no reason to reject one that does.
    /// <see cref="RegexOptions.RightToLeft"/> finds the last match (there is exactly one
    /// top-level closing tag, but this mirrors the old <c>LastIndexOf</c> search) in one pass.
    /// </summary>
    private static readonly Regex ResxRootClosingTag = new(@"</\s*root\s*>", RegexOptions.RightToLeft | RegexOptions.IgnoreCase);

    /// <summary>
    /// Inserts a compact, single-line <c>&lt;data&gt;</c> element before the closing
    /// <c>&lt;/root&gt;</c>, matching the shape Visual Studio writes for a ResXFileRef entry: no
    /// <c>xml:space</c>, no surrounding whitespace in the value.
    /// </summary>
    private static string AppendResxEntry(string resxText, string name, string relativeValue, string assemblyQualifiedType)
    {
        var closingMatch = ResxRootClosingTag.Match(resxText);
        if (!closingMatch.Success)
        {
            throw new FormatException("The resource document has no closing root element.");
        }

        var newLine = resxText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var escapedValue = System.Security.SecurityElement.Escape($"{relativeValue};{assemblyQualifiedType}");
        var data = $"  <data name=\"{name}\" type=\"{ResXFileRefTypeName}\"><value>{escapedValue}</value></data>";
        return resxText[..closingMatch.Index] + data + newLine + resxText[closingMatch.Index..];
    }

    /// <summary>
    /// The line Visual Studio closes <c>Friend Module Resources</c> with: the indentation is
    /// captured, not assumed, so a file reformatted with tabs (or any other width) is still
    /// found. <see cref="RegexOptions.RightToLeft"/> picks the last one, which is the module's
    /// own closing line rather than, say, a nested block comment that happens to contain the
    /// same words.
    /// </summary>
    private static readonly Regex DesignerEndModuleLine =
        new(@"^([ \t]*)End[ \t]+Module[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

    /// <summary>
    /// The indentation of a member Visual Studio always generates before any image is ever
    /// imported, used as the template for a new property's own indentation: a file reformatted
    /// to tabs, or to a different number of spaces, still gets a property that indents the same
    /// way as its neighbours instead of the hard-coded four spaces this used to assume.
    /// </summary>
    private static readonly Regex DesignerMemberIndentHint =
        new(@"^([ \t]*)Private[ \t]+resourceCulture\b", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Inserts the strongly-typed property Visual Studio's VbMyResourcesResXFileCodeGenerator
    /// would add for a new image entry, right before "End Module".
    /// </summary>
    private static string AppendDesignerProperty(string designerText, string name, bool isIcon)
    {
        var moduleClosing = DesignerEndModuleLine.Match(designerText);
        if (!moduleClosing.Success)
        {
            throw new FormatException("Resources.Designer.vb has no closing \"End Module\".");
        }

        var indentHint = DesignerMemberIndentHint.Match(designerText);
        var indent = indentHint.Success ? indentHint.Groups[1].Value : "        ";
        var unit = indent.Contains('\t') ? "\t" : "    ";

        var newLine = designerText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var typeName = isIcon ? "System.Drawing.Icon" : "System.Drawing.Bitmap";
        var lines = new[]
        {
            "",
            $"{indent}'''<summary>",
            $"{indent}'''  Looks up a localized resource of type {typeName}.",
            $"{indent}'''</summary>",
            $"{indent}Friend ReadOnly Property {name}() As {typeName}",
            $"{indent}{unit}Get",
            $"{indent}{unit}{unit}Return CType(ResourceManager.GetObject(\"{name}\", resourceCulture), {typeName})",
            $"{indent}{unit}End Get",
            $"{indent}End Property",
            "",
        };
        var block = string.Join(newLine, lines);
        return designerText[..moduleClosing.Index] + block + designerText[moduleClosing.Index..];
    }

    /// <summary>A brand-new Resources.Designer.vb: the VB ResXFileCodeGenerator shape, no image properties yet.</summary>
    private static string BuildDesignerSkeleton(string rootNamespace)
    {
        var lines = new[]
        {
            "'------------------------------------------------------------------------------",
            "' <auto-generated>",
            "'     This code was generated by a tool.",
            "'     Runtime Version:4.0.30319.1",
            "'",
            "'     Changes to this file may cause incorrect behavior and will be lost if",
            "'     the code is regenerated.",
            "' </auto-generated>",
            "'------------------------------------------------------------------------------",
            "",
            "Option Strict On",
            "Option Explicit On",
            "",
            "Imports System",
            "",
            "Namespace My.Resources",
            "",
            "    'This class was auto-generated by the StronglyTypedResourceBuilder",
            "    'class via a tool like ResGen or Visual Studio.",
            "    'To add or remove a member, edit your .ResX file then rerun ResGen",
            "    'with the /str option, or rebuild your VS project.",
            "    '''<summary>",
            "    '''  A strongly-typed resource class, for looking up localized strings, etc.",
            "    '''</summary>",
            "    <Global.System.CodeDom.Compiler.GeneratedCodeAttribute(\"System.Resources.Tools.StronglyTypedResourceBuilder\", \"4.0.0.0\"),  _",
            "     Global.System.Diagnostics.DebuggerNonUserCodeAttribute(),  _",
            "     Global.System.Runtime.CompilerServices.CompilerGeneratedAttribute(),  _",
            "     Global.Microsoft.VisualBasic.HideModuleNameAttribute()>  _",
            "    Friend Module Resources",
            "",
            "        Private resourceMan As Global.System.Resources.ResourceManager",
            "",
            "        Private resourceCulture As Global.System.Globalization.CultureInfo",
            "",
            "        '''<summary>",
            "        '''  Returns the cached ResourceManager instance used by this class.",
            "        '''</summary>",
            "        <Global.System.ComponentModel.EditorBrowsableAttribute(Global.System.ComponentModel.EditorBrowsableState.Advanced)>  _",
            "        Friend ReadOnly Property ResourceManager() As Global.System.Resources.ResourceManager",
            "            Get",
            "                If Object.ReferenceEquals(resourceMan, Nothing) Then",
            $"                    Dim temp As Global.System.Resources.ResourceManager = New Global.System.Resources.ResourceManager(\"{rootNamespace}.Resources\", GetType(Resources).Assembly)",
            "                    resourceMan = temp",
            "                End If",
            "                Return resourceMan",
            "            End Get",
            "        End Property",
            "",
            "        '''<summary>",
            "        '''  Overrides the current thread's CurrentUICulture property for all",
            "        '''  resource lookups using this strongly typed resource class.",
            "        '''</summary>",
            "        <Global.System.ComponentModel.EditorBrowsableAttribute(Global.System.ComponentModel.EditorBrowsableState.Advanced)>  _",
            "        Friend Property Culture() As Global.System.Globalization.CultureInfo",
            "            Get",
            "                Return resourceCulture",
            "            End Get",
            "            Set",
            "                resourceCulture = value",
            "            End Set",
            "        End Property",
            "    End Module",
            "End Namespace",
            "",
        };
        return string.Join("\n", lines);
    }

    /// <summary>Matched the same tolerant way as <see cref="ResxRootClosingTag"/>, for the same reason.</summary>
    private static readonly Regex ProjectClosingTag = new(@"</\s*Project\s*>", RegexOptions.RightToLeft);

    /// <summary>
    /// The EmbeddedResource/Compile item pairs Visual Studio writes for My Project's
    /// Resources.resx in an SDK-style project, the first time any image is imported; a no-op
    /// once they are there (checked by the generator name, which only this pair ever writes).
    /// A pure text transform, like <see cref="AppendResxEntry"/> and
    /// <see cref="AppendDesignerProperty"/>, so <see cref="Import"/> can compute every edit
    /// before writing any of them.
    /// </summary>
    private static string AddProjectItemsIfMissing(string text)
    {
        if (text.Contains("VbMyResourcesResXFileCodeGenerator", StringComparison.Ordinal)) return text;

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = new[]
        {
            "  <ItemGroup>",
            "    <EmbeddedResource Update=\"My Project\\Resources.resx\">",
            "      <Generator>VbMyResourcesResXFileCodeGenerator</Generator>",
            "      <LastGenOutput>Resources.Designer.vb</LastGenOutput>",
            "      <CustomToolNamespace>My.Resources</CustomToolNamespace>",
            "    </EmbeddedResource>",
            "  </ItemGroup>",
            "  <ItemGroup>",
            "    <Compile Update=\"My Project\\Resources.Designer.vb\">",
            "      <DesignTime>True</DesignTime>",
            "      <AutoGen>True</AutoGen>",
            "      <DependentUpon>Resources.resx</DependentUpon>",
            "    </Compile>",
            "  </ItemGroup>",
            "",
        };
        var block = string.Join(newLine, lines);

        var closingMatch = ProjectClosingTag.Match(text);
        if (!closingMatch.Success)
        {
            throw new FormatException("The project file has no closing </Project> element.");
        }

        return text[..closingMatch.Index] + block + text[closingMatch.Index..];
    }
}
