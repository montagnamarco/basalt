using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Basalt.Workspace.Projects;

/// <summary>
/// The Visual Basic Application Framework of a Windows Forms project.
/// </summary>
/// <remarks>
/// In use when the project's <c>MyType</c> is <c>WindowsForms</c> and its
/// <c>My Project/Application.myapp</c> sets <c>MySubMain</c> to true. Then the
/// program starts through the framework's own <c>My.MyApplication</c>, which
/// opens the form that <c>MainForm</c> names in the same file. Visual Studio
/// keeps <c>StartupObject</c> for that class and changes the form only: the
/// file, and the generated <c>Application.Designer.vb</c> whose
/// <c>OnCreateMainForm</c> assigns <c>Me.MainForm = Global.&lt;Root&gt;.&lt;Form&gt;</c>.
/// </remarks>
public sealed partial class ApplicationFramework
{
    private readonly XDocument? _document;

    private ApplicationFramework(string path, XDocument? document)
    {
        Path = path;
        _document = document;
    }

    /// <summary>The file of the framework: My Project/Application.myapp beside the project.</summary>
    public string Path { get; }

    /// <summary>The designer file Visual Studio generates for it, which assigns the form in code.</summary>
    public string DesignerPath => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Path)!, "Application.Designer.vb");

    /// <summary>Whether the program starts through the framework.</summary>
    public bool InUse => _document is not null;

    /// <summary>The form the framework opens, as the file names it (without the root namespace), or null.</summary>
    public string? MainForm => _document?.Root?.Element("MainForm")?.Value.Trim() is { Length: > 0 } form ? form : null;

    /// <summary>Reads the framework of a project, which is not in use unless the project and its file say so.</summary>
    public static ApplicationFramework For(string projectPath)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath))!, "My Project", "Application.myapp");

        if (!string.Equals(VbProjectFile.Load(projectPath).GetProperty("MyType"), "WindowsForms", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
            return new ApplicationFramework(path, null);

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var subMain = document.Root?.Element("MySubMain")?.Value.Trim();

        return string.Equals(subMain, "true", StringComparison.OrdinalIgnoreCase)
            ? new ApplicationFramework(path, document)
            : new ApplicationFramework(path, null);
    }

    /// <summary>
    /// Writes the form the framework opens to Application.myapp and to the designer file, in place, or removes it from
    /// the file for null. The designer's assignment is changed only when the form changes; the rest of either file is
    /// kept. When the designer cannot be written, Application.myapp is put back as it was.
    /// </summary>
    /// <param name="form">The form as the file names it, without the root namespace; null removes it.</param>
    /// <param name="rootNamespace">The root namespace the designer qualifies the form with.</param>
    public void SaveMainForm(string? form, string rootNamespace)
    {
        if (!InUse) return;

        var originalMyApp = File.ReadAllBytes(Path);
        var myAppChanged = form != MainForm;

        if (myAppChanged && _document?.Root is { } root)
        {
            var element = root.Element("MainForm");
            if (form is null) element?.Remove();
            else if (element is not null) element.Value = form;
            else root.Add(new XElement("MainForm", form));

            _document.Save(Path);
        }

        try
        {
            SaveDesigner(form, rootNamespace);
        }
        catch
        {
            if (myAppChanged) File.WriteAllBytes(Path, originalMyApp);
            throw;
        }
    }

    /// <summary>Changes the form the designer assigns, and leaves every other line of the file as it is.</summary>
    private void SaveDesigner(string? form, string rootNamespace)
    {
        if (form is null || !File.Exists(DesignerPath)) return;

        var bytes = File.ReadAllBytes(DesignerPath);
        var hasByteOrderMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');

        var qualified = rootNamespace.Length == 0 ? form : rootNamespace + "." + form;
        var updated = MainFormAssignment().Replace(text, match => match.Groups[1].Value + "Global." + qualified, 1);
        if (updated == text) return;

        File.WriteAllText(DesignerPath, (hasByteOrderMark ? "﻿" : "") + updated, new UTF8Encoding(false));
    }

    /// <summary>The assignment of the form in the designer's OnCreateMainForm: its indentation kept, and the name after Global.</summary>
    [GeneratedRegex(@"^([ \t]*Me\.MainForm[ \t]*=[ \t]*)Global\.[^\r\n]*", RegexOptions.Multiline)]
    private static partial Regex MainFormAssignment();
}
