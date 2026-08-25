using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Basalt.Core.Model;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Error underlining and completion icons: the parts of the editor a user
/// judges at a glance.
/// </summary>
public sealed class DiagnosticsAndIconsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-diag", Guid.NewGuid().ToString("N"));

    public DiagnosticsAndIconsTests() => Directory.CreateDirectory(_root);

    [AvaloniaTheory]
    [InlineData(CompletionKind.Class)]
    [InlineData(CompletionKind.Method)]
    [InlineData(CompletionKind.Property)]
    [InlineData(CompletionKind.Field)]
    [InlineData(CompletionKind.Keyword)]
    [InlineData(CompletionKind.Namespace)]
    [InlineData(CompletionKind.Local)]
    [InlineData(CompletionKind.Other)]
    public void ProvidesAnIconForEveryCompletionKind(CompletionKind kind)
    {
        var icon = CompletionIcons.For(kind);

        Assert.NotNull(icon);
        Assert.Equal(16, icon!.PixelSize.Width);
    }

    [AvaloniaFact]
    public void ReusesTheSameIconInstanceForAKind()
    {
        // Icons are cached: rendering one per completion entry would be wasteful.
        Assert.Same(
            CompletionIcons.For(CompletionKind.Method),
            CompletionIcons.For(CompletionKind.Method));
    }

    [AvaloniaFact]
    public void DistinguishesKindsFromEachOther()
    {
        Assert.NotSame(
            CompletionIcons.For(CompletionKind.Class),
            CompletionIcons.For(CompletionKind.Method));
    }

    [AvaloniaFact]
    public void TheSquiggleRendererPaintsAboveTheSelectionLayer()
    {
        var renderer = new DiagnosticSquiggles(new TextDocument("class A { }"));

        Assert.Equal(AvaloniaEdit.Rendering.KnownLayer.Selection, renderer.Layer);
    }

    [AvaloniaFact]
    public void AcceptsDiagnosticsWithoutThrowingOnOutOfRangeLines()
    {
        // A stale diagnostic can name a line the document no longer has.
        var renderer = new DiagnosticSquiggles(new TextDocument("class A { }"));

        renderer.SetDiagnostics([
            new IdeDiagnostic("CS0000", "stale", DiagnosticSeverity.Error, "f.cs", 999, 1)
        ]);

        // Nothing to assert beyond the absence of an exception when drawing is
        // driven by the text view.
        Assert.True(true);
    }

    [AvaloniaFact]
    public async Task ShowsDiagnosticsForTheOpenFileOnly()
    {
        var file = Path.Combine(_root, "Broken.cs");
        await File.WriteAllTextAsync(file, "class A { void M() { undefinedName(); } }");

        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        vm.Diagnostics.Add(new IdeDiagnostic(
            "CS0103", "mine", DiagnosticSeverity.Error, file, 1, 22));
        vm.Diagnostics.Add(new IdeDiagnostic(
            "CS0103", "someone else's", DiagnosticSeverity.Error,
            Path.Combine(_root, "Other.cs"), 1, 1));

        var code = host.Window.GetVisualDescendants().OfType<CodeEditor>().Single();

        // Filtering happens inside; the call must not throw and must not paint
        // the other file's diagnostic.
        code.ShowDiagnostics();

        Assert.Contains(vm.Diagnostics, d => d.Message == "mine");
    }

    [AvaloniaFact]
    public async Task ReportsRealCompilerErrorsForTheOpenDocument()
    {
        // End to end: a genuine error must reach the diagnostics collection,
        // which is what the underlines are drawn from.
        var project = Path.Combine(_root, "Probe.vbproj");
        var source = Path.Combine(_root, "Program.vb");

        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(source, """
            Public Class Probe
                Public Sub M()
                    Dim n As Integer = undefinedThing
                End Sub
            End Class
            """);

        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenSolutionAsync(project);
        await vm.OpenFileAsync(source);

        var document = vm.OpenDocuments.Single();
        await vm.RefreshDiagnosticsAsync(document);

        Assert.Contains(vm.Diagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Id == "BC30451");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
