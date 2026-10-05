using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Refactoring;
using Microsoft.CodeAnalysis.CodeActions;

namespace Basalt.Tests;

public sealed class EditorSmartTagTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basalt-smart-tag-").FullName;

    [AvaloniaFact]
    public async Task RefactoringPreviewComputationRunsOffTheInterfaceThreadForEveryCaller()
    {
        // Rename, Extract Variable and Extract Method started from a menu or a
        // key used to call the preview directly: whatever Roslyn work happens
        // before its first await then ran on the interface thread.
        var helper = typeof(Basalt.Shell.MainWindow).GetMethod("PreviewOffUiThreadAsync",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var uiThread = Environment.CurrentManagedThreadId;
        var previewThread = -1;

        var task = Assert.IsAssignableFrom<Task<RefactoringPreview>>(helper.Invoke(null,
        [
            new Func<Task<RefactoringPreview>>(() =>
            {
                previewThread = Environment.CurrentManagedThreadId;
                return Task.FromResult(new RefactoringPreview("probe", []));
            })
        ]));
        await task;

        Assert.NotEqual(uiThread, previewThread);
    }

    [AvaloniaFact]
    public async Task RealRoslynFixAppearsAsARasterizedBulbAndMenuAction()
    {
        const string source = "Public Class Probe\nPublic Sub M()\nDim value As New StringBuilder()\nEnd Sub\nEnd Class";
        var project = Path.Combine(_directory, "Probe.vbproj");
        var path = Path.Combine(_directory, "Probe.vb");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(path, source);
        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(project);
        var document = new EditorDocumentViewModel(path, source)
        {
            CaretOffset = source.IndexOf("StringBuilder", StringComparison.Ordinal) + 2
        };
        vm.OpenDocuments.Add(document);
        var code = new CodeEditor(document, vm) { AutoFormatWhileTyping = false };
        var window = new Window { Content = code, Width = 700, Height = 400 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => code.SmartTagMargin.AvailableLine > 0 && code.SmartTagMargin.IsVisible);
            window.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize(18, 400));
            bitmap.Render(code.SmartTagMargin);
            var bytes = new byte[18 * 400 * 4];
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, 18, 400), pin.AddrOfPinnedObject(), bytes.Length, 18 * 4);
            }
            finally
            {
                pin.Free();
            }

            Assert.Contains(Enumerable.Range(0, 18 * 400), pixel =>
                bytes[pixel * 4] == 255 && bytes[pixel * 4 + 1] == 205 && bytes[pixel * 4 + 2] == 55);
            var editor = code.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
            Assert.DoesNotContain(code.SmartTagMargin, editor.TextArea.LeftMargins);
            Assert.Equal(source.IndexOf("StringBuilder", StringComparison.Ordinal), code.SmartTagMargin.AnchorOffset);
            var view = editor.TextArea.TextView;
            var expected = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(
                editor.Document.GetLocation(code.SmartTagMargin.AnchorOffset)),
                AvaloniaEdit.Rendering.VisualYPosition.LineBottom) - view.ScrollOffset;
            var actual = code.SmartTagMargin.TranslatePoint(default, view)!.Value;
            Assert.InRange(Math.Abs(expected.X - actual.X), 0, 1);
            Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 1);
            var borderPixel = (8 * 18) * 4;
            Assert.Equal((byte)184, bytes[borderPixel]);
            Assert.Equal((byte)134, bytes[borderPixel + 1]);
            Assert.Equal((byte)11, bytes[borderPixel + 2]);
            await code.ShowSmartTagsAsync();
            Assert.Contains(code.SmartTagMargin.ContextMenu!.Items.OfType<MenuItem>(),
                item => item.Header?.ToString()?.Contains("System.Text", StringComparison.Ordinal) == true);
            Assert.Equal(source, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InlineBulbFollowsScrollingAndHidesOutsideTheTextViewport()
    {
        var source = "Class Probe\n" + new string(' ', 40) + "UnknownType ' " + new string('x', 200) + "\n" +
            string.Join("\n", Enumerable.Repeat("' Padding", 60)) + "\nEnd Class";
        var anchor = source.IndexOf("UnknownType", StringComparison.Ordinal);
        using var vm = new MainWindowViewModel();
        var document = new EditorDocumentViewModel("Probe.vb", source) { CaretOffset = anchor + 2 };
        var action = new QuickAction("Test fix", CodeAction.Create("Test fix",
            _ => Task.FromResult<Microsoft.CodeAnalysis.Document>(null!)))
        {
            ApplicableSpan = new Microsoft.CodeAnalysis.Text.TextSpan(anchor, 11)
        };
        var code = new CodeEditor(document, vm)
        {
            AutoFormatWhileTyping = false,
            SmartTagProvider = (_, _, _, _) => Task.FromResult<IReadOnlyList<QuickAction>>([action])
        };
        var owner = new Window { Content = code, Width = 500, Height = 200 };
        owner.Show();
        try
        {
            await WaitUntilAsync(() => code.SmartTagMargin.IsVisible);
            var editor = code.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
            editor.WordWrap = false;
            owner.UpdateLayout();
            var initialX = Canvas.GetLeft(code.SmartTagMargin);
            editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            using var raster = new RenderTargetBitmap(new PixelSize(500, 200));
            raster.Render(owner);
            owner.UpdateLayout();
            var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroller.Offset = new Vector(50, 0);
            owner.UpdateLayout();
            await WaitUntilAsync(() => Canvas.GetLeft(code.SmartTagMargin) < initialX - 40);
            scroller.Offset = new Vector(50, 600);
            owner.UpdateLayout();
            await WaitUntilAsync(() => !code.SmartTagMargin.IsVisible);
            scroller.Offset = default;
            owner.UpdateLayout();
            await WaitUntilAsync(() => code.SmartTagMargin.IsVisible);
            Assert.InRange(Math.Abs(Canvas.GetLeft(code.SmartTagMargin) - initialX), 0, 1);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task LateDiscoveryCannotRestoreABulbAfterSourceChangesOrDetach()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<QuickAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = false;
        var token = CancellationToken.None;
        using var vm = new MainWindowViewModel();
        var document = new EditorDocumentViewModel("Probe.vb", "Class Probe\nEnd Class");
        var code = new CodeEditor(document, vm)
        {
            AutoFormatWhileTyping = false,
            SmartTagProvider = (_, _, _, cancellation) =>
            {
                requested = true;
                token = cancellation;
                return completion.Task;
            }
        };
        var window = new Window { Content = code, Width = 500, Height = 300 };
        window.Show();
        await WaitUntilAsync(() => requested);
        document.Text += "\n' new source";
        Assert.True(token.IsCancellationRequested);
        window.Close();
        completion.SetResult([new QuickAction("Late action", CodeAction.Create("Late action",
            _ => Task.FromResult<Microsoft.CodeAnalysis.Document>(null!)))]);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, code.SmartTagMargin.AvailableLine);
    }

    [AvaloniaFact]
    public async Task PreviewSnapshotRejectsNewerUnsavedEditsAndNewlyAppearedFiles()
    {
        var path = Path.Combine(_directory, "Probe.vb");
        await File.WriteAllTextAsync(path, "disk original");
        var document = new EditorDocumentViewModel(path, "unsaved original");
        var documents = new ObservableCollection<EditorDocumentViewModel> { document };
        var preview = new RefactoringPreview("Fix", [new(path, document.Text, "fixed")]);
        var transaction = await EditorSmartTagTransaction.CaptureAsync(preview, documents);
        document.Text = "later user edit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ApplyAsync());
        Assert.Equal("later user edit", document.Text);
        Assert.Equal("disk original", await File.ReadAllTextAsync(path));

        var added = Path.Combine(_directory, "Added.vb");
        var addition = await EditorSmartTagTransaction.CaptureAsync(
            new RefactoringPreview("Add", [new(added, "", "new member")]), documents);
        await File.WriteAllTextAsync(added, "external file");
        await Assert.ThrowsAsync<InvalidOperationException>(() => addition.ApplyAsync());
        Assert.Single(documents);
        Assert.Equal("external file", await File.ReadAllTextAsync(added));
    }

    [AvaloniaFact]
    public async Task MultiFileActionChangesBuffersAndLeavesDiskForSave()
    {
        var firstPath = Path.Combine(_directory, "First.vb");
        var secondPath = Path.Combine(_directory, "Second.vb");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        var first = new EditorDocumentViewModel(firstPath, "first");
        var documents = new ObservableCollection<EditorDocumentViewModel> { first };
        var transaction = await EditorSmartTagTransaction.CaptureAsync(new RefactoringPreview("Fix",
            [new(firstPath, "first", "first fixed"), new(secondPath, "second", "second fixed")]), documents);
        await transaction.ApplyAsync();
        Assert.Equal("first fixed", first.Text);
        Assert.All(documents, document => Assert.True(document.IsModified));
        Assert.Equal("second fixed", documents.Single(document => document.FilePath == secondPath).Text);
        Assert.Equal("first", await File.ReadAllTextAsync(firstPath));
        Assert.Equal("second", await File.ReadAllTextAsync(secondPath));
    }

    [AvaloniaFact]
    public async Task ObserverFailureRollsBackChangedBuffersAndCreatedDocuments()
    {
        var path = Path.Combine(_directory, "First.vb");
        await File.WriteAllTextAsync(path, "first");
        var first = new EditorDocumentViewModel(path, "first");
        var documents = new ObservableCollection<EditorDocumentViewModel> { first };
        var transaction = await EditorSmartTagTransaction.CaptureAsync(new RefactoringPreview("Fix",
            [new(path, "first", "fixed"), new(Path.Combine(_directory, "Added.vb"), "", "added")]), documents);
        documents.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                throw new InvalidOperationException("Observer failed");
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ApplyAsync());
        Assert.Same(first, Assert.Single(documents));
        Assert.Equal("first", first.Text);
        Assert.False(first.IsModified);
        Assert.Equal("first", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CapturedRoslynActionDoesNotRevertAnUnrelatedWorkspaceEdit()
    {
        const string source = "Public Class Probe\nPublic Sub M()\nDim value As New StringBuilder()\nEnd Sub\nEnd Class";
        var project = Path.Combine(_directory, "Probe.vbproj");
        var first = Path.Combine(_directory, "First.vb");
        var second = Path.Combine(_directory, "Second.vb");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(first, source);
        await File.WriteAllTextAsync(second, "Public Class Other\nEnd Class");
        using var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(project);
        var actions = await service.GetQuickActionsAsync(first, source,
            source.IndexOf("StringBuilder", StringComparison.Ordinal) + 2);
        var action = actions.First(item => item.Title.Contains("System.Text", StringComparison.Ordinal));
        await service.UpdateDocumentAsync(second, "Public Class Other\nPublic Value As Integer\nEnd Class");
        var preview = await service.PreviewQuickActionAsync(first, action);
        Assert.True(preview.CanApply);
        Assert.Equal(first, Assert.Single(preview.Changes).FilePath);
        Assert.Equal(source, preview.Changes[0].OriginalText);

        var baseline = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Solution>(action.OriginalSolution);
        var original = baseline.Projects.SelectMany(item => item.Documents).Single(item => item.FilePath == first);
        var changed = baseline.WithDocumentText(original.Id, Microsoft.CodeAnalysis.Text.SourceText.From(source + "\n' fix"));
        changed = changed.AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(original.Project.Id),
            "Added.vb", Microsoft.CodeAnalysis.Text.SourceText.From("Public Class Added\nEnd Class"), filePath: Path.Combine(_directory, "Added.vb"));
        var structuralAction = new QuickAction("Add file and edit source",
            CodeAction.Create("Add file and edit source", _ => Task.FromResult(changed)))
        {
            OriginalSolution = baseline
        };
        var refused = await service.PreviewQuickActionAsync(first, structuralAction);
        Assert.False(refused.CanApply);
        Assert.Empty(refused.Changes);
    }

    [AvaloniaFact]
    public async Task SelectionChangeAtTheSameCaretInvalidatesDiscoveryAndOffersExtractCommands()
    {
        const string source = "Public Class Probe\nPublic Sub M()\nDim value As Integer = 1 + 2\nEnd Sub\nEnd Class";
        var project = Path.Combine(_directory, "Probe.vbproj");
        var path = Path.Combine(_directory, "Probe.vb");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(path, source);
        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(project);
        var document = new EditorDocumentViewModel(path, source)
        {
            CaretOffset = source.IndexOf("1 + 2", StringComparison.Ordinal) + 5
        };
        vm.OpenDocuments.Add(document);
        CancellationToken firstToken = default;
        var requested = false;
        var code = new CodeEditor(document, vm)
        {
            AutoFormatWhileTyping = false,
            SmartTagProvider = (_, _, _, cancellation) =>
            {
                if (!requested)
                {
                    firstToken = cancellation;
                    requested = true;
                }
                return Task.FromResult<IReadOnlyList<QuickAction>>([]);
            }
        };
        var window = new Window { Content = code, Width = 600, Height = 350 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => requested);
            var editor = Assert.Single(code.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>());
            var caret = editor.CaretOffset;
            editor.Select(caret - 5, 5);
            Assert.Equal(caret, editor.CaretOffset);
            Assert.True(firstToken.IsCancellationRequested);
            await code.ShowSmartTagsAsync();
            var items = code.SmartTagMargin.ContextMenu!.Items.OfType<MenuItem>().ToArray();
            Assert.Contains(items, item => Equals(item.Header, Basalt.Core.Localization.Localizer.Get("Editor_SmartTagExtractVariable")));
            Assert.Contains(items, item => Equals(item.Header, Basalt.Core.Localization.Localizer.Get("Editor_SmartTagExtractMethod")));

            editor.Select(source.IndexOf("value", StringComparison.Ordinal), 0);
            await code.ShowSmartTagsAsync();
            Assert.Contains(code.SmartTagMargin.ContextMenu!.Items.OfType<MenuItem>(),
                item => Equals(item.Header, Basalt.Core.Localization.Localizer.Get("Editor_SmartTagRename")));
            Assert.Equal(source, document.Text);
            Assert.Equal(0, code.SmartTagMargin.AvailableLine);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnAttachedEditorCanUndoAnAppliedBufferChange()
    {
        var path = Path.Combine(_directory, "Undo.vb");
        await File.WriteAllTextAsync(path, "Class Original\nEnd Class");
        var document = new EditorDocumentViewModel(path, "Class Original\nEnd Class");
        using var vm = new MainWindowViewModel();
        vm.OpenDocuments.Add(document);
        var code = new CodeEditor(document, vm) { AutoFormatWhileTyping = false };
        var window = new Window { Content = code, Width = 500, Height = 300 };
        window.Show();
        try
        {
            var transaction = await EditorSmartTagTransaction.CaptureAsync(new RefactoringPreview("Rename",
                [new(path, document.Text, "Class Renamed\nEnd Class")]), vm.OpenDocuments);
            await transaction.ApplyAsync();
            var editor = Assert.Single(code.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>());
            Assert.True(editor.CanUndo);
            editor.Undo();
            Assert.Equal("Class Original\nEnd Class", document.Text);
            Assert.Equal(document.Text, await File.ReadAllTextAsync(path));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RollbackContinuesAfterAnotherObserverThrowsDuringRestore()
    {
        var firstPath = Path.Combine(_directory, "First.vb");
        var secondPath = Path.Combine(_directory, "Second.vb");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        var first = new EditorDocumentViewModel(firstPath, "first");
        var second = new EditorDocumentViewModel(secondPath, "second");
        var documents = new ObservableCollection<EditorDocumentViewModel> { first, second };
        var transaction = await EditorSmartTagTransaction.CaptureAsync(new RefactoringPreview("Fix",
            [new(firstPath, "first", "first fixed"), new(secondPath, "second", "second fixed"),
                new(Path.Combine(_directory, "Added.vb"), "", "added")]), documents);
        second.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorDocumentViewModel.Text) && second.Text == "second")
            {
                throw new InvalidOperationException("Restore observer failed");
            }
        };
        documents.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                throw new InvalidOperationException("Apply observer failed");
            }
        };
        var failure = await Assert.ThrowsAsync<AggregateException>(() => transaction.ApplyAsync());
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal("first", first.Text);
        Assert.Equal("second", second.Text);
        Assert.False(first.IsModified);
        Assert.False(second.IsModified);
        Assert.Equal(2, documents.Count);
    }

    [AvaloniaFact]
    public async Task AcceptingTheActualPreviewCannotOverwriteAnEditMadeWhileItWasOpen()
    {
        var path = Path.Combine(_directory, "Preview.vb");
        await File.WriteAllTextAsync(path, "Class Original\nEnd Class");
        using var owner = new TestWindow();
        var document = new EditorDocumentViewModel(path, "Class Original\nEnd Class");
        var vm = Assert.IsType<MainWindowViewModel>(owner.Window.DataContext);
        vm.OpenDocuments.Add(document);
        vm.ActiveDocument = document;
        var preview = new RefactoringPreview("SmartTag preview",
            [new(path, document.Text, "Class Fixed\nEnd Class")]);
        var apply = typeof(Basalt.Shell.MainWindow).GetMethod("ApplySmartTagPreviewAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var pending = (Task)apply.Invoke(owner.Window, [preview, (Func<bool>)(() => true)])!;
        await WaitUntilAsync(() => owner.Window.OwnedWindows.OfType<Basalt.Shell.RefactoringPreviewDialog>().Any());
        var dialog = Assert.Single(owner.Window.OwnedWindows.OfType<Basalt.Shell.RefactoringPreviewDialog>());
        try
        {
            document.Text = "Class LaterEdit\nEnd Class";
            var accept = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => button.IsDefault);
            accept.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            Assert.Equal("Class LaterEdit\nEnd Class", document.Text);
            Assert.Equal("Class Original\nEnd Class", await File.ReadAllTextAsync(path));
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public async Task SmartTagEditOnAClosedFileGetsATab()
    {
        var path = Path.Combine(_directory, "Closed.vb");
        await File.WriteAllTextAsync(path, "Class Original\nEnd Class");
        using var owner = new TestWindow();
        await owner.SettleAsync();
        var vm = Assert.IsType<MainWindowViewModel>(owner.Window.DataContext);
        var preview = new RefactoringPreview("SmartTag preview",
            [new(path, "Class Original\nEnd Class", "Class Fixed\nEnd Class")]);
        var apply = typeof(Basalt.Shell.MainWindow).GetMethod("ApplySmartTagPreviewAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var pending = (Task)apply.Invoke(owner.Window, [preview, (Func<bool>)(() => true)])!;
        await WaitUntilAsync(() => owner.Window.OwnedWindows.OfType<Basalt.Shell.RefactoringPreviewDialog>().Any());
        var dialog = Assert.Single(owner.Window.OwnedWindows.OfType<Basalt.Shell.RefactoringPreviewDialog>());
        var accept = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => button.IsDefault);
        accept.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await pending;

        // The file was closed before the fix was applied: nothing but the
        // transaction itself put its document into OpenDocuments. Without a
        // tab, that edit is invisible and lost unnoticed when the IDE exits.
        var document = Assert.Single(vm.OpenDocuments, d => d.FilePath == path);
        Assert.True(document.IsModified);

        var documentsField = typeof(Basalt.Shell.MainWindow).GetField("_documents",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var documents = Assert.IsAssignableFrom<System.Collections.IDictionary>(documentsField.GetValue(owner.Window));
        Assert.True(documents.Contains(document));
    }

    [AvaloniaFact]
    public async Task MarginRequestFailureIsReportedNotThrown()
    {
        var document = new EditorDocumentViewModel("Probe.vb", "Class Probe\nEnd Class");
        using var vm = new MainWindowViewModel();
        var discovered = false;
        var code = new CodeEditor(document, vm)
        {
            AutoFormatWhileTyping = false,
            SmartTagProvider = (_, _, _, _) =>
            {
                discovered = true;
                return Task.FromResult<IReadOnlyList<QuickAction>>([]);
            },
            DescribeSymbolProvider = (_, _, _) => throw new InvalidOperationException("boom")
        };
        var window = new Window { Content = code, Width = 400, Height = 200 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => discovered);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            var field = typeof(EditorSmartTagMargin).GetField("Requested",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var handler = Assert.IsType<EventHandler>(field.GetValue(code.SmartTagMargin));
            handler(code.SmartTagMargin, EventArgs.Empty);

            await WaitUntilAsync(() => vm.BuildOutput.Any(line => line.Contains("boom", StringComparison.Ordinal)));
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1000 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition());
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }
}
