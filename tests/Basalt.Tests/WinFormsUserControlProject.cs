namespace Basalt.Tests;

/// <summary>
/// A temporary Visual Basic WinForms project with the user's own controls, for the designer tests:
/// <c>GaugeControl</c> (paints, has a public property), <c>HiddenControl</c> (hidden from the toolbox),
/// <c>BrokenControl</c> (its constructor throws), <c>PaintBrokenControl</c> (its OnPaint throws),
/// <c>ReadingsControl</c> (properties of a constructed generic and of an array of its own type),
/// <c>ProjectComponent</c> (a component, not a control), and <c>Form1</c>. It is an executable that targets Windows Forms, so the view model treats it as the startup project;
/// the retargeting is the same one <see cref="WinFormsProjectResourceBuildTests"/> exercises.
/// </summary>
public sealed class WinFormsUserControlProject : IDisposable
{
    private WinFormsUserControlProject(string root) => Root = root;

    public string Root { get; }
    public string ProjectPath => Path.Combine(Root, "App.vbproj");
    public string DesignerPath => Path.Combine(Root, "Form1.Designer.vb");
    public string GaugePath => Path.Combine(Root, "GaugeControl.vb");

    /// <summary>A new project in a fresh temporary folder, with the default value of the gauge at 42.</summary>
    public static WinFormsUserControlProject Create()
    {
        var project = new WinFormsUserControlProject(Directory.CreateTempSubdirectory("basalt-user-control-").FullName);
        File.WriteAllText(project.ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <RootNamespace>App</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        project.WriteGauge(42);
        File.WriteAllText(Path.Combine(project.Root, "HiddenControl.vb"), """
            <System.ComponentModel.ToolboxItem(False)>
            Public Class HiddenControl
                Inherits System.Windows.Forms.UserControl
            End Class
            """);
        File.WriteAllText(Path.Combine(project.Root, "ReadingsControl.vb"), """
            Public Class Reading
                Public Property Amount As Integer
            End Class

            Public Class ReadingsControl
                Inherits System.Windows.Forms.UserControl

                Public Property Readings As System.Collections.Generic.List(Of Reading)
                Public Property Samples As Reading()
            End Class
            """);

        // A project component is not a control: the toolbox must not list it, as the designer cannot place it.
        File.WriteAllText(Path.Combine(project.Root, "ProjectComponent.vb"), """
            Public Class ProjectComponent
                Inherits System.ComponentModel.Component
            End Class
            """);
        File.WriteAllText(Path.Combine(project.Root, "BrokenControl.vb"), """
            Public Class BrokenControl
                Inherits System.Windows.Forms.UserControl

                Public Sub New()
                    MyBase.New()
                    Throw New System.InvalidOperationException("Constructor failed on purpose.")
                End Sub
            End Class
            """);
        File.WriteAllText(Path.Combine(project.Root, "PaintBrokenControl.vb"), """
            Public Class PaintBrokenControl
                Inherits System.Windows.Forms.UserControl

                Protected Overrides Sub OnPaint(e As System.Windows.Forms.PaintEventArgs)
                    Throw New System.InvalidOperationException("Painting failed on purpose.")
                End Sub
            End Class
            """);
        File.WriteAllText(Path.Combine(project.Root, "Program.vb"), """
            Module Program
                Public Sub Main()
                    System.Windows.Forms.Application.Run(New Form1())
                End Sub
            End Module
            """);
        File.WriteAllText(Path.Combine(project.Root, "Form1.vb"), """
            Public Class Form1
                Public Sub New()
                    InitializeComponent()
                End Sub
            End Class
            """);
        File.WriteAllText(project.DesignerPath, """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Private Sub InitializeComponent()
                    Me.ClientSize = New System.Drawing.Size(500, 400)
                    Me.Text = "Form1"
                    Me.ResumeLayout(False)
                End Sub
            End Class
            """);
        return project;
    }

    /// <summary>
    /// Writes the gauge with the given default value for its property. With <paramref name="failWhenParented"/>
    /// the gauge throws when it is given a parent, which is what the designer's Controls.Add does when it
    /// puts the control on the form: a rebuilt preview that cannot be built.
    /// </summary>
    public void WriteGauge(int defaultValue, bool failWhenParented = false)
    {
        var parentChanged = failWhenParented ? """
            Protected Overrides Sub OnParentChanged(e As System.EventArgs)
                MyBase.OnParentChanged(e)
                If Parent IsNot Nothing Then Throw New System.InvalidOperationException("Parenting failed on purpose.")
            End Sub
            """ : string.Empty;

        File.WriteAllText(GaugePath, $"""
            Public Class GaugeControl
                Inherits System.Windows.Forms.UserControl

                Private _value As Integer = {defaultValue}

                Public Property Value As Integer
                    Get
                        Return _value
                    End Get
                    Set(value As Integer)
                        _value = value
                        Invalidate()
                    End Set
                End Property

                Protected Overrides Sub OnPaint(e As System.Windows.Forms.PaintEventArgs)
                    MyBase.OnPaint(e)
                    e.Graphics.Clear(System.Drawing.Color.Orange)
                End Sub

            {parentChanged}
            End Class
            """);
    }

    /// <summary>
    /// Makes the project use My with Windows Forms, and adds a control whose constructor reads My.Computer. The My
    /// class derives from a Windows Forms base class, so creating the control needs Microsoft.VisualBasic.Forms,
    /// which the project's output folder holds and the IDE does not ship.
    /// </summary>
    public void UseMyWithWindowsForms()
    {
        File.WriteAllText(ProjectPath, File.ReadAllText(ProjectPath).Replace(
            "<UseWindowsForms>true</UseWindowsForms>",
            "<UseWindowsForms>true</UseWindowsForms>\n    <MyType>WindowsForms</MyType>", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(Root, "ComputerControl.vb"), """
            Public Class ComputerControl
                Inherits System.Windows.Forms.UserControl

                Public Sub New()
                    MyBase.New()
                    Me.Tag = My.Computer.Name
                End Sub
            End Class
            """);
    }

    public void Dispose() => WindowsDesktopSamples.DeleteQuietly(Root);
}
