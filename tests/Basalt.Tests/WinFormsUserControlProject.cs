namespace Basalt.Tests;

/// <summary>
/// A temporary Visual Basic WinForms project with the user's own controls, for the designer tests:
/// <c>GaugeControl</c> (paints, has a public property), <c>HiddenControl</c> (hidden from the toolbox),
/// <c>BrokenControl</c> (its constructor throws), <c>PaintBrokenControl</c> (its OnPaint throws),
/// <c>ReadingsControl</c> (properties of a constructed generic and of an array of its own type),
/// <c>ProjectComponent</c> (a component, not a control), <c>ContainerComponent</c> (a component whose constructor takes the
/// form's IContainer), <c>BrokenComponent</c> (a component whose constructor throws), <c>Form1</c>, and two class libraries it references: <c>SharedControls</c>
/// (<c>Widgets.LibraryControl</c> and a hidden <c>Widgets.HiddenLibraryControl</c>) and <c>BrokenLibrary</c>, whose one control
/// carries an attribute with a throwing static constructor, so describing it fails while the other libraries still list.
/// It is an executable that targets Windows Forms, so the view model treats it as the startup project;
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
              <ItemGroup>
                <ProjectReference Include="SharedControls/SharedControls.vbproj" />
                <ProjectReference Include="BrokenLibrary/BrokenLibrary.vbproj" />
                <Compile Remove="SharedControls/**" />
                <Compile Remove="BrokenLibrary/**" />
              </ItemGroup>
            </Project>
            """);
        project.WriteGauge(42);
        project.WriteSharedControls();
        project.WriteBrokenLibrary();
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
        File.WriteAllText(Path.Combine(project.Root, "ContainerComponent.vb"), """
            Public Class ContainerComponent
                Inherits System.ComponentModel.Component

                Public Sub New(container As System.ComponentModel.IContainer)
                    MyBase.New()
                    container.Add(Me)
                End Sub
            End Class
            """);
        File.WriteAllText(Path.Combine(project.Root, "BrokenComponent.vb"), """
            Public Class BrokenComponent
                Inherits System.ComponentModel.Component

                Public Sub New()
                    MyBase.New()
                    Throw New System.InvalidOperationException("Component constructor failed on purpose.")
                End Sub
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
    /// Writes the class library the project references, in a folder of its own: <c>LibraryControl</c> (a visible
    /// UserControl whose namespace is the library's root namespace <c>Widgets</c>) and <c>HiddenLibraryControl</c> (hidden from the toolbox). Its build
    /// copies <c>SharedControls.dll</c> into the project's output, as a ProjectReference does.
    /// </summary>
    public void WriteSharedControls()
    {
        Directory.CreateDirectory(Path.Combine(Root, "SharedControls"));
        File.WriteAllText(Path.Combine(Root, "SharedControls", "SharedControls.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <RootNamespace>Widgets</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        // The namespace is the project's RootNamespace: a Namespace statement here would be prefixed with it again.
        File.WriteAllText(Path.Combine(Root, "SharedControls", "LibraryControl.vb"), """
            Public Class LibraryControl
                Inherits System.Windows.Forms.UserControl
            End Class
            """);
        File.WriteAllText(Path.Combine(Root, "SharedControls", "HiddenLibraryControl.vb"), """
            <System.ComponentModel.ToolboxItem(False)>
            Public Class HiddenLibraryControl
                Inherits System.Windows.Forms.UserControl
            End Class
            """);
    }

    /// <summary>
    /// Writes the second class library the project references, in a folder of its own. Its <c>BrokenLibraryControl</c> is
    /// a visible UserControl marked with <c>ThrowingAttribute</c>, whose static constructor throws. Describing the control
    /// instantiates its attributes, so the description fails with a <c>TypeInitializationException</c> and nothing else
    /// of the library, nor of the other libraries, is affected.
    /// </summary>
    public void WriteBrokenLibrary()
    {
        Directory.CreateDirectory(Path.Combine(Root, "BrokenLibrary"));
        File.WriteAllText(Path.Combine(Root, "BrokenLibrary", "BrokenLibrary.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <RootNamespace>Broken</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(Root, "BrokenLibrary", "BrokenLibraryControl.vb"), """
            Public Class ThrowingAttribute
                Inherits System.Attribute

                Shared Sub New()
                    Throw New System.InvalidOperationException("Static constructor failed on purpose.")
                End Sub
            End Class

            <Throwing>
            Public Class BrokenLibraryControl
                Inherits System.Windows.Forms.UserControl
            End Class
            """);
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
    /// Declares the form's components container as Visual Studio writes it: a <c>components</c> field of type IContainer, created
    /// first in InitializeComponent. A component whose constructor takes the container is then given <c>Me.components</c>.
    /// </summary>
    public void DeclareComponentsContainer()
    {
        File.WriteAllText(DesignerPath, """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Private components As System.ComponentModel.IContainer

                Private Sub InitializeComponent()
                    Me.components = New System.ComponentModel.Container()
                    Me.ClientSize = New System.Drawing.Size(500, 400)
                    Me.Text = "Form1"
                    Me.ResumeLayout(False)
                End Sub
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
