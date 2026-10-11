using System.Diagnostics;
using System.Text;
using Basalt.Core.Model;
using Basalt.Core.Platform;
using Basalt.Designer;
using Basalt.Extensibility;
using Basalt.QuickBasic;
using Basalt.Vb6;
using Basalt.Workspace;
using DiagnosticSeverity = Basalt.Core.Model.DiagnosticSeverity;

namespace Basalt.Tests;

/// <summary>
/// Every project template the New Solution dialog offers produces a project
/// that builds, and the console ones start.
/// </summary>
/// <remarks>
/// No other test builds the templates: SolutionTemplatesTests covers a few of
/// them, and the rest were only ever written as files, so a template that
/// does not compile was found by the user on the first build. Each row takes
/// the path the IDE takes. The project is written by
/// <see cref="SolutionTemplates.CreateAsync"/> and built by the same
/// <see cref="MsBuildBuildService"/>; a Visual Basic 6 project is converted
/// into a .NET one the way the IDE converts a .vbp when it is opened; a
/// QuickBASIC program is compiled by its backend, which is not an MSBuild
/// project.
///
/// Only Visual Basic is generated: <see cref="SolutionTemplates.CreateAsync"/>
/// has no language parameter, so there is no other language to cover.
///
/// Evidence the theory catches the defects it was written for, measured on
/// the template code before the fixes: RazorPages and Mvc failed with
/// BC30526 (Model is ReadOnly) and BC30456 (Render is not a member of the
/// generated view), because the controller or Program.vb drove the view by
/// hand. Both build now. This test only builds them: it does not run them or
/// request a page, so nothing here shows that they answer GET /.
/// VisualBasic6 failed with BC30420 (Sub Main was not found in Form1): the
/// converter named the form as StartupObject, and the converted project
/// referenced neither the runtime nor the generator. It passes now that a
/// startup module is generated and both are referenced.
/// </remarks>
public sealed class TemplateBuildTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-template-build", Guid.NewGuid().ToString("N"));

    public TemplateBuildTests() => Directory.CreateDirectory(_root);

    /// <summary>Every value of the enum, so a template added to it is built without a new row.</summary>
    public static TheoryData<ProjectTemplate> Templates()
    {
        var data = new TheoryData<ProjectTemplate>();

        foreach (var template in Enum.GetValues<ProjectTemplate>())
            data.Add(template);

        return data;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public async Task EveryTemplateBuilds(ProjectTemplate template)
    {
        RequireLocalSources(template);

        var ct = TestContext.Current.CancellationToken;
        var created = await SolutionTemplates.CreateAsync(_root, $"Tpl{template}", template, ct);

        if (template == ProjectTemplate.QuickBasic)
        {
            await CompileAndRunQuickBasicAsync(created.ProjectPath, ct);
            return;
        }

        // The project the IDE builds: a .vbp is converted beside itself when it is opened.
        var projectPath = template == ProjectTemplate.VisualBasic6
            ? ProjectConversion.Convert(created.ProjectPath).ProjectPath
            : created.ProjectPath;

        await BuildAsync(projectPath, ct);

        if (template == ProjectTemplate.ConsoleApp)
        {
            var run = await RunAsync(projectPath, ct);

            Assert.True(run.ExitCode == 0, $"exit code {run.ExitCode}:\n{run.Output}");
            Assert.Contains("Ciao dal progetto", run.Output, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The Razor and WinForms templates reference the Basalt sources (or the
    /// shipped WinForms packages). An installed IDE has no sources, and then
    /// the row says so instead of failing on a project that cannot be restored.
    /// </summary>
    private static void RequireLocalSources(ProjectTemplate template)
    {
        if (template == ProjectTemplate.WinFormsApp)
        {
            Assert.SkipUnless(
                BasaltWinFormsReference.Current().IsAvailable,
                "Basalt.WinForms is not available: no Basalt.Forms sources and no packages on this machine.");
            return;
        }

        if (template is ProjectTemplate.RazorPages or ProjectTemplate.Mvc or ProjectTemplate.Blazor)
        {
            var sources = BasaltSourceRoot.TryFind();

            Assert.SkipUnless(
                sources is not null && Directory.Exists(Path.Combine(sources, "Basalt.Razor.Vb")),
                "The Basalt.Razor.Vb sources were not found (set BASALT_RAZORVB_PATH or run from the repository).");
        }
    }

    /// <summary>
    /// Builds a project with the IDE's build service. A failure reports every
    /// error and the end of the build output, because a bare "did not build"
    /// says nothing about what to fix.
    /// </summary>
    private static async Task BuildAsync(string projectPath, CancellationToken ct)
    {
        var output = new StringBuilder();
        var service = new MsBuildBuildService();
        service.OutputReceived += (_, line) => { lock (output) output.AppendLine(line); };

        var build = await service.BuildAsync(projectPath, ct: ct);

        if (build.Succeeded) return;

        var errors = build.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        string tail;
        lock (output) tail = string.Join('\n', output.ToString().Split('\n').TakeLast(60));

        Assert.Fail(
            $"{Path.GetFileName(projectPath)} did not build:\n" +
            $"{string.Join('\n', errors)}\n--- end of build output ---\n{tail}");
    }

    /// <summary>
    /// Runs a built console project through the same command the IDE's Run
    /// uses, without building it again.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(string projectPath, CancellationToken ct)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "run", "--no-build", "--project", projectPath })
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>
    /// Compiles the QuickBASIC template through the backend the IDE reaches,
    /// then runs the native program and expects its output and a zero exit code.
    /// </summary>
    private async Task CompileAndRunQuickBasicAsync(string sourcePath, CancellationToken ct)
    {
        Clang.SkipUnlessInstalled();

        var backend = new QuickBasicLanguageProvider().Compiler!;
        var output = Path.Combine(_root, Path.GetFileNameWithoutExtension(sourcePath) + ".out");

        var compiled = await backend.CompileAsync(
            sourcePath, new CompilationTarget(HostRuntimeIdentifier) { OutputPath = output }, ct);

        Assert.True(compiled.Succeeded,
            string.Join("\n", compiled.Diagnostics.Select(d => d.Message)));

        using var process = Process.Start(new ProcessStartInfo(output)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;

        var printed = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        Assert.True(process.ExitCode == 0, $"exit code {process.ExitCode}:\n{printed}");

        // 1 + 4 + 9 + 16 + 25.
        Assert.Contains("55", printed, StringComparison.Ordinal);
    }

    private static string HostRuntimeIdentifier =>
        (OperatingSystem.IsMacOS() ? "osx-" : OperatingSystem.IsWindows() ? "win-" : "linux-")
        + (NativeCompiler.HostArchitecture == "arm64" ? "arm64" : "x64");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
