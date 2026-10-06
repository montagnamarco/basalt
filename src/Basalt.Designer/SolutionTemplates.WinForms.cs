using System.Security;
using Basalt.Core.Platform;

namespace Basalt.Designer;

public static partial class SolutionTemplates
{
    private static string WinFormsSupport(ProjectTemplate template, BasaltWinFormsReference? reference)
    {
        if (template != ProjectTemplate.WinFormsApp) return "";
        reference = (reference ?? BasaltWinFormsReference.Current()).Require();

        // With the packages the project names no folder of this machine: the
        // version is all it says, and the IDE passes the package folder to
        // restore (RestoreAdditionalProjectSources, see
        // WindowsDesktopProjectCompatibility) instead of writing it here, so
        // the project still opens elsewhere once the packages are on a feed.
        if (reference.UsesPackages)
            return $"""

                  <ItemGroup>
                    <Import Include="System.Windows.Forms" />
                    <Import Include="System.Drawing" />
                    <PackageReference Include="{BasaltWinFormsPackages.PackageId}" Version="{SecurityElement.Escape(reference.Packages!.Version)}" />
                    <Compile Update="MainForm.Designer.vb">
                      <DependentUpon>MainForm.vb</DependentUpon>
                    </Compile>
                  </ItemGroup>
                """;

        return $"""

              <PropertyGroup>
                <!-- Retarget this path when moving the project to another installation. -->
                <BasaltFormsPath>{SecurityElement.Escape(reference.SourceRoot)}</BasaltFormsPath>
              </PropertyGroup>
              <ItemGroup>
                <Import Include="System.Windows.Forms" />
                <Import Include="System.Drawing" />
                <ProjectReference Include="$(BasaltFormsPath)/Basalt.Forms/Basalt.Forms.csproj" />
                <ProjectReference Include="$(BasaltFormsPath)/Basalt.Forms.VisualBasic/Basalt.Forms.VisualBasic.csproj" />
                <!-- Explicit runtime companions also avoid SDK transitive paths through symlinked folders. -->
                <ProjectReference Include="$(BasaltFormsPath)/Basalt.Drawing/Basalt.Drawing.csproj" />
                <ProjectReference Include="$(BasaltFormsPath)/Basalt.Forms.Primitives/Basalt.Forms.Primitives.csproj" />
                <ProjectReference Include="$(BasaltFormsPath)/Basalt.Windows.Extensions/Basalt.Windows.Extensions.csproj" />
                <ProjectReference Include="$(BasaltFormsPath)/Basalt.Accessibility/Basalt.Accessibility.vbproj" />
                <Compile Update="MainForm.Designer.vb">
                  <DependentUpon>MainForm.vb</DependentUpon>
                </Compile>
              </ItemGroup>
            """;
    }

    private static async Task<string> WriteWinFormsAppAsync(string directory, string name, CancellationToken ct)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "Program.vb"), """
            Imports System.Windows.Forms

            Module Program
                <STAThread>
                Sub Main()
                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
                    Application.EnableVisualStyles()
                    Application.SetCompatibleTextRenderingDefault(False)
                    Application.Run(New MainForm())
                End Sub
            End Module
            """, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, "MainForm.vb"), """
            Public Partial Class MainForm
                Public Sub New()
                    InitializeComponent()
                End Sub
            End Class
            """, ct).ConfigureAwait(false);
        var designerPath = Path.Combine(directory, "MainForm.Designer.vb");
        await File.WriteAllTextAsync(designerPath, $$"""
            <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated>
            Public Partial Class MainForm
                Inherits Global.System.Windows.Forms.Form

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
                    Me.ClientSize = New Global.System.Drawing.Size(800, 500)
                    Me.Name = "MainForm"
                    Me.Text = "{{name}}"
                    Me.ResumeLayout(False)
                End Sub
            End Class
            """, ct).ConfigureAwait(false);
        return designerPath;
    }
}
