namespace Basalt.Designer;

/// <summary>
/// The Windows Forms items, written the way the WinForms project template
/// writes its MainForm pair, so the designer reads them the same way.
/// </summary>
internal static class WinFormsTexts
{
    /// <summary>The same base-class, layout and control lines every pair repeats.</summary>
    private const string Indent = "        ";

    public static IReadOnlyList<ItemFile> Form(string name) =>
        Pair(name, "Form", CodeBehind(name), clientSize: true, size: "800, 500", controls: []);

    public static IReadOnlyList<ItemFile> UserControl(string name) =>
        Pair(name, "UserControl", CodeBehind(name), clientSize: false, size: "150, 150", controls: []);

    /// <summary>
    /// A Form with OK and Cancel, as Visual Studio's dialog template has: the
    /// buttons are the form's AcceptButton and CancelButton, so Enter and Esc
    /// answer without a line of code, and the border is the fixed one of a dialog.
    /// </summary>
    public static IReadOnlyList<ItemFile> Dialog(string name)
    {
        var code = $"""
            Public Partial Class {name}
                Public Sub New()
                    InitializeComponent()
                End Sub

                Private Sub OK_Button_Click(sender As Object, e As System.EventArgs) Handles OK_Button.Click
                    Me.DialogResult = Global.System.Windows.Forms.DialogResult.OK
                    Me.Close()
                End Sub

                Private Sub Cancel_Button_Click(sender As Object, e As System.EventArgs) Handles Cancel_Button.Click
                    Me.DialogResult = Global.System.Windows.Forms.DialogResult.Cancel
                    Me.Close()
                End Sub
            End Class

            """;

        var controls = new[]
        {
            new Control("OK_Button", "Button", [
                "DialogResult = Global.System.Windows.Forms.DialogResult.OK",
                "Location = New Global.System.Drawing.Point(212, 100)",
                "Size = New Global.System.Drawing.Size(80, 28)",
                "TabIndex = 0",
                "Text = \"OK\""]),
            new Control("Cancel_Button", "Button", [
                "DialogResult = Global.System.Windows.Forms.DialogResult.Cancel",
                "Location = New Global.System.Drawing.Point(300, 100)",
                "Size = New Global.System.Drawing.Size(80, 28)",
                "TabIndex = 1",
                "Text = \"Cancel\""])
        };

        var formLines = new[]
        {
            "AcceptButton = Me.OK_Button",
            "CancelButton = Me.Cancel_Button",
            "FormBorderStyle = Global.System.Windows.Forms.FormBorderStyle.FixedDialog",
            "MaximizeBox = False",
            "MinimizeBox = False",
            "ShowInTaskbar = False",
            "StartPosition = Global.System.Windows.Forms.FormStartPosition.CenterParent"
        };

        return Pair(name, "Form", code, clientSize: true, size: "400, 140", controls, formLines);
    }

    /// <summary>
    /// An About box: the product, its version and copyright read from the
    /// assembly when the box is made, a description, and an OK button that is
    /// both AcceptButton and CancelButton, since there is nothing to cancel.
    /// </summary>
    public static IReadOnlyList<ItemFile> AboutBox(string name)
    {
        var code = $"""
            Imports System.Reflection

            Public Partial Class {name}
                Public Sub New()
                    InitializeComponent()

                    ' What the assembly says about itself; the designer's text stays
                    ' in place if the assembly says nothing.
                    Dim assembly As Assembly = Assembly.GetExecutingAssembly()
                    Dim assemblyName As AssemblyName = assembly.GetName()
                    Dim copyright As AssemblyCopyrightAttribute = CustomAttributeExtensions.GetCustomAttribute(Of AssemblyCopyrightAttribute)(assembly)

                    Me.Text = "About " & assemblyName.Name
                    Me.LabelProductName.Text = assemblyName.Name

                    If assemblyName.Version IsNot Nothing Then
                        Me.LabelVersion.Text = "Version " & assemblyName.Version.ToString()
                    End If

                    If copyright IsNot Nothing Then
                        Me.LabelCopyright.Text = copyright.Copyright
                    End If
                End Sub

                Private Sub OK_Button_Click(sender As Object, e As System.EventArgs) Handles OK_Button.Click
                    Me.Close()
                End Sub
            End Class

            """;

        var controls = new[]
        {
            new Control("LabelProductName", "Label", [
                "AutoSize = True",
                "Location = New Global.System.Drawing.Point(16, 16)",
                "Size = New Global.System.Drawing.Size(100, 20)",
                "TabIndex = 0",
                "Text = \"Product Name\""]),
            new Control("LabelVersion", "Label", [
                "AutoSize = True",
                "Location = New Global.System.Drawing.Point(16, 44)",
                "Size = New Global.System.Drawing.Size(100, 20)",
                "TabIndex = 1",
                "Text = \"Version\""]),
            new Control("LabelCopyright", "Label", [
                "AutoSize = True",
                "Location = New Global.System.Drawing.Point(16, 72)",
                "Size = New Global.System.Drawing.Size(100, 20)",
                "TabIndex = 2",
                "Text = \"Copyright\""]),
            new Control("TextBoxDescription", "TextBox", [
                "Location = New Global.System.Drawing.Point(16, 104)",
                "Multiline = True",
                "ReadOnly = True",
                "Size = New Global.System.Drawing.Size(368, 72)",
                "TabIndex = 3",
                "Text = \"Describe your application here.\""]),
            new Control("OK_Button", "Button", [
                "DialogResult = Global.System.Windows.Forms.DialogResult.OK",
                "Location = New Global.System.Drawing.Point(304, 188)",
                "Size = New Global.System.Drawing.Size(80, 28)",
                "TabIndex = 4",
                "Text = \"OK\""])
        };

        var formLines = new[]
        {
            "AcceptButton = Me.OK_Button",
            "CancelButton = Me.OK_Button",
            "FormBorderStyle = Global.System.Windows.Forms.FormBorderStyle.FixedDialog",
            "MaximizeBox = False",
            "MinimizeBox = False",
            "ShowInTaskbar = False",
            "StartPosition = Global.System.Windows.Forms.FormStartPosition.CenterParent"
        };

        return Pair(name, "Form", code, clientSize: true, size: "400, 232", controls, formLines);
    }

    private static string CodeBehind(string name) => $"""
        Public Partial Class {name}
            Public Sub New()
                InitializeComponent()
            End Sub
        End Class

        """;

    /// <summary>A control the designer file creates: a name, a WinForms type and the properties it is given.</summary>
    private sealed record Control(string Name, string Type, string[] Properties);

    private static IReadOnlyList<ItemFile> Pair(
        string name, string baseClass, string classText, bool clientSize, string size,
        Control[] controls, string[]? formLines = null)
    {
        // Form sizes its client area and carries a title; a UserControl takes
        // the size of its own bounds. The rest is the WinForms project's
        // MainForm designer, with the base class and these lines swapped.
        // Each inserted line carries its own indentation: the raw string below
        // only indents the text written in it.
        var initialize = new List<string>
        {
            "Me.components = New Global.System.ComponentModel.Container()"
        };
        initialize.AddRange(controls.Select(c =>
            $"Me.{c.Name} = New Global.System.Windows.Forms.{c.Type}()"));
        initialize.Add("Me.SuspendLayout()");
        initialize.Add("Me.AutoScaleDimensions = New Global.System.Drawing.SizeF(96.0F, 96.0F)");
        initialize.Add("Me.AutoScaleMode = Global.System.Windows.Forms.AutoScaleMode.Dpi");

        foreach (var control in controls)
        {
            initialize.Add("'");
            initialize.Add($"'{control.Name}");
            initialize.Add("'");
            initialize.Add($"Me.{control.Name}.Name = \"{control.Name}\"");
            initialize.AddRange(control.Properties.Select(p => $"Me.{control.Name}.{p}"));
        }

        // The form's own section, headed the way the designer heads each
        // control's; a form with nothing but a size has none, as before.
        if (controls.Length > 0 || formLines is { Length: > 0 })
        {
            initialize.Add("'");
            initialize.Add($"'{name}");
            initialize.Add("'");
        }

        foreach (var line in formLines ?? [])
            initialize.Add("Me." + line);

        initialize.AddRange(controls.Select(c => $"Me.Controls.Add(Me.{c.Name})"));

        initialize.Add(clientSize
            ? $"Me.ClientSize = New Global.System.Drawing.Size({size})"
            : $"Me.Size = New Global.System.Drawing.Size({size})");
        initialize.Add($"Me.Name = \"{name}\"");
        if (clientSize) initialize.Add($"Me.Text = \"{name}\"");
        initialize.Add("Me.ResumeLayout(False)");

        var body = string.Join("\n" + Indent, initialize);

        var fields = string.Concat(controls.Select(c =>
            $"\n    Friend WithEvents {c.Name} As Global.System.Windows.Forms.{c.Type}"));

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
                    {{body}}
                End Sub{{fields}}
            End Class

            """;

        return
        [
            new ItemFile(name + ".vb", classText),
            new ItemFile(name + ".Designer.vb", designerText)
        ];
    }
}
