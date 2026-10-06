using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Proves the files <see cref="WinFormsProjectResources.Import"/> writes — Resources.resx,
/// Resources.Designer.vb, the project items and the copied image — actually compile together
/// with a Designer.vb that reads the image back the way the live designer writes it
/// ("Global.RootNamespace.My.Resources.Resources.Name"): a real "dotnet build" of a temporary
/// WinForms VB project, not just the text each piece produces in isolation.
/// </summary>
[Collection("PackageBuild")]
public sealed class WinFormsProjectResourceBuildTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-project-resource-build-").FullName;

    public void Dispose() => WindowsDesktopSamples.DeleteQuietly(_root);

    [Fact]
    public async Task AFormReferencingAnImportedProjectResourceBuildsWithoutErrors()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds WinForms projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();

        var projectPath = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <RootNamespace>App</RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        var pngPath = Path.Combine(_root, "logo.png");
        using (var bitmap = new Bitmap(4, 4))
        {
            bitmap.Save(pngPath, ImageFormat.Png);
        }
        var entry = WinFormsProjectResources.Import(projectPath, pngPath, isIcon: false);

        File.WriteAllText(Path.Combine(_root, "Form1.vb"), """
            Public Class Form1
                Inherits System.Windows.Forms.Form
                Public Sub New()
                    InitializeComponent()
                End Sub
            End Class
            """);
        File.WriteAllText(Path.Combine(_root, "Form1.Designer.vb"), $"""
            Partial Class Form1
                Private Sub InitializeComponent()
                    Me.PictureBox1 = New System.Windows.Forms.PictureBox()
                    Me.PictureBox1.Name = "PictureBox1"
                    Me.PictureBox1.Image = Global.App.My.Resources.Resources.{entry.Name}
                    Me.Controls.Add(Me.PictureBox1)
                    Me.ClientSize = New System.Drawing.Size(300, 300)
                End Sub
                Friend WithEvents PictureBox1 As System.Windows.Forms.PictureBox
            End Class
            """);

        var builder = new MsBuildBuildService();
        var output = new StringBuilder();
        builder.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };
        var result = await builder.BuildAsync(projectPath);

        Assert.True(result.Succeeded, output.ToString());
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
