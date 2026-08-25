using Basalt.Core.Model;
using Basalt.Razor.Vb;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The whole path from template to running page: parse, generate, compile, run.
///
/// Checking the shape of the generated source is not enough — Visual Basic has
/// to accept it. These tests build a real project and execute the view.
/// </summary>
public sealed class VbHtmlEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-vbhtml", Guid.NewGuid().ToString("N"));

    public VbHtmlEndToEndTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// Builds a project containing the generated view plus the runtime, and
    /// returns what the view renders.
    /// </summary>
    private async Task<string> RenderAsync(string template, string modelSetup, string modelClass = "")
    {
        var document = VbHtmlParser.Parse(template);
        var view = VbHtmlCodeWriter.Write(document, "TestView", "Views");

        var runtimeSource = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRoot(), "src", "Basalt.Razor.Vb", "VbHtmlView.cs"));

        // The runtime is C#, the view is VB: they go in separate projects.
        var runtimeDirectory = Path.Combine(_root, "Runtime");
        Directory.CreateDirectory(runtimeDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(runtimeDirectory, "Runtime.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(runtimeDirectory, "VbHtmlView.cs"), runtimeSource);

        var appDirectory = Path.Combine(_root, "App");
        Directory.CreateDirectory(appDirectory);

        await File.WriteAllTextAsync(Path.Combine(appDirectory, "App.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Runtime/Runtime.csproj" />
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(appDirectory, "TestView.vb"), view);

        await File.WriteAllTextAsync(Path.Combine(appDirectory, "Program.vb"), $$"""
            Imports Views

            {{modelClass}}

            Module Program
                Sub Main()
                    Dim v As New TestView()
                    {{modelSetup}}
                    Console.Write(v.Render())
                End Sub
            End Module
            """);

        var build = await new MsBuildBuildService()
            .BuildAsync(Path.Combine(appDirectory, "App.vbproj"));

        var errors = build.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        Assert.True(build.Succeeded,
            $"generated code did not compile:\n{string.Join("\n", errors)}\n\n--- view ---\n{view}");

        return await RunAsync(Path.Combine(appDirectory, "App.vbproj"));
    }

    private static async Task<string> RunAsync(string projectPath)
    {
        var info = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "run", "--project", projectPath, "--no-build" })
            info.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(info)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output;
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (directory is not null && !File.Exists(Path.Combine(directory, "Basalt.slnx")))
            directory = Path.GetDirectoryName(directory);

        return directory ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public async Task RendersMarkupAndAnExpression()
    {
        var output = await RenderAsync(
            "<h1>Hello @Model</h1>",
            modelSetup: "v.Model = \"world\"");

        Assert.Contains("<h1>Hello world</h1>", output);
    }

    [Fact]
    public async Task AnAttributeWhoseValueIsFalseDisappears()
    {
        // The reason this exists: disabled="False" disables the control,
        // because a browser reads the attribute's presence, not its value.
        var output = await RenderAsync(
            "<input disabled=\"@Model\" />",
            modelSetup: "v.Model = False");

        Assert.DoesNotContain("disabled", output);
        Assert.Contains("<input />", output);
    }

    [Fact]
    public async Task AnAttributeWhoseValueIsNothingDisappears()
    {
        var output = await RenderAsync(
            "<input class=\"@Model\" />",
            modelSetup: "v.Model = Nothing");

        Assert.DoesNotContain("class", output);
    }

    [Fact]
    public async Task AnAttributeWhoseValueIsTrueTakesItsOwnName()
    {
        // How disabled="disabled" is written.
        var output = await RenderAsync(
            "<input disabled=\"@Model\" />",
            modelSetup: "v.Model = True");

        Assert.Contains("disabled=\"disabled\"", output);
    }

    [Fact]
    public async Task AnOrdinaryAttributeValueIsWrittenAndEncoded()
    {
        var output = await RenderAsync(
            "<input class=\"@Model\" />",
            modelSetup: "v.Model = \"a\"\"b\"");

        Assert.Contains("class=", output);
        Assert.Contains("&quot;", output);
    }

    [Fact]
    public async Task ADataAttributeKeepsFalse()
    {
        // data-* carries data rather than behaviour: False is a value there,
        // and Razor makes the same exception.
        var output = await RenderAsync(
            "<input data-active=\"@Model\" />",
            modelSetup: "v.Model = False");

        Assert.Contains("data-active=\"False\"", output);
    }

    [Fact]
    public async Task ComposedContentEncodesTheTextAndKeepsTheMarkup()
    {
        // Why IHtmlContent exists. Building this string and calling Raw on it
        // would switch encoding off for the whole thing, script tag included.
        var output = await RenderAsync(
            "<p>@Html.Content().Append(Model).AppendHtml(\"<br>\")</p>",
            modelSetup: "v.Model = \"<script>alert(1)</script>\"");

        Assert.Contains("&lt;script&gt;", output);
        Assert.DoesNotContain("<script>", output);

        // And the part that was meant to be markup still is.
        Assert.Contains("<br>", output);
    }

    [Fact]
    public async Task RawStillWritesMarkupThrough()
    {
        var output = await RenderAsync(
            "<p>@Html.Raw(Model)</p>",
            modelSetup: "v.Model = \"<b>bold</b>\"");

        Assert.Contains("<b>bold</b>", output);
    }

    [Fact]
    public async Task EncodesValuesSoTheyCannotBecomeMarkup()
    {
        // The point of encoding: user data must not turn into tags.
        var output = await RenderAsync(
            "<p>@Model</p>",
            modelSetup: "v.Model = \"<script>alert(1)</script>\"");

        Assert.Contains("&lt;script&gt;", output);
        Assert.DoesNotContain("<script>", output);
    }

    [Fact]
    public async Task RendersAnIfBlockTakingTheTrueBranch()
    {
        var output = await RenderAsync("""
            @ModelType Boolean
            @If Model Then
                <span>yes</span>
            @Else
                <span>no</span>
            @End If
            """,
            modelSetup: "v.Model = True");

        Assert.Contains("<span>yes</span>", output);
        Assert.DoesNotContain("<span>no</span>", output);
    }

    [Fact]
    public async Task RendersAnIfBlockTakingTheElseBranch()
    {
        var output = await RenderAsync("""
            @ModelType Boolean
            @If Model Then
                <span>yes</span>
            @Else
                <span>no</span>
            @End If
            """,
            modelSetup: "v.Model = False");

        Assert.Contains("<span>no</span>", output);
        Assert.DoesNotContain("<span>yes</span>", output);
    }

    [Fact]
    public async Task RendersAForEachOverACollection()
    {
        var output = await RenderAsync("""
            @ModelType String()
            <ul>
            @For Each item In Model
                <li>@item</li>
            @Next
            </ul>
            """,
            modelSetup: "v.Model = New String() {\"a\", \"b\", \"c\"}");

        Assert.Contains("<li>a</li>", output);
        Assert.Contains("<li>b</li>", output);
        Assert.Contains("<li>c</li>", output);
    }

    [Fact]
    public async Task RendersAModelWithProperties()
    {
        var output = await RenderAsync("""
            @ModelType Product
            <h1>@Model.Name</h1>
            <p>@Model.Price.ToString("0.00")</p>
            """,
            modelSetup: "v.Model = New Product With {.Name = \"Keyboard\", .Price = 49.5D}",
            modelClass: """
            Public Class Product
                Public Property Name As String = ""
                Public Property Price As Decimal
            End Class
            """);

        Assert.Contains("<h1>Keyboard</h1>", output);
        Assert.Contains("49", output);
    }

    [Fact]
    public async Task RendersNestedBlocks()
    {
        var output = await RenderAsync("""
            @ModelType Integer()
            @For Each n In Model
                @If n > 1 Then
                    <b>@n</b>
                @End If
            @Next
            """,
            modelSetup: "v.Model = New Integer() {1, 2, 3}");

        Assert.DoesNotContain("<b>1</b>", output);
        Assert.Contains("<b>2</b>", output);
        Assert.Contains("<b>3</b>", output);
    }

    [Fact]
    public async Task RendersCodeBlockStatements()
    {
        var output = await RenderAsync("""
            @Code
                Dim total = 6 * 7
            End Code
            <p>@total</p>
            """,
            modelSetup: "");

        Assert.Contains("<p>42</p>", output);
    }

    [Fact]
    public async Task WritesRawContentUnencoded()
    {
        var output = await RenderAsync(
            "<div>@Html.Raw(Model)</div>",
            modelSetup: "v.Model = \"<em>markup</em>\"");

        Assert.Contains("<em>markup</em>", output);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
