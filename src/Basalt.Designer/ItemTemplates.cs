using System.Text.RegularExpressions;

namespace Basalt.Designer;

/// <summary>
/// The item templates Solution Explorer's Add New Item offers, the Visual
/// Basic ones Visual Studio offers in a VB project.
/// </summary>
public enum ItemTemplate
{
    Class,
    Module,
    Interface,
    WindowsForm,
    UserControl,
    EmptyFile
}

/// <summary>A file an item template writes: its name within the folder, and its text.</summary>
public sealed record ItemFile(string FileName, string Content);

/// <summary>
/// The text of each item template.
///
/// The files declare no Namespace: a new project's RootNamespace is empty and
/// the compiler places every type of a file at the root of the project, which
/// is what the other templates do too. Declaring one here would double the
/// project name in the type's full name.
///
/// The Windows Form and UserControl pairs are written the way the WinForms
/// project template writes its MainForm pair, so the designer reads them the
/// same way.
/// </summary>
public static class ItemTemplates
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether the template needs System.Windows.Forms. Only a Windows Forms
    /// project can build such an item, so the Add New Item dialog offers these
    /// two only there.
    /// </summary>
    public static bool NeedsWindowsForms(ItemTemplate template) =>
        template is ItemTemplate.WindowsForm or ItemTemplate.UserControl;

    /// <summary>The name the dialog suggests for a template, as Visual Studio suggests Class1.vb.</summary>
    public static string DefaultName(ItemTemplate template) => template switch
    {
        ItemTemplate.Class => "Class1.vb",
        ItemTemplate.Module => "Module1.vb",
        ItemTemplate.Interface => "Interface1.vb",
        ItemTemplate.WindowsForm => "Form1.vb",
        ItemTemplate.UserControl => "UserControl1.vb",
        _ => "NewFile.vb"
    };

    /// <summary>
    /// The file name a typed name stands for, or null when the typed name
    /// cannot be one.
    ///
    /// The code templates take a Visual Basic type name, so the ".vb" the user
    /// may have typed is taken off and put back, and the rest must be an
    /// identifier that is not a reserved word. An escaped name would have to
    /// be written [End] in every file, which the templates do not do, so a
    /// reserved word is refused instead.
    ///
    /// <paramref name="isReservedWord"/> is the compiler's answer, supplied by
    /// the caller: this assembly does not reference the Visual Basic compiler.
    /// An empty file keeps the name as typed.
    /// </summary>
    public static string? FileNameFor(ItemTemplate template, string typed, Func<string, bool> isReservedWord)
    {
        var name = typed.Trim();

        if (template == ItemTemplate.EmptyFile)
            return name.Length > 0 ? name : null;

        if (name.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            name = name[..^3];

        if (!Identifier.IsMatch(name) || isReservedWord(name)) return null;

        return name + ".vb";
    }

    /// <summary>
    /// The files a template writes for a file name from <see cref="FileNameFor"/>.
    /// The first file is the one the editor opens; a Windows Form or
    /// UserControl adds its Designer.vb companion after it.
    /// </summary>
    public static IReadOnlyList<ItemFile> FilesFor(ItemTemplate template, string fileName)
    {
        if (template == ItemTemplate.EmptyFile) return [new ItemFile(fileName, "")];

        var typeName = Path.GetFileNameWithoutExtension(fileName);

        return template switch
        {
            ItemTemplate.Class => [new ItemFile(fileName, ClassText(typeName))],
            ItemTemplate.Module => [new ItemFile(fileName, ModuleText(typeName))],
            ItemTemplate.Interface => [new ItemFile(fileName, InterfaceText(typeName))],
            ItemTemplate.WindowsForm => WinFormsPair(typeName, "Form", "800, 500", clientSize: true),
            ItemTemplate.UserControl => WinFormsPair(typeName, "UserControl", "150, 150", clientSize: false),
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, null)
        };
    }

    private static string ClassText(string name) => $"""
        Public Class {name}
        End Class

        """;

    private static string ModuleText(string name) => $"""
        Module {name}
        End Module

        """;

    private static string InterfaceText(string name) => $"""
        Public Interface {name}
        End Interface

        """;

    private static IReadOnlyList<ItemFile> WinFormsPair(string name, string baseClass, string size, bool clientSize)
    {
        // The base class is the WinForms one, named in full so that a
        // Form or UserControl of the project's own cannot shadow it.
        var classText = $"""
            Public Partial Class {name}
                Public Sub New()
                    InitializeComponent()
                End Sub
            End Class

            """;

        // Form sizes its client area and carries a title; a UserControl takes
        // the size of its own bounds. The rest is the WinForms project's
        // MainForm designer, with the base class and these lines swapped.
        // Each inserted line carries its own indentation: the raw string below
        // only indents the text written in it.
        var layout = new List<string>
        {
            clientSize
                ? $"Me.ClientSize = New Global.System.Drawing.Size({size})"
                : $"Me.Size = New Global.System.Drawing.Size({size})",
            $"Me.Name = \"{name}\""
        };
        if (clientSize) layout.Add($"Me.Text = \"{name}\"");
        var layoutText = string.Join("\n        ", layout);

        var designerText = $$"""
            <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated>
            Public Partial Class {{name}}
                Inherits Global.System.Windows.Forms.{{baseClass}}

                Private components As Global.System.ComponentModel.IContainer

                Protected Overrides Sub Dispose(disposing As Boolean)
                    Try
                        If disposing AndAlso components IsNot Nothing Then
                            components.Dispose()
                        End If
                    Finally
                        MyBase.Dispose(disposing)
                    End Try
                End Sub

                <Global.System.Diagnostics.DebuggerStepThrough>
                Private Sub InitializeComponent()
                    Me.components = New Global.System.ComponentModel.Container()
                    Me.SuspendLayout()
                    Me.AutoScaleDimensions = New Global.System.Drawing.SizeF(96.0F, 96.0F)
                    Me.AutoScaleMode = Global.System.Windows.Forms.AutoScaleMode.Dpi
                    {{layoutText}}
                    Me.ResumeLayout(False)
                End Sub
            End Class

            """;

        return
        [
            new ItemFile(name + ".vb", classText),
            new ItemFile(name + ".Designer.vb", designerText)
        ];
    }
}
