using Avalonia.Headless.XUnit;
using Basalt.Designer.Toolbox;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Designer edits reaching the file.
/// </summary>
/// <remarks>
/// They did not. The session held the XAML, the preview showed the controls,
/// and the document knew nothing: the tab showed no dot, Save wrote the old
/// text, and closing the file threw the work away without a word.
/// </remarks>
public sealed class DesignerSavingTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-designer-save-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string WriteWindow()
    {
        var path = Path.Combine(_root, "MainWindow.axaml");

        File.WriteAllText(path, """
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Canvas />
            </Window>
            """);

        return path;
    }

    private static ToolboxItem Button() =>
        new("Button", "Button", "Common", "<Button Content=\"New\" />");

    [AvaloniaFact]
    public async Task AddingAControlMarksTheDocumentModified()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;
        var path = WriteWindow();

        await vm.OpenFileAsync(path, inDesigner: true);

        var document = vm.OpenDocuments.Single();
        var session = vm.DesignerSessionFor(document)!;

        Assert.False(document.IsModified);

        session.InsertFromToolbox(Button());

        // The dot on the tab is the only warning that closing loses work.
        Assert.True(document.IsModified, "the tab would show no unsaved changes");
    }

    [AvaloniaFact]
    public async Task WhatWasDrawnIsWhatGetsSaved()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;
        var path = WriteWindow();

        await vm.OpenFileAsync(path, inDesigner: true);

        var document = vm.OpenDocuments.Single();
        var session = vm.DesignerSessionFor(document)!;

        session.InsertFromToolbox(Button());

        await document.SaveAsync();

        Assert.Contains("<Button", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task MovingAControlIsSavedToo()
    {
        // Not only insertions: a property written by dragging is an edit like
        // any other, and it went the same way.
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;
        var path = WriteWindow();

        await vm.OpenFileAsync(path, inDesigner: true);

        var document = vm.OpenDocuments.Single();
        var session = vm.DesignerSessionFor(document)!;

        var button = session.InsertFromToolbox(Button());

        session.Select(button);
        session.MoveSelection(30, 40);

        await document.SaveAsync();

        var saved = File.ReadAllText(path);

        Assert.Contains("30", saved, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task UndoReachesTheDocumentAsWell()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;
        var path = WriteWindow();

        await vm.OpenFileAsync(path, inDesigner: true);

        var document = vm.OpenDocuments.Single();
        var session = vm.DesignerSessionFor(document)!;

        session.InsertFromToolbox(Button());
        session.Undo();

        Assert.DoesNotContain("<Button", document.Text, StringComparison.Ordinal);
    }
}
