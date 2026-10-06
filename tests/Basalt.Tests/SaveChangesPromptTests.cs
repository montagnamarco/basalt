using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Shell;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// "Save changes to X?" when a dirty document is about to be lost.
///
/// Before <c>RoslynLanguageService.UpdateDocumentAsync</c> stopped writing
/// every pause in typing to disk, nothing ever asked: closing a tab simply
/// discarded whatever was not yet reflected on disk at the moment MSBuild's
/// workspace last wrote it. Now the editor keeps text in memory only, so
/// every way of making a dirty tab go away has to ask Save / Don't Save /
/// Cancel first.
/// </summary>
public sealed class SaveChangesPromptTests
{
    private static string WriteProject(string root)
    {
        var projectPath = Path.Combine(root, "P.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        return projectPath;
    }

    /// <summary>
    /// Closing a dirty tab through the dock (its own close button and
    /// friends all end up here) asks, and Save writes the file before the
    /// tab goes away.
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingADirtyTabAsksAndSaveWritesTheFile()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-save-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        var saved = "Module M\n    Sub Main()\n    End Sub\nEnd Module\n";
        File.WriteAllText(file, saved);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text += "\n' edited\n";
        Assert.True(vm.ActiveDocument!.IsModified);
        var editedText = vm.ActiveDocument!.Text;

        var dockable = DocumentDockableFor(w, vm.ActiveDocument!);
        CloseOnUiThread(w, dockable);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ClickButton(dialog!, b => b.Classes.Contains("primary")); // Save

        await host.SettleAsync(10);

        Assert.Empty(vm.OpenDocuments);

        // The tab is gone by now, so this has to compare against the text
        // captured before closing rather than vm.ActiveDocument (null),
        // otherwise it would compare the file on disk with itself.
        Assert.Equal(editedText, File.ReadAllText(file));
        Assert.Contains("edited", File.ReadAllText(file));
    }

    /// <summary>
    /// Don't Save closes the tab, leaves the file on disk untouched, and
    /// makes the language service forget the discarded edit: a diagnostic
    /// that only existed in the text thrown away must not survive the close.
    /// </summary>
    [AvaloniaFact]
    public async Task DontSaveLeavesTheFileAloneAndForgetsTheEditInTheLanguageService()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-dontsave-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        var saved = "Module M\n    Sub Main()\n    End Sub\nEnd Module\n";
        File.WriteAllText(file, saved);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        // The window's own language service, not a second instance of our
        // own: a separate one could be made to answer anything, proving
        // nothing about what the close actually did to the one the IDE
        // keeps.
        var languageService = LanguageServiceFor(vm);

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        // An edit that uses a name nobody declared, pushed into the window's
        // own language service the same way its usual debounced refresh
        // would, but synchronously, so the diagnostic is there to lose.
        var dirty = saved.Replace("    End Sub", "        Undeclared()\n    End Sub");
        vm.ActiveDocument!.Text = dirty;
        await languageService.UpdateDocumentAsync(file, dirty);
        Assert.Contains(await languageService.GetDiagnosticsAsync(file), d => d.Id == "BC30451");

        var dockable = DocumentDockableFor(w, vm.ActiveDocument!);
        CloseOnUiThread(w, dockable);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ClickButton(dialog!, b => !b.Classes.Contains("primary") && !b.IsCancel); // Don't Save

        await host.SettleAsync(10);

        Assert.Empty(vm.OpenDocuments);
        Assert.Equal(saved, File.ReadAllText(file));

        // The close itself (MainWindow.ConfirmAndCloseDirtyDocumentsAsync)
        // awaits ForgetDiscardedEditAsync before the batch is done, so by
        // now the window's own language service must already have forgotten
        // the discarded edit -- nothing in this test calls that method
        // itself, unlike before.
        Assert.DoesNotContain(await languageService.GetDiagnosticsAsync(file), d => d.Id == "BC30451");
    }

    private static RoslynLanguageService LanguageServiceFor(MainWindowViewModel vm) =>
        (RoslynLanguageService)typeof(MainWindowViewModel)
            .GetField("_languageService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(vm)!;

    /// <summary>
    /// Ctrl+W / Cmd+W — IdeCommands.FileClose, through the shortcut a user
    /// actually presses rather than by calling the view model directly —
    /// asks before losing a dirty tab, exactly as the tab's own close
    /// button does.
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlWAsksBeforeClosingADirtyDocumentAndSaveWritesTheFile()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-ctrlw-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        var saved = "Module M\nEnd Module\n";
        File.WriteAllText(file, saved);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text += "\n' edited\n";
        var editedText = vm.ActiveDocument!.Text;

        w.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.W,
            KeyModifiers = KeyModifiers.Control,
            Source = w
        });

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ClickButton(dialog!, b => b.Classes.Contains("primary")); // Save

        await host.SettleAsync(10);

        Assert.Empty(vm.OpenDocuments);
        Assert.Equal(editedText, File.ReadAllText(file));
    }

    /// <summary>
    /// A save that fails during a dirty close — a read-only file, say —
    /// must not lose the document, close the tab or quit the window: only
    /// the write itself failed, so the choice to save still stands.
    /// </summary>
    [AvaloniaFact]
    public async Task AFailedSaveKeepsTheDocumentOpenAndReportsTheError()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-readonly-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        var saved = "Module M\nEnd Module\n";
        File.WriteAllText(file, saved);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text += "\n' edited\n";
        var editedText = vm.ActiveDocument!.Text;

        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var dockable = DocumentDockableFor(w, vm.ActiveDocument!);
            CloseOnUiThread(w, dockable);

            var saveDialog = await WaitForDialogAsync(host);
            Assert.NotNull(saveDialog);

            ClickButton(saveDialog!, b => b.Classes.Contains("primary")); // Save

            // The save itself fails and a second dialog reports it, not the
            // Save/Don't Save/Cancel one that already closed.
            Window? errorDialog = null;
            for (var i = 0; i < 60 && errorDialog is null; i++)
            {
                await host.SettleAsync(1);
                errorDialog = w.OwnedWindows.FirstOrDefault(d => !ReferenceEquals(d, saveDialog));
            }

            Assert.NotNull(errorDialog);
            ClickButton(errorDialog!, _ => true);

            await host.SettleAsync(10);

            Assert.Single(vm.OpenDocuments);
            Assert.Equal(editedText, vm.OpenDocuments[0].Text);
            Assert.True(vm.OpenDocuments[0].IsModified);
            Assert.Equal(saved, File.ReadAllText(file));
            Assert.True(w.IsVisible);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    /// <summary>Cancel leaves the tab exactly as it was, text and all.</summary>
    [AvaloniaFact]
    public async Task CancelKeepsTheTabAndItsText()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-cancel-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        File.WriteAllText(file, "Module M\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text += "\n' edited\n";
        var editedText = vm.ActiveDocument!.Text;

        var dockable = DocumentDockableFor(w, vm.ActiveDocument!);
        CloseOnUiThread(w, dockable);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ClickButton(dialog!, b => b.IsCancel);

        await host.SettleAsync(10);

        Assert.Single(vm.OpenDocuments);
        Assert.Equal(editedText, vm.OpenDocuments[0].Text);
        Assert.True(vm.OpenDocuments[0].IsModified);
    }

    /// <summary>A clean tab closes at once, with no dialog at all.</summary>
    [AvaloniaFact]
    public async Task ACleanTabClosesWithNoDialog()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-clean-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        File.WriteAllText(file, "Module M\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        Assert.False(vm.ActiveDocument!.IsModified);

        var dockable = DocumentDockableFor(w, vm.ActiveDocument!);
        CloseOnUiThread(w, dockable);

        await host.SettleAsync(10);

        Assert.Empty(vm.OpenDocuments);
        Assert.Null(w.OwnedWindows.FirstOrDefault());
    }

    /// <summary>
    /// Window closing with a dirty document asks; Cancel keeps the window
    /// open.
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingTheWindowWithADirtyDocumentAsksAndCancelKeepsItOpen()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-quit-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        File.WriteAllText(file, "Module M\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text += "\n' edited\n";

        w.Close();
        await host.SettleAsync(5);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ClickButton(dialog!, b => b.IsCancel);

        await host.SettleAsync(10);

        Assert.True(w.IsVisible, "the window closed despite cancelling");
        Assert.Single(vm.OpenDocuments);
    }

    /// <summary>
    /// Opening another solution would close every document of this one: it
    /// asks first, and Cancel leaves the current solution where it was.
    /// </summary>
    [AvaloniaFact]
    public async Task OpeningAnotherSolutionWithADirtyDocumentAsksAndCancelStays()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-switch-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        File.WriteAllText(file, "Module M\nEnd Module\n");

        var otherRoot = Directory.CreateTempSubdirectory("basalt-saveprompt-other-").FullName;
        var other = WriteProject(otherRoot);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var before = vm.SolutionPath;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text += "\n' edited\n";

        // The same method the Open dialog, the recent list and the welcome
        // page call once a path is chosen.
        var opening = (Task)typeof(MainWindow)
            .GetMethod("OpenSolutionAskingToSaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(w, [other])!;

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ClickButton(dialog!, b => b.IsCancel);
        await opening;
        await host.SettleAsync(5);

        Assert.Equal(before, vm.SolutionPath);
        Assert.Single(vm.OpenDocuments);
        Assert.True(vm.OpenDocuments[0].IsModified);
    }

    /// <summary>
    /// Close All with two dirty documents asks once, in a single dialog
    /// that lists both — not one dialog per tab.
    /// </summary>
    [AvaloniaFact]
    public async Task CloseAllWithTwoDirtyDocumentsAsksOnceForBoth()
    {
        var root = Directory.CreateTempSubdirectory("basalt-saveprompt-closeall-").FullName;
        var project = WriteProject(root);
        var fileA = Path.Combine(root, "A.vb");
        var fileB = Path.Combine(root, "B.vb");
        File.WriteAllText(fileA, "Module A\nEnd Module\n");
        File.WriteAllText(fileB, "Module B\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(fileA);
        await host.SettleAsync(3);
        await vm.OpenFileAsync(fileB);
        await host.SettleAsync(5);

        vm.OpenDocuments[0].Text += "\n' edited\n";
        vm.OpenDocuments[1].Text += "\n' edited\n";

        var dockableB = DocumentDockableFor(w, vm.OpenDocuments[1]);
        CloseAllOnUiThread(w, dockableB);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var listed = dialog!.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();
        Assert.NotNull(listed);
        Assert.Equal(2, ((IEnumerable<string>)listed!.ItemsSource!).Count());

        ClickButton(dialog, b => !b.Classes.Contains("primary") && !b.IsCancel); // Don't Save both

        await host.SettleAsync(10);

        Assert.Empty(vm.OpenDocuments);
    }

    /// <summary>
    /// "New Solution" collects a name and a location, then asks whether the
    /// current solution's dirty tab may be left before anything is written
    /// to disk. Cancelling that question must leave no trace of the
    /// solution the dialog described — not generate it first and ask
    /// afterwards, which left an unopened solution on disk whenever the
    /// question was answered "no" (critical-review finding).
    /// </summary>
    [AvaloniaFact]
    public async Task CancellingTheSaveChangesPromptLeavesNoNewSolutionOnDisk()
    {
        var root = Directory.CreateTempSubdirectory("basalt-newsolution-order-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "M.vb");
        File.WriteAllText(file, "Module M\nEnd Module\n");

        var parent = Directory.CreateTempSubdirectory("basalt-newsolution-order-parent-").FullName;
        var newSolutionFolder = Path.Combine(parent, "NewApp");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);
        vm.ActiveDocument!.Text += "\n' edited, unsaved\n";

        var method = typeof(MainWindow).GetMethod(
            "ShowNewSolutionDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(w, [])!;

        var newSolutionDialog = await WaitForDialogAsync(host);
        Assert.NotNull(newSolutionDialog);

        var boxes = newSolutionDialog!.GetVisualDescendants().OfType<TextBox>().ToList();
        boxes.First(b => b.Name == "NameBox").Text = "NewApp";
        boxes.First(b => b.Name == "PathBox").Text = parent;

        ClickButton(newSolutionDialog, b => b.Classes.Contains("primary")); // Create

        // The question about leaving the current, dirty solution — Save,
        // Don't Save or Cancel — only now, with nothing created yet.
        var saveDialog = await WaitForDialogAsync(host);
        Assert.NotNull(saveDialog);
        ClickButton(saveDialog!, b => b.IsCancel);

        await task;
        await host.SettleAsync(10);

        Assert.False(Directory.Exists(newSolutionFolder),
            "the new solution was generated on disk despite cancelling the save-changes prompt");
        Assert.Equal(project, vm.SolutionPath);
    }

    private static object DocumentDockableFor(MainWindow window, EditorDocumentViewModel document)
    {
        var field = typeof(MainWindow).GetField("_documents", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var map = (System.Collections.IDictionary)field.GetValue(window)!;
        return map[document]!;
    }

    // The test itself already runs on Avalonia's own dispatcher thread
    // (AvaloniaFact), the same one every one of the window's own handlers
    // runs on, so these call straight through: no further marshalling.
    private static void CloseOnUiThread(MainWindow window, object dockable)
    {
        var factory = Factory(window);
        factory.GetType().GetMethod("CloseDockable", [typeof(Dock.Model.Core.IDockable)])!
            .Invoke(factory, [dockable]);
    }

    private static void CloseAllOnUiThread(MainWindow window, object dockable)
    {
        var factory = Factory(window);
        factory.GetType().GetMethod("CloseAllDockables", [typeof(Dock.Model.Core.IDockable)])!
            .Invoke(factory, [dockable]);
    }

    private static object Factory(MainWindow window) =>
        typeof(MainWindow).GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static void ClickButton(Window dialog, Func<Button, bool> choose)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(choose);
        button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task<Window?> WaitForDialogAsync(TestWindow host)
    {
        Window? dialog = null;

        for (var i = 0; i < 60 && dialog is null; i++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }
}
