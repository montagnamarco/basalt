using Basalt.Core.Model;

namespace Basalt.Designer;

public sealed record NewSolutionResult(
    string SolutionPath,
    string ProjectPath,
    string? MainWindowXamlPath);

/// <summary>Kind of application to generate.</summary>
public enum ProjectTemplate
{
    /// <summary>Avalonia application with a main window ready to be designed.</summary>
    AvaloniaApp,

    /// <summary>Command-line application.</summary>
    ConsoleApp,

    /// <summary>Class library.</summary>
    ClassLibrary,

    /// <summary>HTTP service exposing JSON endpoints.</summary>
    WebApi,

    /// <summary>Web application built from page-based Razor views.</summary>
    RazorPages,

    /// <summary>Web application with controllers and views.</summary>
    Mvc,

    /// <summary>Minimal web application serving a single page.</summary>
    WebApp,

    /// <summary>A test project, with xunit and a test to start from.</summary>
    TestProject,

    /// <summary>
    /// A Blazor application whose components are written in Visual Basic.
    /// </summary>
    /// <remarks>
    /// The .vbrazor components are compiled by the same generator the views
    /// use, so this needs no runtime of its own — only the reference and a
    /// component to start from.
    /// </remarks>
    Blazor,

    /// <summary>
    /// A Visual Basic 6 project, written the way Visual Basic 6 wrote them.
    /// </summary>
    /// <remarks>
    /// A .vbp and a .frm, not a .vbproj: the point is a project that opens
    /// both here and in Visual Basic 6, and Basalt writes the .NET project
    /// beside it when the .vbp is opened.
    /// </remarks>
    VisualBasic6,

    /// <summary>
    /// A QuickBASIC program compiled to a native executable.
    ///
    /// Not a .NET project: there is no project file, only the source, which
    /// the QuickBASIC backend compiles through C.
    /// </summary>
    QuickBasic
}

/// <summary>Which templates produce a web project.</summary>
internal static class ProjectTemplateExtensions
{
    public static bool IsWeb(this ProjectTemplate template) =>
        template is ProjectTemplate.WebApi
                 or ProjectTemplate.RazorPages
                 or ProjectTemplate.Mvc
                 or ProjectTemplate.WebApp
                 or ProjectTemplate.Blazor;

    /// <summary>
    /// Whether the template needs the Visual Basic Razor compiler.
    /// </summary>
    /// <remarks>
    /// Blazor as well as the view-based templates: a .vbrazor component is
    /// compiled by the same generator, so the reference is the same one.
    /// </remarks>
    public static bool UsesViews(this ProjectTemplate template) =>
        template is ProjectTemplate.RazorPages
                 or ProjectTemplate.Mvc
                 or ProjectTemplate.Blazor;
}

/// <summary>
/// Generates a ready-to-build solution in VB.NET.
///
/// The files are written directly instead of invoking "dotnet new" because the
/// official Avalonia templates for VB.NET do not exist, and because this way
/// the generated structure stays identical across the three platforms.
/// </summary>
public static class SolutionTemplates
{
    public static async Task<NewSolutionResult> CreateAsync(
        string parentDirectory,
        string solutionName,
        ProjectTemplate template,
        CancellationToken ct = default)
    {
        var root = Path.Combine(parentDirectory, solutionName);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"The folder '{solutionName}' already exists and is not empty.");

        var projectDirectory = Path.Combine(root, solutionName);
        Directory.CreateDirectory(projectDirectory);

        // QuickBASIC has no project file and no solution: the source is the
        // program, and the compiler takes it directly.
        if (template == ProjectTemplate.QuickBasic)
            return await CreateQuickBasicAsync(projectDirectory, solutionName, ct)
                .ConfigureAwait(false);

        // A Visual Basic 6 project is a .vbp and a .frm, not a .vbproj: the
        // point is a project that opens both here and in Visual Basic 6, and
        // Basalt writes the .NET project beside it when the .vbp is opened.
        if (template == ProjectTemplate.VisualBasic6)
            return await CreateVisualBasic6Async(projectDirectory, solutionName, ct)
                .ConfigureAwait(false);

        var projectPath = Path.Combine(projectDirectory, $"{solutionName}.vbproj");

        await File.WriteAllTextAsync(
            projectPath, ProjectFile(template), ct).ConfigureAwait(false);

        string? mainWindowXaml = null;

        switch (template)
        {
            case ProjectTemplate.AvaloniaApp:
                mainWindowXaml = await WriteAvaloniaAppAsync(
                    projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.ConsoleApp:
                await WriteConsoleAppAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.ClassLibrary:
                await WriteClassLibraryAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.TestProject:
                await WriteTestProjectAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.WebApi:
                await WriteWebApiAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.WebApp:
                await WriteWebAppAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.RazorPages:
                await WriteRazorPagesAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.Mvc:
                await WriteMvcAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;

            case ProjectTemplate.Blazor:
                await WriteBlazorAsync(projectDirectory, solutionName, ct).ConfigureAwait(false);
                break;
        }

        var solutionPath = Path.Combine(root, $"{solutionName}.sln");
        await File.WriteAllTextAsync(
            solutionPath,
            SolutionFile(solutionName, $"{solutionName}{Path.DirectorySeparatorChar}{Path.GetFileName(projectPath)}"),
            ct).ConfigureAwait(false);

        return new NewSolutionResult(solutionPath, projectPath, mainWindowXaml);
    }

    /// <summary>
    /// Writes a test project.
    ///
    /// One passing test rather than an empty class: a new project that runs
    /// and shows a green result proves the setup works, which an empty one
    /// leaves the user to find out for themselves.
    /// </summary>
    private static async Task WriteTestProjectAsync(
        string directory, string name, CancellationToken ct)
    {
        var source = """
              Imports Xunit

              Public Class CalculatorTests

                  <Fact>
                  Public Sub AddsTwoNumbers()
                      Assert.Equal(4, Add(2, 2))
                  End Sub

                  <Theory>
                  <InlineData(1, 1, 2)>
                  <InlineData(2, 3, 5)>
                  <InlineData(-1, 1, 0)>
                  Public Sub AddsTheseNumbersToo(a As Integer, b As Integer, expected As Integer)
                      Assert.Equal(expected, Add(a, b))
                  End Sub

                  Private Function Add(a As Integer, b As Integer) As Integer
                      Return a + b
                  End Function

              End Class

              """;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "CalculatorTests.vb"), source, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a QuickBASIC program.
    ///
    /// The example is chosen to use what the compiler supports and to print
    /// something at once, so that "new project, then run" works.
    /// </summary>
    private static async Task<NewSolutionResult> CreateQuickBasicAsync(
        string projectDirectory, string name, CancellationToken ct)
    {
        var sourcePath = Path.Combine(projectDirectory, $"{name}.bas");

        await File.WriteAllTextAsync(sourcePath, $"""
            ' {name} — a QuickBASIC program.
            ' Compiled to a native executable, through C.

            DECLARE FUNCTION Square%(n%)

            DIM total AS INTEGER
            total = 0

            FOR i = 1 TO 5
                total = total + Square%(i)
            NEXT i

            PRINT "The sum of the first five squares is"
            PRINT total

            FUNCTION Square%(n%)
                Square% = n% * n%
            END FUNCTION

            """, ct).ConfigureAwait(false);

        return new NewSolutionResult(sourcePath, sourcePath, null);
    }

    /// <summary>
    /// Writes a Blazor application whose components are Visual Basic.
    /// </summary>
    /// <remarks>
    /// A .vbrazor component and the two calls that host it. Nothing else is
    /// needed: the components are compiled by the same generator the views
    /// use, so the project carries a package reference and no runtime of its
    /// own.
    /// </remarks>
    private static async Task WriteBlazorAsync(
        string projectDirectory, string name, CancellationToken ct)
    {
        var components = Path.Combine(projectDirectory, "Components");

        Directory.CreateDirectory(components);

        await File.WriteAllTextAsync(
            Path.Combine(components, "Home.vbrazor"), $"""
            @Page "/"

            <h1>@Titolo</h1>

            <p>Conteggio: @count</p>

            <button onclick="@AddressOf Incrementa">Aggiungi</button>

            @Functions
                <Global.Microsoft.AspNetCore.Components.Parameter>
                Public Property Titolo As String = "{name}"

                Private count As Integer

                Private Sub Incrementa()
                    count += 1
                End Sub
            @End Functions

            """, ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.vb"), $"""
            Imports Microsoft.AspNetCore.Builder
            Imports Microsoft.Extensions.DependencyInjection

            Public Module Program

                Public Sub Main(args As String())
                    Dim builder = WebApplication.CreateBuilder(args)

                    builder.Services.AddRazorComponents()

                    Dim app = builder.Build()

                    app.UseAntiforgery()
                    app.MapRazorComponents(Of Global.Components.Home)()

                    app.Run()
                End Sub

            End Module

            """, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a Visual Basic 6 project, in the format Visual Basic 6 wrote.
    /// </summary>
    /// <remarks>
    /// CRLF throughout, because that is what a .frm is: a file with Unix
    /// endings is one no copy of Visual Basic 6 ever produced, and the point
    /// of this template is a project that opens in both.
    ///
    /// The .vbproj is not written here. It is written when the .vbp is opened,
    /// so the two cannot drift apart — and so the folder holds only what
    /// Visual Basic 6 would have put in it.
    /// </remarks>
    private static async Task<NewSolutionResult> CreateVisualBasic6Async(
        string projectDirectory, string name, CancellationToken ct)
    {
        var projectPath = Path.Combine(projectDirectory, $"{name}.vbp");
        var formPath = Path.Combine(projectDirectory, "Form1.frm");

        await File.WriteAllTextAsync(projectPath, Crlf($"""
            Type=Exe
            Form=Form1.frm
            Startup="Form1"
            Name="{name}"
            MajorVer=1
            MinorVer=0
            RevisionVer=0

            """), ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(formPath, Crlf($$"""
            VERSION 5.00
            Begin VB.Form Form1 
               Caption         =   "{{name}}"
               ClientHeight    =   3195
               ClientWidth     =   4680
               StartUpPosition =   3  'Windows Default
               Begin VB.TextBox txtNome 
                  Height          =   285
                  Left            =   1320
                  TabIndex        =   1
                  Top             =   480
                  Width           =   2295
               End
               Begin VB.CommandButton cmdSaluta 
                  Caption         =   "Saluta"
                  Height          =   375
                  Left            =   1320
                  TabIndex        =   0
                  Top             =   1080
                  Width           =   1215
               End
               Begin VB.Label lblNome 
                  Caption         =   "Nome:"
                  Height          =   255
                  Left            =   240
                  TabIndex        =   2
                  Top             =   525
                  Width           =   975
               End
            End
            Attribute VB_Name = "Form1"
            Attribute VB_GlobalNameSpace = False
            Attribute VB_Creatable = False
            Attribute VB_PredeclaredId = True
            Attribute VB_Exposed = False
            Option Explicit

            Private Sub cmdSaluta_Click()
                If Trim$(txtNome.Text) = "" Then
                    MsgBox "Inserire il nome", vbExclamation
                    Exit Sub
                End If
                MsgBox "Ciao " & txtNome.Text
            End Sub

            Private Sub Form_Load()
                txtNome.Text = ""
            End Sub

            """), ct).ConfigureAwait(false);

        return new NewSolutionResult(projectPath, projectPath, formPath);
    }

    /// <summary>The text with the line endings Visual Basic 6 wrote.</summary>
    private static string Crlf(string text) =>
        text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string ProjectFile(ProjectTemplate template)
    {
        var outputType = template switch
        {
            ProjectTemplate.AvaloniaApp => "WinExe",
            ProjectTemplate.ConsoleApp or ProjectTemplate.QuickBasic => "Exe",
            _ => "Library"
        };

        var avaloniaPackages = template == ProjectTemplate.AvaloniaApp
            ? """

              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
                <PackageReference Include="Avalonia.Desktop" Version="12.1.1" />
                <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.1" />
              </ItemGroup>
            """
            : "";

        // A test project needs the runner as well as the framework: without
        // Test.Sdk the tests compile and nothing discovers them.
        var testPackages = template == ProjectTemplate.TestProject
            ? """

              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit.v3" Version="3.2.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
              </ItemGroup>
            """
            : "";

        // A web project uses the Web SDK, which brings in the ASP.NET Core
        // framework reference and the launch settings a server needs.
        var sdk = template.IsWeb() ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk";

        // The Web SDK infers the output type; stating it would fight the SDK.
        var outputTypeElement = template.IsWeb()
            ? ""
            : $"\n    <OutputType>{outputType}</OutputType>";

        // An empty RootNamespace: VB prepends it to the Namespace declared in
        // the files, which would otherwise produce doubled-up type names.
        return $"""
            <Project Sdk="{sdk}">

              <PropertyGroup>{outputTypeElement}
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            {avaloniaPackages}{testPackages}{VbHtmlSupport(template)}
            </Project>
            """;
    }

    /// <summary>Writes an HTTP service exposing JSON endpoints.</summary>
    private static async Task WriteWebApiAsync(
        string directory, string name, CancellationToken ct)
    {
        var code = $$"""
              Imports Microsoft.AspNetCore.Builder
              Imports Microsoft.AspNetCore.Http

              Namespace {{name}}

                  Public Class WeatherForecast
                      ' Square brackets let a reserved word be used as a name.
                      Public Property [Date] As DateOnly
                      Public Property TemperatureC As Integer
                      Public Property Summary As String = ""

                      Public ReadOnly Property TemperatureF As Integer
                          Get
                              Return 32 + CInt(TemperatureC / 0.5556)
                          End Get
                      End Property
                  End Class

                  Module Program

                      Private ReadOnly Summaries As String() =
                          {"Freezing", "Chilly", "Mild", "Warm", "Sweltering"}

                      Sub Main(args As String())
                          Dim builder = WebApplication.CreateBuilder(args)
                          Dim app = builder.Build()

                          app.MapGet("/weatherforecast", Function()
                                                             Return Enumerable.Range(1, 5).
                                                                 Select(Function(i) New WeatherForecast With {
                                                                     .Date = DateOnly.FromDateTime(DateTime.Now.AddDays(i)),
                                                                     .TemperatureC = Random.Shared.Next(-20, 55),
                                                                     .Summary = Summaries(Random.Shared.Next(Summaries.Length))
                                                                 }).ToArray()
                                                         End Function)

                          app.Run()
                      End Sub

                  End Module

              End Namespace

              """;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "Program.vb"), code, ct)
            .ConfigureAwait(false);

        await WriteWebSupportFilesAsync(directory, name, ct).ConfigureAwait(false);
    }

    /// <summary>Writes a minimal web application serving a single page.</summary>
    private static async Task WriteWebAppAsync(
        string directory, string name, CancellationToken ct)
    {
        var code = $$"""
              Imports Microsoft.AspNetCore.Builder
              Imports Microsoft.AspNetCore.Http

              Namespace {{name}}

                  Module Program

                      Sub Main(args As String())
                          Dim builder = WebApplication.CreateBuilder(args)
                          Dim app = builder.Build()

                          app.MapGet("/", Function()
                                              Return Results.Content(
                                                  "<!DOCTYPE html><html><body><h1>{{name}}</h1></body></html>",
                                                  "text/html")
                                          End Function)

                          app.Run()
                      End Sub

                  End Module

              End Namespace

              """;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "Program.vb"), code, ct)
            .ConfigureAwait(false);

        await WriteWebSupportFilesAsync(directory, name, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a page-based web application.
    ///
    /// ASP.NET Core's Razor Pages cannot be used: the Razor generator emits
    /// C# only, so the pages are .vbhtml templates compiled by our own
    /// generator and served from mapped routes.
    /// </summary>
    private static async Task WriteRazorPagesAsync(
        string directory, string name, CancellationToken ct)
    {
        var pages = Path.Combine(directory, "Views");
        Directory.CreateDirectory(pages);

        await File.WriteAllTextAsync(Path.Combine(pages, "Index.vbhtml"), $$"""
            @ModelType {{name}}.IndexModel
            <!DOCTYPE html>
            <html>
            <head>
                <title>@Model.Title</title>
            </head>
            <body>
                <h1>@Model.Title</h1>
                <p>Pages are .vbhtml templates compiled by the Visual Basic Razor generator.</p>
                <ul>
                @For Each item In Model.Items
                    <li>@item</li>
                @Next
                </ul>
            </body>
            </html>

            """, ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(directory, "Program.vb"), $$"""
            Imports Microsoft.AspNetCore.Builder
            Imports Microsoft.AspNetCore.Http

            Namespace {{name}}

                Public Class IndexModel
                    Public Property Title As String = "{{name}}"
                    Public Property Items As String() = {"first", "second", "third"}
                End Class

                Module Program

                    Sub Main(args As String())
                        Dim builder = WebApplication.CreateBuilder(args)
                        Dim app = builder.Build()

                        app.MapGet("/", Function()
                                            ' Fully qualified: "Index" alone is ambiguous
                                            ' against ASP.NET Core's own members.
                                            Dim view As New Global.Views.Index()
                                            view.Model = New IndexModel()
                                            Return Results.Content(view.Render(), "text/html")
                                        End Function)

                        app.Run()
                    End Sub

                End Module

            End Namespace

            """, ct).ConfigureAwait(false);

        await WriteWebSupportFilesAsync(directory, name, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a web application with controllers and views.
    ///
    /// The controllers render .vbhtml templates directly, since MVC's view
    /// engine only knows how to locate and compile C# Razor views.
    /// </summary>
    private static async Task WriteMvcAsync(
        string directory, string name, CancellationToken ct)
    {
        var controllers = Path.Combine(directory, "Controllers");
        Directory.CreateDirectory(controllers);

        var views = Path.Combine(directory, "Views", "Home");
        Directory.CreateDirectory(views);

        await File.WriteAllTextAsync(Path.Combine(views, "Index.vbhtml"), $$"""
            @ModelType {{name}}.HomeViewModel
            <!DOCTYPE html>
            <html>
            <head>
                <title>@Model.Title</title>
            </head>
            <body>
                <h1>@Model.Title</h1>
                <p>Rendered by a controller from a .vbhtml view.</p>
                <ul>
                @For Each item In Model.Items
                    <li>@item</li>
                @Next
                </ul>
            </body>
            </html>

            """, ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(controllers, "HomeController.vb"), $$"""
            Imports Microsoft.AspNetCore.Mvc

            Namespace {{name}}

                Public Class HomeViewModel
                    Public Property Title As String = "{{name}}"
                    Public Property Items As String() = {"first", "second", "third"}
                End Class

                Public Class HomeController
                    Inherits Controller

                    Public Function Index() As IActionResult
                        Dim view As New Global.Views.Home.Index()
                        view.Model = New HomeViewModel()

                        Return Content(view.Render(), "text/html")
                    End Function

                End Class

            End Namespace

            """, ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(directory, "Program.vb"), $$"""
            Imports Microsoft.AspNetCore.Builder
            Imports Microsoft.Extensions.DependencyInjection

            Namespace {{name}}

                Module Program

                    Sub Main(args As String())
                        Dim builder = WebApplication.CreateBuilder(args)
                        builder.Services.AddControllers()

                        Dim app = builder.Build()
                        app.UseStaticFiles()
                        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}")

                        app.Run()
                    End Sub

                End Module

            End Namespace

            """, ct).ConfigureAwait(false);

        await WriteWebSupportFilesAsync(directory, name, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the files every web project needs: configuration, launch
    /// settings, and a place for static content.
    /// </summary>
    private static async Task WriteWebSupportFilesAsync(
        string directory, string name, CancellationToken ct)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "appsettings.json"), """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft.AspNetCore": "Warning"
                }
              },
              "AllowedHosts": "*"
            }

            """, ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(Path.Combine(directory, "appsettings.Development.json"), """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Debug",
                  "Microsoft.AspNetCore": "Information"
                }
              }
            }

            """, ct).ConfigureAwait(false);

        var properties = Path.Combine(directory, "Properties");
        Directory.CreateDirectory(properties);

        await File.WriteAllTextAsync(Path.Combine(properties, "launchSettings.json"), $$"""
            {
              "profiles": {
                "{{name}}": {
                  "commandName": "Project",
                  "dotnetRunMessages": true,
                  "launchBrowser": true,
                  "applicationUrl": "http://localhost:5000",
                  "environmentVariables": {
                    "ASPNETCORE_ENVIRONMENT": "Development"
                  }
                }
              }
            }

            """, ct).ConfigureAwait(false);

        var web = Path.Combine(directory, "wwwroot");
        Directory.CreateDirectory(web);

        await File.WriteAllTextAsync(Path.Combine(web, "site.css"), """
            body {
                font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
                margin: 2rem auto;
                max-width: 48rem;
                line-height: 1.5;
            }

            """, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Wires up the .vbhtml source generator for projects with views.
    ///
    /// ASP.NET Core's own Razor generator emits C# only, so a Visual Basic
    /// project needs this one to compile its templates. The paths are relative
    /// to the generated project, which sits two levels below the repository
    /// that provides the generator.
    /// </summary>
    private static string VbHtmlSupport(ProjectTemplate template)
    {
        if (!template.UsesViews()) return "";

        // Project references rather than a package: the generator is not
        // published to NuGet, so the generated project points at wherever this
        // IDE was installed. RazorVbPath is written into the project so the
        // paths stay in one place and are easy to retarget.
        return $"""

          <!--
            Razor emits C# only, so Visual Basic views are compiled by the
            .vbhtml source generator instead. Point RazorVbPath at the folder
            holding Basalt.Razor.Vb if this project is moved.
          -->
          <PropertyGroup>
            <RazorVbPath>{RazorVbRoot()}</RazorVbPath>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="$(RazorVbPath)/Basalt.Razor.Vb/Basalt.Razor.Vb.csproj" />
            <ProjectReference Include="$(RazorVbPath)/Basalt.Razor.Vb.Generator/Basalt.Razor.Vb.Generator.csproj"
                              OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
            <AdditionalFiles Include="Views/**/*.vbhtml" />

            <!--
              Declared here rather than coming from the package's props: those
              are imported for a PackageReference and not for a project one,
              so a .vbrazor reached the compiler as nothing at all and the
              component was never generated.
            -->
            <AdditionalFiles Include="Components/**/*.vbrazor" />
          </ItemGroup>
        """;
    }

    /// <summary>
    /// Folder holding the .vbhtml generator projects.
    ///
    /// Found by walking up from the running IDE to the repository root, so a
    /// generated project references the same copy of the generator the IDE
    /// itself was built from.
    /// </summary>
    private static string RazorVbRoot()
    {
        // An explicit setting wins: it lets the IDE point generated projects at
        // wherever it was installed, and lets tests point at the repository.
        var configured = Environment.GetEnvironmentVariable("BASALT_RAZORVB_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return configured!;

        var directory = AppContext.BaseDirectory;

        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "src", "Basalt.Razor.Vb");
            if (Directory.Exists(candidate)) return Path.Combine(directory, "src");

            directory = Path.GetDirectoryName(directory);
        }

        // Not found: a relative guess the user can correct, rather than a path
        // that silently points nowhere.
        return "../../../src";
    }

    private static async Task<string> WriteAvaloniaAppAsync(
        string directory, string name, CancellationToken ct)
    {
        // Main window, designable in the designer right from the start.
        var windowXaml = Path.Combine(directory, "MainWindow.axaml");
        await File.WriteAllTextAsync(windowXaml, $"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="{name}.MainWindow"
                    Width="800" Height="500"
                    Title="{name}">
              <Grid>
              </Grid>
            </Window>
            """, ct).ConfigureAwait(false);

        var windowDocument = Model.XamlDocument.Parse(
            await File.ReadAllTextAsync(windowXaml, ct).ConfigureAwait(false), windowXaml);

        await File.WriteAllTextAsync(
            $"{windowXaml}.vb",
            CodeBehindGenerator.Generate(windowDocument, SourceLanguage.VisualBasic),
            ct).ConfigureAwait(false);

        // Application and entry point.
        await File.WriteAllTextAsync(Path.Combine(directory, "App.axaml"), $"""
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="{name}.App">
              <Application.Styles>
                <FluentTheme />
              </Application.Styles>
            </Application>
            """, ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "App.axaml.vb"), VbApp(name), ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "Program.vb"), VbProgram(name), ct).ConfigureAwait(false);

        return windowXaml;
    }

    private static string VbApp(string name) => $"""
        Imports Avalonia
        Imports Avalonia.Controls.ApplicationLifetimes
        Imports Avalonia.Markup.Xaml

        Namespace {name}

            Partial Public Class App
                Inherits Application

                Public Overrides Sub Initialize()
                    AvaloniaXamlLoader.Load(Me)
                End Sub

                Public Overrides Sub OnFrameworkInitializationCompleted()
                    Dim desktop = TryCast(ApplicationLifetime, IClassicDesktopStyleApplicationLifetime)
                    If desktop IsNot Nothing Then
                        desktop.MainWindow = New MainWindow()
                    End If

                    MyBase.OnFrameworkInitializationCompleted()
                End Sub

            End Class

        End Namespace

        """;

    private static string VbProgram(string name) => $"""
        Imports Avalonia

        Namespace {name}

            Module Program

                <STAThread>
                Sub Main(args As String())
                    AppBuilder.Configure(Of App)() _
                        .UsePlatformDetect() _
                        .StartWithClassicDesktopLifetime(args)
                End Sub

            End Module

        End Namespace

        """;

    private static async Task WriteConsoleAppAsync(
        string directory, string name, CancellationToken ct)
    {
        var code = $"""
              Namespace {name}

                  Module Program

                      Sub Main(args As String())
                          Console.WriteLine("Ciao dal progetto {name}")
                      End Sub

                  End Module

              End Namespace

              """;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "Program.vb"), code, ct).ConfigureAwait(false);
    }

    private static async Task WriteClassLibraryAsync(
        string directory, string name, CancellationToken ct)
    {
        var code = $"""
              Namespace {name}

                  Public Class Classe1

                  End Class

              End Namespace

              """;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "Classe1.vb"), code, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// .sln file in the classic format. The project type GUID is a constant
    /// defined by Visual Studio, identifying the project as VB.NET.
    /// </summary>
    private static string SolutionFile(string name, string relativeProjectPath)
    {
        const string typeGuid = "F184B08F-C81C-45F6-A57F-5ABD9991F28F";

        // The project GUID is derived from the name: stable across runs, so
        // regenerating the solution does not produce spurious diffs.
        var projectGuid = DeterministicGuid(name).ToString("B").ToUpperInvariant();

        // The .sln format always uses backslashes in paths.
        var windowsPath = relativeProjectPath.Replace(Path.DirectorySeparatorChar, '\\');

        return $$"""
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{{{typeGuid}}}") = "{{name}}", "{{windowsPath}}", "{{projectGuid}}"
            EndProject
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            		Release|Any CPU = Release|Any CPU
            	EndGlobalSection
            	GlobalSection(ProjectConfigurationPlatforms) = postSolution
            		{{projectGuid}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
            		{{projectGuid}}.Debug|Any CPU.Build.0 = Debug|Any CPU
            		{{projectGuid}}.Release|Any CPU.ActiveCfg = Release|Any CPU
            		{{projectGuid}}.Release|Any CPU.Build.0 = Release|Any CPU
            	EndGlobalSection
            EndGlobal

            """;
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash);
    }
}
