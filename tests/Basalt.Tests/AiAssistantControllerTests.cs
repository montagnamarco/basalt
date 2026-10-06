using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Ai;
using Basalt.Workspace.Refactoring;

namespace Basalt.Tests;

/// <summary>
/// What happens when the user decides on an assistant's proposal: the same
/// buffer-transaction path a smart-tag fix takes, with nothing saved.
/// </summary>
public sealed class AiAssistantControllerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basalt-ai-apply-").FullName;
    private readonly MainWindowViewModel _viewModel = new();
    private readonly AiChatPanel _panel = new();
    private readonly List<string> _tabs = [];

    private AiAssistantController Controller(Func<RefactoringPreview, Task<bool>>? confirm = null) => new(
        _panel, _viewModel, () => new Basalt.Core.Settings.AiSettings(), new ApiKeyStore(Path.Combine(_directory, "keys")),
        () => null, paths => _tabs.AddRange(paths), confirm ?? (_ => Task.FromResult(true)));

    private AiChangeSet Propose(params AiFileChange[] files) => new("Fix it", files);

    private AiFileChange Change(string name, string before, string after, bool isNew = false) =>
        new(Path.Combine(_directory, name), name, before, after) { IsNewFile = isNew };

    public void Dispose()
    {
        _viewModel.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task AcceptingChangesTheOpenBufferAndLeavesTheDiskAlone()
    {
        var path = Path.Combine(_directory, "A.vb");
        File.WriteAllText(path, "Module A");
        var document = new EditorDocumentViewModel(path, "Module A");
        _viewModel.OpenDocuments.Add(document);

        var set = Propose(Change("A.vb", "Module A", "Module B"));

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, Accept: true));

        Assert.Equal("Module B", document.Text);
        Assert.True(document.IsModified);

        // Saving stays the user's decision.
        Assert.Equal("Module A", File.ReadAllText(path));
        Assert.Equal(AiChangeState.Accepted, set.Files[0].State);
        Assert.Equal([path], _tabs);
    }

    [AvaloniaFact]
    public async Task AcceptingAFileThatIsNotOpenOpensItAsAModifiedTabWithoutWritingIt()
    {
        var path = Path.Combine(_directory, "Closed.vb");
        File.WriteAllText(path, "old");

        var set = Propose(Change("Closed.vb", "old", "new"));

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

        var document = Assert.Single(_viewModel.OpenDocuments);

        Assert.Equal("new", document.Text);
        Assert.True(document.IsModified);
        Assert.Equal("old", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task AcceptingANewFileOpensItAsAModifiedTabWithoutCreatingIt()
    {
        var set = Propose(Change("New.vb", "", "Module New", isNew: true));

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

        Assert.Equal("Module New", Assert.Single(_viewModel.OpenDocuments).Text);
        Assert.False(File.Exists(Path.Combine(_directory, "New.vb")));
    }

    [AvaloniaFact]
    public async Task RefusesAFileTheUserHasEditedSinceTheAssistantSawIt()
    {
        var path = Path.Combine(_directory, "A.vb");
        File.WriteAllText(path, "Module A");
        var document = new EditorDocumentViewModel(path, "Module A");
        _viewModel.OpenDocuments.Add(document);

        var set = Propose(Change("A.vb", "Module A", "Module B"));

        // The user types after the proposal was made.
        document.Text = "Module A ' my own edit";

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

        Assert.Equal("Module A ' my own edit", document.Text);
        Assert.Equal(AiChangeState.Failed, set.Files[0].State);
        Assert.NotNull(set.Files[0].Problem);
        Assert.Empty(_tabs);
    }

    [AvaloniaFact]
    public async Task RefusesAClosedFileThatChangedOnDisk()
    {
        var path = Path.Combine(_directory, "Closed.vb");
        File.WriteAllText(path, "changed elsewhere");

        var set = Propose(Change("Closed.vb", "old", "new"));

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

        Assert.Equal(AiChangeState.Failed, set.Files[0].State);
        Assert.Empty(_viewModel.OpenDocuments);
        Assert.Equal("changed elsewhere", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task OneStaleFileDoesNotHoldBackTheOthers()
    {
        var stale = Path.Combine(_directory, "Stale.vb");
        File.WriteAllText(stale, "edited on disk");
        File.WriteAllText(Path.Combine(_directory, "Fine.vb"), "old");

        var set = Propose(Change("Stale.vb", "old", "new"), Change("Fine.vb", "old", "new"));

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

        Assert.Equal(AiChangeState.Failed, set.Files[0].State);
        Assert.Equal(AiChangeState.Accepted, set.Files[1].State);
    }

    [AvaloniaFact]
    public async Task AnyFailureWhileApplyingLeavesTheFileFailedNotApplyingForever()
    {
        var path = Path.Combine(_directory, "A.vb");
        File.WriteAllText(path, "Module A");
        var document = new EditorDocumentViewModel(path, "Module A");
        _viewModel.OpenDocuments.Add(document);

        // A document that cannot take the text, as a locked or protected one would.
        document.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorDocumentViewModel.Text) && document.Text == "Module B")
                throw new UnauthorizedAccessException("read-only");
        };

        var set = Propose(Change("A.vb", "Module A", "Module B"));

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

        Assert.Equal(AiChangeState.Failed, set.Files[0].State);
        Assert.Contains("read-only", set.Files[0].Problem);
        Assert.Equal("Module A", document.Text);

        // The card offers no buttons for a failed file, and Accept all would not reach it.
        Assert.Empty(set.Pending);
    }

    [AvaloniaFact]
    public async Task CapturingAChangeAmongManyOpenDocumentsDoesNotResolveEveryOne()
    {
        // Smart-tag fixes and assistant changes validate the open documents
        // on the interface thread, again after each file: path resolution
        // per document per comparison froze the editor.
        var resolved = new List<string>();
        var matcher = new PathMatcher(path => { resolved.Add(path); return path; });

        for (var i = 0; i < 200; i++)
            _viewModel.OpenDocuments.Add(new EditorDocumentViewModel(Path.Combine(_directory, $"Doc{i}.vb"), "x"));

        var target = Path.Combine(_directory, "Target.vb");
        File.WriteAllText(target, "old");

        var preview = new RefactoringPreview("t", [new FileChangePreview(target, "old", "new")]);

        var transaction = await EditorSmartTagTransaction.CaptureAsync(preview, _viewModel.OpenDocuments, matcher);
        await transaction.ApplyAsync();

        Assert.Empty(resolved);
    }

    [AvaloniaFact]
    public async Task RejectingTouchesNothing()
    {
        var path = Path.Combine(_directory, "A.vb");
        var document = new EditorDocumentViewModel(path, "Module A");
        _viewModel.OpenDocuments.Add(document);

        var set = Propose(Change("A.vb", "Module A", "Module B"));

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, Accept: false));

        Assert.Equal("Module A", document.Text);
        Assert.False(document.IsModified);
        Assert.Equal(AiChangeState.Rejected, set.Files[0].State);
    }

    [AvaloniaFact]
    public async Task AnAcceptedChangeIsOneUndoStepInTheEditor()
    {
        var path = Path.Combine(_directory, "Undo.vb");
        File.WriteAllText(path, "Class Original\nEnd Class");
        var document = new EditorDocumentViewModel(path, "Class Original\nEnd Class");
        _viewModel.OpenDocuments.Add(document);

        var code = new CodeEditor(document, _viewModel) { AutoFormatWhileTyping = false };
        var window = new Window { Content = code, Width = 500, Height = 300 };
        window.Show();

        try
        {
            var set = Propose(Change("Undo.vb", "Class Original\nEnd Class", "Class Renamed\nEnd Class"));

            await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

            var editor = Assert.Single(code.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>());

            Assert.Equal("Class Renamed\nEnd Class", editor.Text);

            editor.Undo();

            Assert.Equal("Class Original\nEnd Class", document.Text);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AcceptingTwiceAtOnceAppliesTheFileOnce()
    {
        var path = Path.Combine(_directory, "Closed.vb");
        File.WriteAllText(path, "old");

        var set = Propose(Change("Closed.vb", "old", "new"));
        var controller = Controller();

        // Two clicks before the first has finished.
        await Task.WhenAll(
            controller.DecideAsync(new AiChangeDecision(set, set.Files, true)),
            controller.DecideAsync(new AiChangeDecision(set, set.Files, true)));

        Assert.Equal(AiChangeState.Accepted, set.Files[0].State);
        Assert.Single(_viewModel.OpenDocuments);
        Assert.Single(_tabs);
    }

    [AvaloniaFact]
    public async Task AcceptAllAfterAcceptLeavesTheAcceptedFileAlone()
    {
        File.WriteAllText(Path.Combine(_directory, "A.vb"), "a");
        File.WriteAllText(Path.Combine(_directory, "B.vb"), "b");

        var set = Propose(Change("A.vb", "a", "A"), Change("B.vb", "b", "B"));
        var controller = Controller();

        await controller.DecideAsync(new AiChangeDecision(set, [set.Files[0]], true));

        var a = _viewModel.OpenDocuments.Single();
        a.Text = "A edited afterwards";

        await controller.DecideAsync(new AiChangeDecision(set, set.Files, true));

        Assert.Equal("A edited afterwards", a.Text);
        Assert.All(set.Files, f => Assert.Equal(AiChangeState.Accepted, f.State));
        Assert.Equal(2, _viewModel.OpenDocuments.Count);
    }

    [AvaloniaFact]
    public void AFileMovesOnlyAlongPendingApplyingAcceptedOrFailed()
    {
        var file = Change("A.vb", "a", "b");
        var set = Propose(file);

        Assert.False(set.MarkAccepted(file));
        Assert.True(set.TryBeginApply(file));
        Assert.False(set.TryBeginApply(file));
        Assert.False(set.MarkRejected(file));
        Assert.True(set.MarkFailed(file, "stale"));
        Assert.False(set.MarkAccepted(file));
    }

    [AvaloniaFact]
    public async Task AcceptingThroughALinkedSpellingDoesNotOpenASecondTab()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a link needs privileges on Windows.");

        var link = Path.Combine(Directory.CreateTempSubdirectory("basalt-ai-link-").FullName, "solution");
        Directory.CreateSymbolicLink(link, _directory);

        try
        {
            var real = Path.Combine(_directory, "A.vb");
            File.WriteAllText(real, "Module A");

            // The editor holds the file under the linked spelling, dirty.
            var document = new EditorDocumentViewModel(Path.Combine(link, "A.vb"), "Module A");
            _viewModel.OpenDocuments.Add(document);

            var set = Propose(new AiFileChange(PathIdentity.Canonical(real), "A.vb", "Module A", "Module B"));

            await Controller().DecideAsync(new AiChangeDecision(set, set.Files, true));

            Assert.Equal(AiChangeState.Accepted, set.Files[0].State);
            Assert.Single(_viewModel.OpenDocuments);
            Assert.Equal("Module B", document.Text);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(link)!, recursive: true);
        }
    }

    [AvaloniaFact]
    public void ClearingStopsAReplyThatIsStillArriving()
    {
        using var running = new CancellationTokenSource();
        _panel.BeginReply(running);

        _panel.Clear();

        Assert.True(running.IsCancellationRequested);
    }

    // The card in the chat

    [AvaloniaFact]
    public void ShowsEachFileWithItsDiffAndButtonsToAcceptOrRejectIt()
    {
        var set = Propose(Change("A.vb", "x", "y"), Change("B.vb", "p", "q"));
        _panel.AppendChanges(set);

        var window = new Window { Content = _panel, Width = 600, Height = 700 };
        window.Show();

        try
        {
            window.UpdateLayout();

            var buttons = _panel.GetVisualDescendants().OfType<Button>()
                .Select(b => b.Name).Where(n => n is not null).ToList();

            Assert.Equal(2, buttons.Count(n => n == "Accept"));
            Assert.Equal(2, buttons.Count(n => n == "Reject"));
            Assert.Contains("AcceptAll", buttons);
            Assert.Contains("RejectAll", buttons);

            Assert.Equal(2, _panel.GetVisualDescendants().OfType<DiffView>().Count());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ReportsTheDecisionTheUserClicks()
    {
        var set = Propose(Change("A.vb", "x", "y"), Change("B.vb", "p", "q"));
        _panel.AppendChanges(set);

        AiChangeDecision? decided = null;
        _panel.ChangesDecided += (_, d) => decided = d;

        var window = new Window { Content = _panel, Width = 600, Height = 700 };
        window.Show();

        try
        {
            window.UpdateLayout();

            _panel.GetVisualDescendants().OfType<Button>().First(b => b.Name == "RejectAll")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.False(decided!.Accept);
            Assert.Equal(2, decided.Files.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OnceDecidedTheCardShowsTheOutcomeInsteadOfButtons()
    {
        var set = Propose(Change("A.vb", "x", "y"));
        _panel.AppendChanges(set);

        await Controller().DecideAsync(new AiChangeDecision(set, set.Files, false));

        var window = new Window { Content = _panel, Width = 600, Height = 700 };
        window.Show();

        try
        {
            window.UpdateLayout();

            Assert.DoesNotContain(_panel.GetVisualDescendants().OfType<Button>(), b => b.Name == "Accept");
            Assert.Empty(_panel.GetVisualDescendants().OfType<DiffView>());
        }
        finally
        {
            window.Close();
        }
    }

    // Applying a code block

    [AvaloniaFact]
    public async Task ApplyingACodeBlockShowsADiffFirstAndDoesNothingWhenDeclined()
    {
        var path = Path.Combine(_directory, "A.vb");
        var document = new EditorDocumentViewModel(path, "Module A");
        _viewModel.OpenDocuments.Add(document);
        _viewModel.ActiveDocument = document;

        var editor = new AvaloniaEdit.TextEditor { Text = "Module A", CaretOffset = 8 };

        RefactoringPreview? shown = null;

        var controller = new AiAssistantController(
            _panel, _viewModel, () => new Basalt.Core.Settings.AiSettings(),
            new ApiKeyStore(Path.Combine(_directory, "keys")), () => editor, _ => { },
            preview => { shown = preview; return Task.FromResult(false); });

        await controller.ApplyCodeAsync("\n' added");

        Assert.NotNull(shown);
        Assert.Equal("Module A\n' added", shown!.Changes[0].NewText);
        Assert.Equal("Module A", document.Text);
    }

    [AvaloniaFact]
    public async Task ApplyingACodeBlockReplacesTheSelectionOnceConfirmed()
    {
        var path = Path.Combine(_directory, "A.vb");
        var document = new EditorDocumentViewModel(path, "Dim x = 1\nDim y = 2");
        _viewModel.OpenDocuments.Add(document);
        _viewModel.ActiveDocument = document;

        var editor = new AvaloniaEdit.TextEditor { Text = document.Text };
        editor.Select(0, 9);

        await new AiAssistantController(
            _panel, _viewModel, () => new Basalt.Core.Settings.AiSettings(),
            new ApiKeyStore(Path.Combine(_directory, "keys")), () => editor, _ => { },
            _ => Task.FromResult(true)).ApplyCodeAsync("Dim z = 3");

        Assert.Equal("Dim z = 3\nDim y = 2", document.Text);
    }

    // The conversation

    [AvaloniaFact]
    public void ShowsToolActivityAndNoticesAsLinesOfTheirOwn()
    {
        var controller = Controller();
        var activity = new Dictionary<string, ChatEntry>();
        _panel.Append(Microsoft.Extensions.AI.ChatRole.Assistant, "");

        var call = new AiToolCallPart("c1", "read_file", new System.Text.Json.Nodes.JsonObject { ["path"] = "A.vb" });

        controller.Show(new AiToolStarted(call), activity);
        controller.Show(new AiToolFinished(call, new AiToolResult("text")), activity);
        controller.Show(new AiTextChunk("It declares."), activity);
        controller.Show(new AiNotice(AiNoticeKind.Refusal, "declined"), activity);

        // The empty reply is gone; the activity reads "Used read_file: A.vb".
        Assert.Equal(["Used read_file: A.vb", "It declares.", "declined"], _panel.Entries.Select(e => e.Text));
        Assert.Equal(ChatEntryKind.Activity, _panel.Entries[0].Kind);
        Assert.Equal(ChatEntryKind.Notice, _panel.Entries[2].Kind);
    }

    [AvaloniaFact]
    public void NamesATypedModelIdAsItIs()
    {
        var provider = new ClaudeProvider();

        Assert.Equal("Claude Opus 5.5", AiAssistantController.ModelName(provider, "claude-opus-5-5"));
        Assert.Equal("my-custom-model", AiAssistantController.ModelName(provider, "my-custom-model"));
        Assert.Equal("Claude Opus 5.5", AiAssistantController.ModelName(provider, ""));
    }

    [AvaloniaFact]
    public void DescribesTheSolutionTheAssistantWorksIn()
    {
        File.WriteAllText(Path.Combine(_directory, "App.vbproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0</TargetFramework>
              <UseWindowsForms>true</UseWindowsForms></PropertyGroup>
            </Project>
            """);

        var description = AiProjectContext.Describe(Path.Combine(_directory, "App.sln"), _directory);

        Assert.Contains("App.sln", description);
        Assert.Contains("App.vbproj: Visual Basic, net10.0, WinForms", description);
    }
}
