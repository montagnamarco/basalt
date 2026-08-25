using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Dock.Model.Core;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Core.Commands;
using Basalt.Core.Services;
using Basalt.Extensibility.Interpretation;
using Basalt.Core.Settings;
using Basalt.Designer;
using Basalt.Extensibility;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using TestResult = Basalt.Core.Services.TestResult;
using Basalt.Workspace.Ai;
using Basalt.Workspace.Testing;

namespace Basalt.Shell;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private readonly IdeDockFactory _factory = new();

    /// <summary>Already-built contents, one per panel or document.</summary>
    private readonly Dictionary<IDockable, Control> _contents = [];

    /// <summary>Open dockable documents, indexed by ViewModel.</summary>
    private readonly Dictionary<EditorDocumentViewModel, IdeDocument> _documents = [];

    private SearchPanel? _search;
    private OutlinePanel? _outline;
    private ReferencesPanel? _references;
    private CallStackPanel? _callStack;
    private VariablesPanel? _variables;
    private BreakpointsPanel? _breakpoints;
    private WatchPanel? _watch;
    private GitChangesPanel? _gitChanges;
    private GitHistoryPanel? _gitHistory;
    private BranchPanel? _branches;
    private DiffView? _diff;
    private TestExplorerPanel? _tests;
    private AiChatPanel? _assistant;

    /// <summary>The conversation with the assistant, which outlives one question.</summary>
    private readonly AiConversation _conversation = new();

    /// <summary>Where the assistants' API keys are kept.</summary>
    private readonly ApiKeyStore _apiKeys = new();

    /// <summary>
    /// Every command the IDE offers, and the keys assigned to them.
    ///
    /// One registry behind the menus, the toolbar, the keyboard and the
    /// palette, so none of them can disagree about what a key does.
    /// </summary>
    private readonly CommandRegistry _commands = LoadCommands();

    /// <summary>
    /// Builds the registry from what the user chose.
    ///
    /// The scheme decides the defaults; what the user assigned by hand is
    /// applied on top, so a later change to the defaults still reaches them.
    /// </summary>
    private static CommandRegistry LoadCommands()
    {
        var settings = new SettingsStore().Load().Keyboard;

        var scheme = Enum.TryParse<KeyboardScheme>(settings.Scheme, out var chosen)
            ? chosen
            : KeyboardScheme.Basalt;

        var registry = IdeCommands.CreateRegistry(scheme);

        registry.ApplyCustomisations(settings.Shortcuts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Length == 0 ? null : pair.Value,
            StringComparer.Ordinal));

        return registry;
    }

    /// <summary>Finds and runs the tests of the solution.</summary>
    private readonly DotnetTestService _testService = new();

    /// <summary>
    /// The debugging session, which outlives any one open file: a breakpoint
    /// set and then closed must still stop when the program runs.
    /// </summary>
    private readonly DebugSession _debug = new();

    /// <summary>Breakpoint margins of the open editors, indexed by file.</summary>
    private readonly Dictionary<string, BreakpointMargin> _margins = new(StringComparer.Ordinal);

    /// <summary>Where the user's settings are kept between sessions.</summary>
    private readonly SettingsStore _settingsStore = new();

    /// <summary>The languages the IDE knows, which contribute their own settings.</summary>
    private LanguageRegistry Languages => ViewModel.Languages;
    private SolutionExplorerPanel? _solutionExplorer;
    private ToolboxPanel? _toolbox;
    private PropertyPanel? _properties;

    /// <summary>The surface the pointer was last over, for toolbox drops.</summary>
    private DesignSurface? _activeSurface;

    public MainWindow() : this(null) { }

    /// <summary>
    /// Builds the window, opening the given solution at startup.
    /// The path comes from the command-line arguments, so the IDE can be
    /// launched directly on a project.
    /// </summary>
    public MainWindow(string? solutionToOpen)
    {
        InitializeComponent();

        var viewModel = new MainWindowViewModel();
        viewModel.ActiveDocumentChanged += (_, _) => ShowActiveDocument();
        DataContext = viewModel;

        InstallMenu(viewModel);
        ShowRecentOnWelcome();
        BuildLayout(viewModel);

        if (solutionToOpen is not null)
            Loaded += (_, _) => Guarded.Run(
                () => viewModel.OpenSolutionAsync(solutionToOpen),
                ViewModel.WriteOutput, "ide");





    }

    /// <summary>Creates the dockable layout and wires up the panel contents.</summary>
    private void BuildLayout(MainWindowViewModel viewModel)
    {
        _factory.TerminalWorkingDirectory = Directory.GetCurrentDirectory();

        // Remembered so the next session can start where this one left off.
        viewModel.Opened += (_, opened) => Remember(opened.Path, opened.IsSolution);

        _solutionExplorer = new SolutionExplorerPanel(viewModel);

        // Finding the open file in the tree by hand means scrolling a project
        // the size of a real one.
        _solutionExplorer.SyncRequested += (_, _) =>
        {
            if (viewModel.ActiveDocument is { } active)
                _solutionExplorer.Reveal(active.FilePath);
        };
        _solutionExplorer.FileOpened += (_, node) => Guarded.Run(
            () => viewModel.OpenFileAsync(node.Path, inDesigner: node.IsDesignable),
            ViewModel.WriteOutput, "ide");

        _toolbox = new ToolboxPanel();
        _toolbox.ControlChosen += (_, item) =>
        {
            // Into whatever container the pointer is over, so a control
            // dropped on a nested panel goes in that panel rather than in the
            // root of the document.
            var container = _activeSurface?.ContainerAt(_activeSurface.LastPointerPosition);

            ViewModel.ActiveDesigner?.InsertFromToolbox(item, container);
            _properties?.Show(ViewModel.ActiveDesigner);
        };

        _properties = new PropertyPanel();

        _contents[_factory.SolutionExplorer] = _solutionExplorer;
        _contents[_factory.Toolbox] = _toolbox;
        _contents[_factory.Properties] = _properties;
        _contents[_factory.Problems] = new ProblemsPanel(viewModel, OpenDiagnostic);
        _contents[_factory.Output] = new OutputPanel(viewModel);

        _search = new SearchPanel();
        _search.HitActivated += (_, hit) => Guarded.Run(
            () => OpenHitAsync(hit),
            ViewModel.WriteOutput, "ide");
        _contents[_factory.Search] = _search;

        _outline = new OutlinePanel();
        _outline.SymbolActivated += (_, symbol) => GoToLine(symbol.Range.Start.Line);
        _contents[_factory.Outline] = _outline;

        _references = new ReferencesPanel();
        _references.ReferenceActivated += (_, location) => Guarded.Run(
            () => OpenLocationAsync(location),
            ViewModel.WriteOutput, "ide");
        _contents[_factory.References] = _references;

        BuildDebugPanels();
        BuildGitPanels();
        BuildTestPanel();
        BuildAssistantPanel();
        BuildToolbar();
        ConnectContextMenus();

        // Tunnelling, so the debugging keys win over the editor's own handling
        // of F-keys before it consumes them.
        AddHandler(KeyDownEvent, OnKeyboardShortcut, RoutingStrategies.Tunnel);

        viewModel.ActiveDocumentChanged += (_, _) => Guarded.Run(
            () => RefreshOutlineAsync(),
            ViewModel.WriteOutput, "ide");

        viewModel.ActiveDocumentChanged += (_, _) => Guarded.Run(
            async () =>
            {
                if (viewModel.ActiveDocument is { } document)
                    await RefreshEncodingAsync(document.FilePath);
            },
            ViewModel.WriteOutput, "ide");

        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);

        Docking.Factory = _factory;
        Docking.Layout = layout;

        // Dock asks the view for the content of each dockable: here we
        // return the controls built in code, terminals included.
        Docking.DataTemplates.Add(new FuncDataTemplate<IDockable>(
            (dockable, _) => dockable is null ? null : ContentFor(dockable)));

        AttachDocumentTabMenus();

        ViewModel.Operations.Changed += (_, _) =>
            Dispatcher.UIThread.Post(ShowRunningOperation);
    }

    /// <summary>
    /// Shows what is running, with a way to stop it.
    ///
    /// Hidden when nothing is: a stop button with nothing to stop is one the
    /// user has to work out the meaning of.
    /// </summary>
    private void ShowRunningOperation()
    {
        if (ViewModel.Operations.Current is { } operation)
        {
            CancelOperationLabel.Text = $"{operation.Description}  ✕";
            CancelOperationButton.IsVisible = true;
            return;
        }

        CancelOperationButton.IsVisible = false;
    }

    /// <summary>Stops whatever is running.</summary>
    private void OnCancelOperation(object? sender, RoutedEventArgs e)
    {
        ViewModel.Operations.CancelCurrent();

        CancelOperationLabel.Text = "Stopping…";
    }

    /// <summary>
    /// Gives each document tab its menu as it appears.
    ///
    /// Attached when the tab is realised rather than declared in a style,
    /// because each menu closes over the document it belongs to, and a style
    /// setter has no way to say which document a tab is showing.
    ///
    /// Driven by the open documents changing rather than by the dock being
    /// loaded: loading happens once, before anything is open, so a sweep
    /// there would find no tabs at all.
    /// </summary>
    private void AttachDocumentTabMenus() =>
        // On every layout pass of the dock rather than on an event from the
        // tab. Measured, because two likelier approaches do not work: a tab's
        // Loaded does not bubble (only the window's own arrives), and a sweep
        // posted when a document is opened runs while the dock has not built
        // the tab yet. LayoutUpdated fires after it has.
        Docking.LayoutUpdated += (_, _) => GiveTabsTheirMenus();

    /// <summary>Puts a menu on every document tab that has none yet.</summary>
    private void GiveTabsTheirMenus()
    {
        foreach (var tab in Docking.GetVisualDescendants().OfType<DocumentTabStripItem>())
            GiveTabItsMenu(tab);
    }

    /// <summary>
    /// Puts a menu on one tab.
    ///
    /// Each menu closes over the document its tab is showing, so a tab that
    /// comes to show a different one is given a new menu rather than keeping
    /// one that would close the wrong document.
    /// </summary>
    private void GiveTabItsMenu(DocumentTabStripItem tab)
    {
        if (tab.DataContext is not Basalt.Shell.Docking.IdeDocument document) return;

        if (tab.ContextMenu is { Tag: Basalt.Shell.Docking.IdeDocument already }
            && ReferenceEquals(already, document))
        {
            return;
        }

        var menu = Basalt.Shell.Docking.DocumentTabMenu.Build(document, OnDocumentTabCommand);

        menu.Tag = document;

        tab.ContextMenu = menu;
    }

    /// <summary>
    /// Carries out what was asked for on a document tab.
    ///
    /// The closing commands go to the dock, which already knows how to close
    /// others and to the right; the rest are ours.
    /// </summary>
    private async void OnDocumentTabCommand(
        object? sender, (Basalt.Shell.Docking.IdeDocument Document, Basalt.Shell.Docking.DocumentTabCommand Command) asked)
    {
        var (document, command) = asked;

        if (Basalt.Shell.Docking.DocumentTabMenu.Close(_factory, document, command))
        {
            // A tab that closed leaves the others where they were, so the
            // ones that appear in its place still need their menus.
            Dispatcher.UIThread.Post(GiveTabsTheirMenus, DispatcherPriority.Loaded);
            return;
        }

        var path = document.Document.FilePath;

        switch (command)
        {
            case Basalt.Shell.Docking.DocumentTabCommand.CopyPath:
                await CopyToClipboardAsync(path);
                ViewModel.WriteOutput($"[editor] Copied {path}");
                break;

            case Basalt.Shell.Docking.DocumentTabCommand.OpenContainingFolder:
                RevealInFileManager(path);
                break;
        }
    }

    /// <summary>
    /// Returns the control associated with a panel or document, creating it on
    /// first use. Reuse preserves state, caret position and history.
    /// </summary>
    private Control? ContentFor(IDockable dockable)
    {
        if (_headed.TryGetValue(dockable, out var already)) return already;

        if (!_contents.TryGetValue(dockable, out var content))
        {
            content = dockable switch
            {
                TerminalTool terminal => NewTerminal(terminal.WorkingDirectory),
                IdeDocument ideDocument => CreateDocumentView(ideDocument.Document),
                _ => null
            };

            if (content is not null) _contents[dockable] = content;
        }

        if (content is null) return null;

        // A document is named by its own tab; a panel is not, and Dock has no
        // icon in its model, so the panel wears its own header.
        var headed = dockable is IdeTool { Icon: not IconKind.None } tool
            ? new PanelHeader(tool.Title ?? "", tool.Icon, content, ToolsOf(content))
            : content;

        _headed[dockable] = headed;

        return headed;
    }

    /// <summary>
    /// Starts a repository in the solution's folder.
    ///
    /// The commonest moment to want one is just after making a solution, and
    /// until now that meant leaving for a terminal.
    /// </summary>
    private async Task StartRepositoryAsync()
    {
        if (ViewModel.SolutionPath is not { } solution)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(solution));

        if (folder is null) return;

        var started = await Basalt.Workspace.GitSourceControlService
            .InitializeAsync(folder, "First commit");

        ViewModel.StatusMessage = Localizer.Get(started
            ? StringKeys.StatusRepositoryCreated
            : StringKeys.StatusRepositoryExists);

        if (started) await RefreshGitAsync();
    }

    /// <summary>
    /// The buttons a panel wants in its header, when it has any.
    /// </summary>
    private static Control? ToolsOf(Control content) => content switch
    {
        SolutionExplorerPanel explorer => explorer.Tools,
        _ => null
    };

    /// <summary>
    /// The panels as Dock sees them: the control plus the header above it.
    ///
    /// Kept apart from <c>_contents</c>, which holds the panels themselves and
    /// is what the rest of the window looks things up in.
    /// </summary>
    private readonly Dictionary<IDockable, Control> _headed = [];

    /// <summary>
    /// The drawing of a .axaml, or an explanation when it cannot be drawn.
    /// </summary>
    private Control DesignSurfaceFor(EditorDocumentViewModel document)
    {
        var session = ViewModel.DesignerSessionFor(document);

        // A file the designer cannot read: show why. The markup is a click
        // away in the other half, which is where it gets repaired.
        if (session is null) return BrokenDesignerView(document);

        var surface = new DesignSurface { Session = session };

        surface.SelectionChanged += (_, _) => _properties?.Show(session);

        // Remembered so the toolbox knows where to put a new control: the
        // panel and the surface are built separately, and without this a
        // control dropped into a nested panel landed in the root instead.
        surface.PointerEntered += (_, _) => _activeSurface = surface;

        _activeSurface ??= surface;

        return surface;
    }

    /// <summary>
    /// What is shown when a .axaml will not parse: the reason, and the text.
    ///
    /// The text especially — a broken XAML is repaired by editing it, and an
    /// empty surface leaves nothing to edit.
    /// </summary>
    private Control BrokenDesignerView(EditorDocumentViewModel document)
    {
        var reason = ViewModel.DesignerProblems.TryGetValue(document, out var problem)
            ? problem
            : "This file cannot be shown in the designer.";

        var banner = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.FromArgb(0x22, 0xD9, 0x53, 0x1E)),
            Padding = new Thickness(12, 8),
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new IconView { Kind = IconKind.Warning, IconSize = 14 },
                    new TextBlock
                    {
                        Text = reason,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                }
            }
        };

        var editor = TextEditorFor(document);

        DockPanel.SetDock(banner, Avalonia.Controls.Dock.Top);

        return new DockPanel { Children = { banner, editor } };
    }

    private Control CreateDocumentView(EditorDocumentViewModel document)
    {
        if (document.OpenInDesigner)
        {
            // Both halves of the same file in one tab, with a way to change
            // which is showing: the markup used to be reachable only by
            // closing the file and opening it again from another menu entry.
            return new DesignerHost(
                document,
                () => DesignSurfaceFor(document),
                () => TextEditorFor(document));
        }

        return TextEditorFor(document);
    }

    /// <summary>
    /// A code editor for a document, wired to the shell.
    ///
    /// Shared with the fallback shown when the designer cannot read a file:
    /// that one needs a real editor, not a read-only copy of the text.
    /// </summary>
    private CodeEditor TextEditorFor(EditorDocumentViewModel document)
    {
var editor = new CodeEditor(document, ViewModel);

        // The settings the user already chose apply to this editor too, not
        // only to the ones open when they chose them.
        editor.ApplySettings(_settingsStore.Load());

        editor.CaretMoved += (_, _) => ShowCaretPosition();

        // Also once the editor is in the tree: without this the status bar
        // stays blank until the caret happens to move, which reads as the
        // fields not working. Posted rather than run at once, because at the
        // moment of attaching the inner text editor is not yet realised and
        // CurrentEditor finds nothing.
        editor.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(
                () =>
                {
                    ShowCaretPosition();

                    // And the caret goes into the editor. Without this an
                    // opened file could be read and not typed into until it
                    // was clicked: keystrokes went nowhere, so the completion
                    // list never opened and no tooltip ever appeared.
                    editor.FocusText();
                },
                DispatcherPriority.Loaded);

        editor.EditorMenu.CommandChosen += (_, command) => Guarded.Run(
            () => RunEditorCommandAsync(command, editor),
            ViewModel.WriteOutput, "ide");

        ConnectDebugging(editor);
        _ = ShowGitChangesInMarginAsync(editor);

        return editor;
    }

    /// <summary>
    /// Connects an editor to the debugging session.
    ///
    /// The margin reports clicks into the session, which owns the breakpoints,
    /// and the session's existing breakpoints for this file are drawn straight
    /// away: reopening a file must show the breakpoints already set in it.
    /// </summary>
    private void ConnectDebugging(CodeEditor editor)
    {
        var path = editor.FilePath;

        _margins[path] = editor.BreakpointMargin;

        editor.BreakpointMargin.Toggled += (_, line) => Guarded.Run(
            () => _debug.ToggleBreakpointAsync(path, line),
            ViewModel.WriteOutput, "ide");

        editor.BreakpointMargin.ConditionRequested += (_, line) => Guarded.Run(
            () => EditBreakpointConditionAsync(path, line),
            ViewModel.WriteOutput, "ide");

        editor.BreakpointMargin.RunToLineRequested += (_, line) => Guarded.Run(
            () => RunToLineAsync(path, line),
            ViewModel.WriteOutput, "ide");

        RefreshBreakpointDisplay(path);

        // While stopped in this file, mark the line execution is on.
        if (_debug.CurrentFrame is { } frame &&
            string.Equals(frame.FilePath, path, StringComparison.Ordinal))
        {
            editor.BreakpointMargin.ShowCurrentLine(frame.Line);
        }
    }

    /// <summary>Opens or brings to the front the active document in the central area.</summary>
    private void ShowActiveDocument()
    {
        if (ActingDocument is not { } document) return;
        if (_factory.DocumentArea is not { } area) return;

        if (!_documents.TryGetValue(document, out var dockable))
        {
            dockable = new IdeDocument(document);
            _documents[document] = dockable;
            _factory.AddDockable(area, dockable);
        }

        _factory.SetActiveDockable(dockable);
        _factory.SetFocusedDockable(area, dockable);

        // And the caret into the text. Dock focuses the panel; the editor
        // inside it stays unfocused, so switching back to a tab left the
        // keyboard pointing at nothing.
        Dispatcher.UIThread.Post(
            () => CurrentCodeEditor()?.FocusText(), DispatcherPriority.Loaded);

        // The tab for this document has just been asked for; its menu is put
        // on once the tab itself has been realised.
        Dispatcher.UIThread.Post(GiveTabsTheirMenus, DispatcherPriority.Loaded);

        if (document.OpenInDesigner)
            _properties?.Show(ViewModel.DesignerSessionFor(document));
    }

    /// <summary>Opens a search result and puts the caret on the hit.</summary>
    private async Task OpenHitAsync(Basalt.Workspace.Search.SearchHit hit)
    {
        if (!File.Exists(hit.FilePath)) return;

        await ViewModel.OpenFileAsync(hit.FilePath);

        // The editor is created by the document change, so moving the caret is
        // queued until after that has happened.
        Dispatcher.UIThread.Post(() =>
        {
            var editor = DocumentHostContent()?.GetVisualDescendants()
                .OfType<AvaloniaEdit.TextEditor>().FirstOrDefault();

            if (editor is null) return;

            editor.CaretOffset = Math.Clamp(hit.Offset, 0, editor.Document.TextLength);
            editor.ScrollToLine(hit.Line);
            editor.Select(editor.CaretOffset, hit.Length);
        }, DispatcherPriority.Background);
    }

    private Control? DocumentHostContent() =>
        _documents.Values
            .Select(d => _contents.TryGetValue(d, out var c) ? c : null)
            .FirstOrDefault(c => c is not null);

    /// <summary>Rebuilds the outline for the document now in front.</summary>
    private async Task RefreshOutlineAsync()
    {
        if (_outline is null) return;

        if (ViewModel.ActiveDocument is not { OpenInDesigner: false } document)
        {
            _outline.Clear();
            return;
        }

        var symbols = await ViewModel
            .GetDocumentSymbolsAsync(document.FilePath, document.Text)
            .ConfigureAwait(true);

        _outline.Show(symbols);
    }

    /// <summary>Jumps to where the symbol under the caret is declared.</summary>
    private async Task GoToDefinitionAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        RecordCurrentPosition();

        var target = await ViewModel
            .GoToDefinitionAsync(document.FilePath, editor.Text, editor.CaretOffset)
            .ConfigureAwait(true);

        if (target is not { } found)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusNoDefinition);
            return;
        }

        await OpenAtAsync(found.FilePath, found.Line, found.Column);
    }

    /// <summary>
    /// Marks the lines of a file that differ from the last commit.
    ///
    /// Silent when the file is not in a repository, which is the normal case
    /// for a project not under source control.
    /// </summary>
    private async Task ShowGitChangesInMarginAsync(CodeEditor editor)
    {
        if (ViewModel.Repository is not { } git) return;

        try
        {
            var diff = await git.GetDiffAsync(editor.FilePath, staged: false).ConfigureAwait(true);

            editor.GitChangeMargin.Show(GitChangeMargin.FromDiff(diff));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // A file outside the repository simply has nothing to mark.
        }
    }

    /// <summary>Builds the debugging panels and connects them to the session.</summary>
    private void BuildDebugPanels()
    {
        _callStack = new CallStackPanel();

        _callStack.FrameSelected += (_, index) => Guarded.Run(
            async () =>
            {
                // Looking at a caller changes which frame the variables and watch
                // expressions are read from.
                _debug.SelectedFrame = index;

                var frames = await _debug.GetCallStackAsync().ConfigureAwait(true);

                if (index >= 0 && index < frames.Count) await ShowFrameAsync(frames[index]);

                await RefreshDebugValuesAsync();
            },
            ViewModel.WriteOutput, "ide");

        _contents[_factory.CallStack] = _callStack;

        _variables = new VariablesPanel();
        _contents[_factory.Variables] = _variables;

        _watch = new WatchPanel();
        _watch.Changed += (_, _) => Guarded.Run(
            () => RefreshWatchAsync(),
            ViewModel.WriteOutput, "ide");

        _breakpoints = new BreakpointsPanel();

        _breakpoints.Toggled += (_, breakpoint) => Guarded.Run(
            () => _debug
            .SetBreakpointEnabledAsync(breakpoint.FilePath, breakpoint.Line, breakpoint.Enabled),
            ViewModel.WriteOutput, "ide");

        _breakpoints.Activated += (_, breakpoint) => Guarded.Run(
            () => OpenAtAsync(breakpoint.FilePath, breakpoint.Line, 1),
            ViewModel.WriteOutput, "ide");

        _breakpoints.ConditionRequested += (_, breakpoint) => Guarded.Run(
            () => EditBreakpointConditionAsync(breakpoint.FilePath, breakpoint.Line),
            ViewModel.WriteOutput, "ide");

        _breakpoints.RemoveRequested += (_, breakpoint) => Guarded.Run(
            () => _debug.ToggleBreakpointAsync(breakpoint.FilePath, breakpoint.Line),
            ViewModel.WriteOutput, "ide");

        _contents[_factory.Breakpoints] = _breakpoints;

        // Guarded: this used to be "async void", so anything thrown while
        // showing the frame — a file that moved, an adapter that answered
        // badly — reached the runtime and took the application down.
        _debug.Paused += (_, frame) => Guarded.Run(
            async () =>
            {
                ShowDebugState();
                await OnDebuggerPausedAsync(frame);
            },
            ViewModel.WriteOutput, "debug");

        _debug.Resumed += (_, _) => { OnDebuggerResumed(); ShowDebugState(); };
        _debug.Exited += (_, _) => { OnDebuggerResumed(); ShowDebugState(); };
        _debug.BreakpointsChanged += (_, file) => RefreshBreakpointDisplay(file);
        _debug.OutputReceived += (_, text) => ViewModel.WriteOutput(text);
        _debug.Failed += (_, message) => ViewModel.WriteOutput(message + Environment.NewLine);
        _debug.InputRequested += OnInputRequested;
    }

    /// <summary>
    /// Answers a program that is asking for a line to be typed.
    ///
    /// An interpreted program has no console of its own, so INPUT comes here.
    /// The interpreter is waiting inside a statement on a background thread,
    /// so the dialog is shown on the interface thread and waited for: letting
    /// the program carry on with nothing typed would be a wrong answer rather
    /// than a slow one.
    /// </summary>
    private void OnInputRequested(object? sender, InputRequest request)
    {
        // Waiting like this is safe only because the interpreter runs its
        // program under Task.Run, so this never arrives on the interface
        // thread; waiting on that thread for itself would deadlock.
        if (Dispatcher.UIThread.CheckAccess())
        {
            ViewModel.WriteOutput(
                "[debug] A program asked for input from the interface thread, "
              + "which cannot be answered without stopping it.");

            request.Response = "";
            return;
        }

        request.Response = Dispatcher.UIThread.InvokeAsync(
            async () => await AskForTextAsync("Input", request.Prompt, ""),
            DispatcherPriority.Normal).GetAwaiter().GetResult();
    }

    /// <summary>Shows where execution stopped, and what is in scope there.</summary>
    private async Task OnDebuggerPausedAsync(StackFrame frame)
    {
        await ShowFrameAsync(frame);

        if (_callStack is not null)
            _callStack.Show(await _debug.GetCallStackAsync().ConfigureAwait(true));

        await RefreshDebugValuesAsync();

        if (_factory.BottomArea is not null)
            _factory.ShowTool(_factory.CallStack, _factory.BottomArea);
    }

    /// <summary>Opens the file execution stopped in and marks the line.</summary>
    private async Task ShowFrameAsync(StackFrame frame)
    {
        if (frame.FilePath is null || frame.Line <= 0) return;

        await OpenAtAsync(frame.FilePath, frame.Line, 1);

        if (_margins.TryGetValue(frame.FilePath, out var margin))
            margin.ShowCurrentLine(frame.Line);
    }

    private async Task RefreshDebugValuesAsync()
    {
        if (_variables is not null)
        {
            var locals = await _debug.GetLocalsAsync().ConfigureAwait(true);

            _variables.Show([.. locals.Select(ToNode)]);
        }

        await RefreshWatchAsync();
    }

    /// <summary>
    /// Wraps a variable so its children are fetched only when opened.
    ///
    /// Walking an object graph eagerly would mean fetching the reachable heap
    /// to show one frame.
    /// </summary>
    private VariableNode ToNode(VariableValue value) =>
        new(value, 0, async _ =>
        {
            var children = await _debug.ExpandAsync(0).ConfigureAwait(true);
            return (IReadOnlyList<VariableNode>)[.. children.Select(ToNode)];
        });

    private async Task RefreshWatchAsync()
    {
        if (_watch is null) return;

        if (!_debug.IsPaused)
        {
            _watch.ClearValues();
            return;
        }

        await _watch.RefreshAsync(expression => _debug.EvaluateAsync(expression));
    }

    /// <summary>Clears everything that only means something while stopped.</summary>
    private void OnDebuggerResumed()
    {
        _callStack?.Clear();
        _variables?.Clear();
        _watch?.ClearValues();

        foreach (var margin in _margins.Values) margin.ShowCurrentLine(0);
    }

    /// <summary>Redraws the breakpoints of a file, in the margin and the list.</summary>
    private void RefreshBreakpointDisplay(string filePath)
    {
        if (_margins.TryGetValue(filePath, out var margin))
        {
            var breakpoints = _debug.BreakpointsIn(filePath);

            margin.Show(
                [.. breakpoints.Select(b => b.Line)],
                [.. breakpoints.Where(b => !b.Enabled).Select(b => b.Line)]);
        }

        _breakpoints?.Show(_debug.AllBreakpoints);
    }

    /// <summary>
    /// The debugging keys, which Visual Basic users already know.
    ///
    /// These live here rather than in the XAML bindings because they act on
    /// the debugging session, which belongs to the window and not to the
    /// view model.
    /// </summary>
    /// <summary>The language of a file, as the status bar names it.</summary>
    private static string DescribeLanguage(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".vb" => "Visual Basic",
            ".cs" => "C#",
            ".vbhtml" => "Razor (VB)",
            ".cshtml" or ".razor" => "Razor (C#)",
            ".bas" or ".qb" => "QuickBASIC",
            ".html" or ".htm" => "HTML",
            ".css" => "CSS",
            ".js" => "JavaScript",
            ".json" => "JSON",
            ".xml" or ".vbproj" or ".csproj" => "XML",
            ".axaml" or ".xaml" => "XAML",
            _ => "Text"
        };

    /// <summary>Shows what the debugger is doing, or nothing when it is idle.</summary>
    private void ShowDebugState()
    {
        DebugStateLabel.Text = _debug switch
        {
            { IsPaused: true } => "Paused",
            { IsRunning: true } => "Running",
            _ => ""
        };
    }

    /// <summary>
    /// Offers the fixes for the problem at the caret.
    ///
    /// The same review step as a rename: a fix that adds an import changes the
    /// file just as surely, and the user should see what it will do.
    /// </summary>
    private async Task ShowQuickActionsAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        // The workspace holds the file as last saved; a fix worked out from
        // that would apply to text the user has since changed.
        await ViewModel.SyncActiveDocumentAsync();

        var actions = await ViewModel.GetQuickActionsAsync(document.FilePath, editor.CaretOffset);

        if (actions.Count == 0)
        {
            ViewModel.WriteOutput("[editor] Nothing to fix here.");
            return;
        }

        var chosen = await AskForChoiceAsync(
            "Quick Actions", "Apply:", [.. actions.Select(a => a.Title)]);

        if (chosen is not { Length: > 0 }) return;

        var action = actions.First(a => a.Title == chosen);

        var preview = await ViewModel.PreviewQuickActionAsync(document.FilePath, action);

        await ApplyRefactoringWithPreviewAsync(preview);
    }

    /// <summary>
    /// Sorts the file's imports and drops the ones nothing uses.
    ///
    /// Shown first like the others: an import that looks unused can be one
    /// this does not see through, so the user gets to look.
    /// </summary>
    /// <summary>
    /// Offers the @Imports that would make the name under the caret resolve.
    ///
    /// The part the author cannot do without going to look: which namespace
    /// declares the type.
    /// </summary>
    private async Task AddImportAsync()
    {
        if (ActingDocument is not { } document) return;
        if (CurrentEditor() is not { } editor) return;

        await ViewModel.SyncActiveDocumentAsync();

        var name = WordAt(editor.Text, editor.CaretOffset);

        if (name.Length == 0)
        {
            ViewModel.WriteOutput("[editor] There is no name under the caret.");
            return;
        }

        await ApplyRefactoringWithPreviewAsync(
            await ViewModel.PreviewAddImportAsync(document.FilePath, name));
    }

    /// <summary>
    /// Moves the selected markup into a partial view of its own.
    ///
    /// Only for templates: there is no partial to extract into from a .vb.
    /// </summary>
    private async Task ExtractPartialAsync()
    {
        if (ActingDocument is not { } document) return;
        if (CurrentEditor() is not { } editor) return;

        if (!document.FilePath.EndsWith(".vbhtml", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.WriteOutput("[editor] A partial view can only come from a template.");
            return;
        }

        if (editor.SelectionLength <= 0)
        {
            ViewModel.WriteOutput("[editor] Select the markup to extract first.");
            return;
        }

        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        var text = editor.Text;

        var name = await AskForTextAsync(
            "Extract to Partial View", "Name the partial:", "Row");

        if (name is not { Length: > 0 }) return;

        await ApplyRefactoringWithPreviewAsync(MainWindowViewModel.PreviewExtractPartial(
            document.FilePath, text, start, length, name));
    }

    private async Task TidyImportsAsync()
    {
        if (ActingDocument is not { } document) return;

        await ViewModel.SyncActiveDocumentAsync();

        await ApplyRefactoringWithPreviewAsync(
            await ViewModel.PreviewTidyImportsAsync(document.FilePath));
    }

    /// <summary>
    /// Names the selected expression as a constant.
    ///
    /// The same shape as extracting a variable, and the same preview: the
    /// difference is one word of the declaration and that an expression not
    /// known at compile time is refused.
    /// </summary>
    private async Task ExtractConstantAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        if (editor.SelectionLength <= 0)
        {
            ViewModel.WriteOutput("[editor] Select the expression to extract first.");
            return;
        }

        var start = editor.SelectionStart;
        var length = editor.SelectionLength;

        await ViewModel.SyncActiveDocumentAsync();

        var name = await AskForTextAsync("Extract Constant", "Name the constant:", "Value");

        if (name is not { Length: > 0 }) return;

        var preview = await ViewModel.PreviewExtractConstantAsync(
            document.FilePath, start, length, name);

        await ApplyRefactoringWithPreviewAsync(preview);
    }

    /// <summary>
    /// Puts a variable's value back where it was used.
    ///
    /// From the caret rather than a selection: the variable is wherever the
    /// caret is, which is how the same command works elsewhere.
    /// </summary>
    private async Task InlineVariableAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var position = editor.CaretOffset;

        await ViewModel.SyncActiveDocumentAsync();

        var preview = await ViewModel.PreviewInlineVariableAsync(document.FilePath, position);

        await ApplyRefactoringWithPreviewAsync(preview);
    }

    /// <summary>
    /// Turns the If the caret is in into a Select Case, or back.
    ///
    /// Which way round follows what the caret is in, so it is one command
    /// rather than two the user has to choose between.
    /// </summary>
    private async Task ConvertConditionalAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var position = editor.CaretOffset;

        await ViewModel.SyncActiveDocumentAsync();

        var preview = await ViewModel.PreviewConvertConditionalAsync(
            document.FilePath, position);

        await ApplyRefactoringWithPreviewAsync(preview);
    }

    /// <summary>Writes a constructor taking the fields of the type the caret is in.</summary>
    private async Task GenerateConstructorAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var position = editor.CaretOffset;

        await ViewModel.SyncActiveDocumentAsync();

        await ApplyRefactoringWithPreviewAsync(
            await ViewModel.PreviewGenerateConstructorAsync(document.FilePath, position));
    }

    /// <summary>Writes a property in front of the field the caret is on.</summary>
    private async Task GeneratePropertyAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var position = editor.CaretOffset;

        await ViewModel.SyncActiveDocumentAsync();

        await ApplyRefactoringWithPreviewAsync(
            await ViewModel.PreviewGeneratePropertyAsync(document.FilePath, position));
    }

    /// <summary>
    /// Moves the type the caret is in into a file named after it.
    ///
    /// Two files change, and the preview shows both before either is written.
    /// </summary>
    private async Task MoveTypeToFileAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var position = editor.CaretOffset;

        await ViewModel.SyncActiveDocumentAsync();

        await ApplyRefactoringWithPreviewAsync(
            await ViewModel.PreviewMoveTypeToFileAsync(document.FilePath, position));
    }

    /// <summary>
    /// Changes a method's parameters, and every call to it.
    ///
    /// The parameters are moved and dropped in a list rather than retyped:
    /// typing a signature again invites a typo the preview would then apply
    /// faithfully.
    /// </summary>
    private async Task ChangeSignatureAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var position = editor.CaretOffset;

        await ViewModel.SyncActiveDocumentAsync();

        var parameters = await ViewModel.GetParametersAsync(document.FilePath, position);

        if (parameters.Count == 0)
        {
            ViewModel.WriteOutput(
                "[editor] Put the caret in a Sub or a Function that takes parameters.");
            return;
        }

        var dialog = new ChangeSignatureDialog(parameters);

        await dialog.ShowDialog(this);

        if (dialog.Result is not { } change) return;

        await ApplyRefactoringWithPreviewAsync(
            await ViewModel.PreviewChangeSignatureAsync(document.FilePath, position, change));
    }

    /// <summary>
    /// Shows the Visual Basic a template turns into.
    ///
    /// Razor's own tooling exposes this, and for the same reason: when a
    /// position maps wrongly, or an error lands on a line nobody wrote, the
    /// generated code is the only place the answer is. Without it a mapping
    /// bug is a mystery.
    /// </summary>
    private async Task ShowGeneratedCodeAsync()
    {
        if (ActingDocument is not { } document) return;

        if (!document.FilePath.EndsWith(".vbhtml", StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.WriteOutput(
                "[editor] Only a .vbhtml template is generated into Visual Basic.");
            return;
        }

        await ViewModel.SyncActiveDocumentAsync();

        var parsed = Basalt.Razor.Vb.VbHtmlParser.Parse(document.Text);

        var generated = Basalt.Razor.Vb.VbHtmlCodeWriter.WriteWithMap(
            parsed,
            Path.GetFileNameWithoutExtension(document.FilePath),
            "Views",
            document.FilePath);

        // Written beside the template rather than into a scratch folder, so
        // it opens in the editor like any other file and can be read with the
        // template next to it.
        var path = document.FilePath + ".generated.vb";

        try
        {
            await File.WriteAllTextAsync(path, generated.Code);

            await ViewModel.OpenFileAsync(path);

            ViewModel.WriteOutput(
                $"[editor] {generated.Map.Mappings.Count} mapped region"
              + $"{(generated.Map.Mappings.Count == 1 ? "" : "s")} between the template "
              + "and the generated code.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.WriteOutput($"[editor] {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a refactoring, applies it if accepted, and reloads what changed.
    ///
    /// The tail every refactoring shares, so a new one is the preview and
    /// nothing else.
    /// </summary>
    /// <summary>
    /// Shows a preview, writes it if the user accepts, and puts the result on
    /// screen.
    ///
    /// Every refactoring ends here. Written once because the last step is the
    /// one easy to forget, and forgetting it means the file changes while the
    /// editor goes on showing the old text.
    /// </summary>
    private async Task ApplyRefactoringWithPreviewAsync(
        Basalt.Workspace.Refactoring.RefactoringPreview preview)
    {
        if (!preview.CanApply)
        {
            ViewModel.WriteOutput($"[editor] {preview.Problem}");
            return;
        }

        var dialog = new RefactoringPreviewDialog(preview);

        await dialog.ShowDialog(this);

        if (!dialog.Accepted) return;

        if (!await MainWindowViewModel.ApplyRefactoringAsync(preview))
        {
            ViewModel.WriteOutput("[editor] The change could not be written.");
            return;
        }

        await ReloadOpenDocumentsAsync(preview.Changes.Select(c => c.FilePath));
    }

    /// <summary>
    /// Lifts the selected statements into a method of their own.
    ///
    /// The signature comes from what the statements read and write, so the
    /// preview is also where the user checks that it came out as intended.
    /// </summary>
    private async Task ExtractMethodAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        if (editor.SelectionLength <= 0)
        {
            ViewModel.WriteOutput("[editor] Select the statements to extract first.");
            return;
        }

        var start = editor.SelectionStart;
        var length = editor.SelectionLength;

        await ViewModel.SyncActiveDocumentAsync();

        var name = await AskForTextAsync("Extract Method", "Name the method:", "Helper");

        if (name is not { Length: > 0 }) return;

        var preview = await ViewModel.PreviewExtractMethodAsync(
            document.FilePath, start, length, name);

        await ApplyRefactoringWithPreviewAsync(preview);
    }

    /// <summary>
    /// Gives the selected expression a name.
    ///
    /// Shown before it is applied like the other refactorings, though this one
    /// touches a single file: the preview is where the user sees that the
    /// selection was read as the expression they meant.
    /// </summary>
    private async Task ExtractVariableAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        if (editor.SelectionLength <= 0)
        {
            ViewModel.WriteOutput("[editor] Select the expression to extract first.");
            return;
        }

        var start = editor.SelectionStart;
        var length = editor.SelectionLength;

        await ViewModel.SyncActiveDocumentAsync();

        var name = await AskForTextAsync("Extract Variable", "Name the expression:", "value");

        if (name is not { Length: > 0 }) return;

        var preview = await ViewModel.PreviewExtractVariableAsync(
            document.FilePath, start, length, name);

        await ApplyRefactoringWithPreviewAsync(preview);
    }

    /// <summary>
    /// Renames the symbol under the caret, everywhere it is used.
    ///
    /// The change is shown before it is made: a rename can touch a dozen
    /// files, and undoing it afterwards means undoing each one.
    /// </summary>
    private async Task RenameSymbolAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var currentName = WordAt(editor.Text, editor.CaretOffset);

        if (currentName.Length == 0)
        {
            ViewModel.WriteOutput("[rename] Put the caret on a name first.");
            return;
        }

        var newName = await AskForTextAsync("Rename", $"Rename '{currentName}' to:", currentName);

        if (newName is not { Length: > 0 } || newName == currentName) return;

        // The workspace has the file as it was last saved; the editor may be
        // ahead of it, and renaming from a stale copy would move the wrong
        // text.
        await ViewModel.SyncActiveDocumentAsync();

        var preview = await ViewModel.PreviewRenameAsync(
            document.FilePath, editor.CaretOffset, newName);

        var dialog = new RefactoringPreviewDialog(preview);

        await dialog.ShowDialog(this);

        if (!dialog.Accepted) return;

        if (!await MainWindowViewModel.ApplyRefactoringAsync(preview))
        {
            ViewModel.WriteOutput("[rename] The change could not be written.");
            return;
        }

        await ReloadOpenDocumentsAsync(preview.Changes.Select(c => c.FilePath));
    }

    /// <summary>
    /// Reads changed files back into the editors showing them.
    ///
    /// Without this the user sees the old text and saving would undo the
    /// refactoring.
    /// </summary>
    private async Task ReloadOpenDocumentsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var open = ViewModel.OpenDocuments.FirstOrDefault(
                d => string.Equals(d.FilePath, path, StringComparison.Ordinal));

            if (open is null) continue;

            try
            {
                open.Text = await File.ReadAllTextAsync(path);
                open.MarkSaved();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViewModel.WriteOutput($"[rename] {ex.Message}");
            }
        }
    }

    /// <summary>Asks for a line of text, returning null if the user gave up.</summary>
    private async Task<string?> AskForTextAsync(string title, string prompt, string initial)
    {
        var box = new TextBox { Text = initial };

        var answered = false;

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var dialog = new Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        ok.Click += (_, _) => { answered = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt, FontSize = 12 },
                box,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok }
                }
            }
        };

        dialog.Opened += (_, _) => box.SelectAll();

        await dialog.ShowDialog(this);

        return answered ? (box.Text ?? "").Trim() : null;
    }

    /// <summary>Opens the branch list.</summary>
    private void OnStatusBranch(object? sender, RoutedEventArgs e) =>
        ShowTool(_factory.GitBranches);

    /// <summary>Opens the problems panel.</summary>
    private void OnStatusErrors(object? sender, RoutedEventArgs e) =>
        ShowTool(_factory.Problems);

    /// <summary>Asks for a line and goes there.</summary>
    private async void OnStatusGoToLine(object? sender, RoutedEventArgs e)
    {
        if (CurrentEditor() is not { } editor) return;

        var line = await AskForNumberAsync(
            "Go to Line", "Line number:", editor.TextArea.Caret.Line);

        if (line is { } wanted) GoToLine(Math.Clamp(wanted, 1, editor.Document.LineCount));
    }

    /// <summary>
    /// Switches between tabs and spaces, and their width.
    ///
    /// The setting is the one the editor already has, so this is a shortcut to
    /// the settings rather than a second place the answer is kept.
    /// </summary>
    private async void OnStatusIndentation(object? sender, RoutedEventArgs e)
    {
        if (CurrentEditor() is null) return;

        var settings = _settingsStore.Load();

        var chosen = await AskForChoiceAsync("Indentation", "Use:",
        [
            "Spaces: 2", "Spaces: 4", "Spaces: 8", "Tabs: 4", "Tabs: 8"
        ]);

        if (chosen is not { Length: > 0 }) return;

        var parts = chosen.Split(':', StringSplitOptions.TrimEntries);

        settings.Editor.ConvertTabsToSpaces = parts[0] == "Spaces";
        settings.Editor.IndentationSize = int.TryParse(parts[1], out var width) ? width : 4;

        _settingsStore.Save(settings);
        ApplySettings(settings);
        ShowCaretPosition();
    }

    /// <summary>Converts the document's line endings.</summary>
    private async void OnStatusLineEnding(object? sender, RoutedEventArgs e)
    {
        if (CurrentEditor() is not { } editor) return;

        var chosen = await AskForChoiceAsync(
            "Line Endings", "Convert to:", ["LF", "CRLF"]);

        if (chosen is not { Length: > 0 }) return;

        var ending = chosen == "CRLF" ? "\r\n" : "\n";

        // Normalised first, so a file with both does not end up with three
        // kinds of line ending.
        var text = editor.Text.Replace("\r\n", "\n");

        if (ending == "\r\n") text = text.Replace("\n", "\r\n");

        var caret = editor.CaretOffset;

        editor.Document.Replace(0, editor.Document.TextLength, text);
        editor.CaretOffset = Math.Clamp(caret, 0, editor.Document.TextLength);

        ShowCaretPosition();
    }

    /// <summary>Asks for a number, returning null if the user gave up.</summary>
    private async Task<int?> AskForNumberAsync(string title, string prompt, int initial)
    {
        var box = new TextBox { Text = initial.ToString() };

        var answered = false;

        var ok = new Button { Content = "Go", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var dialog = new Window
        {
            Title = title,
            Width = 280,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        ok.Click += (_, _) => { answered = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt, FontSize = 12 },
                box,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok }
                }
            }
        };

        dialog.Opened += (_, _) => box.SelectAll();

        await dialog.ShowDialog(this);

        return answered && int.TryParse(box.Text, out var value) ? value : null;
    }

    /// <summary>Asks the user to pick one of a few options.</summary>
    private async Task<string?> AskForChoiceAsync(
        string title, string prompt, IReadOnlyList<string> options)
    {
        var list = new ListBox { ItemsSource = options, SelectedIndex = 0, Height = 140 };

        var answered = false;

        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        var dialog = new Window
        {
            Title = title,
            Width = 260,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        ok.Click += (_, _) => { answered = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        list.DoubleTapped += (_, _) => { answered = true; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt, FontSize = 12 },
                list,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok }
                }
            }
        };

        await dialog.ShowDialog(this);

        return answered ? list.SelectedItem as string : null;
    }

    /// <summary>
    /// Runs whatever the pressed keys are bound to.
    ///
    /// Every shortcut goes through the registry rather than being matched
    /// here: keys written into this method would keep firing after the user
    /// reassigned them, which is the whole point of making them configurable.
    /// </summary>
    private async void OnKeyboardShortcut(object? sender, KeyEventArgs e)
    {
        try
        {
            e.Handled = await TryRunShortcutAsync(e);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // An event handler cannot be awaited, so a failure is reported
            // rather than left to bring the application down.
            ViewModel.WriteOutput($"[command] {ex.Message}");
        }
    }

    /// <summary>
    /// Starts debugging, building first.
    ///
    /// While already stopped this continues instead, which is what F5 does in
    /// Visual Basic and what the key is muscle memory for.
    /// </summary>
    private async Task StartOrContinueDebuggingAsync()
    {
        if (_debug.IsPaused)
        {
            await _debug.ContinueAsync();
            return;
        }

        if (_debug.IsRunning) return;

        // A QuickBASIC program is debugged by interpreting its source, so
        // there is nothing to build first: the file itself is what runs.
        if (ViewModel.ActiveDocument is { FilePath: var path }
            && Path.GetExtension(path).Equals(".bas", StringComparison.OrdinalIgnoreCase))
        {
            // Saved first: the interpreter reads the file from disk, and
            // debugging text the user has since changed would step through a
            // different program than the one on screen.
            await ViewModel.ActiveDocument.SaveAsync();

            await _debug.StartAsync(path, Path.GetDirectoryName(path));
            return;
        }

        var assembly = await ViewModel.BuildForDebuggingAsync();

        if (assembly is null)
        {
            // Saying "no assembly" when the build failed names the symptom and
            // hides the cause. The errors are already in the Problems panel,
            // so the message points there and brings it to the front.
            ReportWhyThereIsNothingToDebug();
            return;
        }

        await _debug.StartAsync(assembly, ViewModel.StartupDirectory);
    }

    /// <summary>
    /// Explains why a debugging session could not start.
    ///
    /// Three different reasons look the same from here — no project, a build
    /// that failed, a build that succeeded and produced nothing — and only
    /// one of them is about an assembly.
    /// </summary>
    private void ReportWhyThereIsNothingToDebug()
    {
        if (ViewModel.StartupProject is null)
        {
            ViewModel.WriteOutput(
                "[debug] No project to debug: open a solution, or choose a "
              + "startup project in the toolbar.");
            return;
        }

        var errors = ViewModel.Diagnostics
            .Where(d => d.Severity == Basalt.Core.Model.DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            ViewModel.WriteOutput(
                $"[debug] The build failed with {errors.Count} error(s); "
              + "nothing was produced to debug.");

            // The first one by name: it is usually the one to fix.
            ViewModel.WriteOutput($"[debug] {errors[0].Message}");

            if (_factory.BottomArea is not null)
                _factory.ShowTool(_factory.Problems, _factory.BottomArea);

            return;
        }

        ViewModel.WriteOutput(
            $"[debug] The build succeeded but no assembly was found under "
          + $"bin. Is '{Path.GetFileName(ViewModel.StartupProject)}' a library "
          + "with no entry point?");
    }

    /// <summary>
    /// Fills the toolbar.
    ///
    /// Grouped as the work is: opening and saving, then editing, then building
    /// and running, then debugging.
    /// </summary>
    private void BuildToolbar()
    {
        var toolbar = new IdeToolbar();

        // Every button carries the registry command it stands for, which is
        // also how a configuration names it: a stored list of ids survives
        // the buttons being reordered or retitled.
        var available = new List<ToolbarAction>
        {
            new ToolbarAction(IconKind.New, Localizer.Get(StringKeys.MenuFileNewSolution),
                () => _ = ShowNewSolutionDialogAsync()) { CommandId = IdeCommands.FileNewSolution },
            new ToolbarAction(IconKind.Open, Localizer.Get(StringKeys.MenuFileOpenSolution),
                () => _ = ShowOpenSolutionDialogAsync())
                { CommandId = IdeCommands.FileOpenSolution },
            new ToolbarAction(IconKind.Save, Localizer.Get(StringKeys.MenuFileSave), () => { })
                { Command = ViewModel.SaveCommand, CommandId = IdeCommands.FileSave },

            new ToolbarAction(IconKind.Undo, Localizer.Get(StringKeys.MenuEditUndo), () => { })
                { Command = ViewModel.UndoCommand, StartsGroup = true, CommandId = IdeCommands.EditUndo },
            new ToolbarAction(IconKind.Redo, Localizer.Get(StringKeys.MenuEditRedo), () => { })
                { Command = ViewModel.RedoCommand, CommandId = IdeCommands.EditRedo },
            new ToolbarAction(IconKind.Find, Localizer.Get(StringKeys.MenuEditFind), ShowSearch)
                { CommandId = IdeCommands.EditFind },
            new ToolbarAction(IconKind.Back, Localizer.Get(StringKeys.MenuNavigateBack),
                () => _ = NavigateAsync(ViewModel.History.GoBack()))
                { StartsGroup = true, CommandId = IdeCommands.NavigateBack },
            new ToolbarAction(IconKind.Forward, Localizer.Get(StringKeys.MenuNavigateForward),
                () => _ = NavigateAsync(ViewModel.History.GoForward()))
                { CommandId = IdeCommands.NavigateForward },

            new ToolbarAction(IconKind.Build, Localizer.Get(StringKeys.MenuBuildBuildSolution),
                () => { })
                { Command = ViewModel.BuildCommand, StartsGroup = true,
                  CommandId = IdeCommands.BuildSolution },
            new ToolbarAction(IconKind.Run, Localizer.Get(StringKeys.MenuRunWithoutDebugging),
                () => { })
                { Command = ViewModel.RunCommand, CommandId = IdeCommands.DebugStartWithout },
            new ToolbarAction(IconKind.Stop, Localizer.Get(StringKeys.MenuRunStop),
                () => { })
                { Command = ViewModel.StopRunCommand, CommandId = IdeCommands.DebugStop },

            new ToolbarAction(IconKind.Debug, Localizer.Get(StringKeys.MenuDebugStart),
                () => _ = StartOrContinueDebuggingAsync())
                { StartsGroup = true, CommandId = IdeCommands.DebugStart },
            new ToolbarAction(IconKind.StepOver, Localizer.Get(StringKeys.MenuDebugStepOver),
                () => _ = _debug.StepOverAsync()) { CommandId = IdeCommands.DebugStepOver },
            new ToolbarAction(IconKind.StepInto, Localizer.Get(StringKeys.MenuDebugStepInto),
                () => _ = _debug.StepIntoAsync()) { CommandId = IdeCommands.DebugStepInto },
            new ToolbarAction(IconKind.StepOut, Localizer.Get(StringKeys.MenuDebugStepOut),
                () => _ = _debug.StepOutAsync()) { CommandId = IdeCommands.DebugStepOut },

            new ToolbarAction(IconKind.Tests, Localizer.Get(StringKeys.MenuTestRunAll),
                () => _ = RunTestsAsync(TestExplorerPanel.RunScope.All))
                { StartsGroup = true, CommandId = IdeCommands.TestRunAll },
            new ToolbarAction(IconKind.Commit, Localizer.Get(StringKeys.MenuGitCommit),
                () => ShowTool(_factory.GitChanges))
                { CommandId = IdeCommands.GitCommit },

            new ToolbarAction(IconKind.Terminal, Localizer.Get(StringKeys.MenuViewNewTerminal),
                OpenNewTerminal)
                { StartsGroup = true, CommandId = IdeCommands.ViewTerminal },
            new ToolbarAction(IconKind.Settings, Localizer.Get(StringKeys.MenuToolsSettings),
                () => _ = ShowSettingsAsync()) { CommandId = IdeCommands.ToolsSettings }
        };

        _toolbarButtons = available;

        toolbar.Show(ChosenToolbarButtons(available));

        // Not buttons: these are chosen from a list, and they belong on the
        // toolbar because they change what Build and Run actually do.
        if (!_settingsStore.Load().Toolbar.ShowChoosers)
        {
            ToolbarHost.Child = toolbar;
            _toolbar = toolbar;
            return;
        }

        // The chooser used to store the answer and nothing else: picking
        // Release still built Debug, and then looked for the result in the
        // Debug folder — right by accident.
        ViewModel.Configuration = _settingsStore.Load().Build.Configuration;

        toolbar.Add(new ToolbarChooser(
            "Build configuration",
            ["Debug", "Release"],
            _settingsStore.Load().Build.Configuration,
            chosen => Apply(() =>
            {
                var settings = _settingsStore.Load();
                settings.Build.Configuration = chosen;
                _settingsStore.Save(settings);

                ViewModel.Configuration = chosen;
            }))
        { StartsGroup = true, Width = 100 });

        AddStartupProjectChooser(toolbar);

        ToolbarHost.Child = toolbar;

        _toolbar = toolbar;
    }

    /// <summary>
    /// Every button the toolbar could show, in the order it comes with.
    ///
    /// Kept so that the settings window can offer them and the toolbar can be
    /// rebuilt without going through the window's construction again.
    /// </summary>
    private List<ToolbarAction> _toolbarButtons = [];

    /// <summary>
    /// The buttons to show, following what was configured.
    ///
    /// An empty configuration means the ones it comes with, so a user who has
    /// never chosen still gets buttons added in a later version. An id that
    /// no longer exists is passed over rather than leaving a gap.
    /// </summary>
    private IReadOnlyList<ToolbarAction> ChosenToolbarButtons(
        IReadOnlyList<ToolbarAction> available)
    {
        var chosen = _settingsStore.Load().Toolbar.Buttons;

        if (chosen.Count == 0) return available;

        var byId = available
            .Where(a => a.CommandId is not null)
            .ToDictionary(a => a.CommandId!, StringComparer.Ordinal);

        var wanted = chosen
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();

        // A separator wherever the group changes, rather than the ones the
        // default order carries: those would fall in the wrong places once
        // the buttons have been reordered.
        var withGroups = new List<ToolbarAction>();

        string? lastGroup = null;

        foreach (var action in wanted)
        {
            var group = SettingsWindow.ToolbarGroupOf(action.CommandId!);

            withGroups.Add(action with
            {
                StartsGroup = group is not null && lastGroup is not null && group != lastGroup
            });

            if (group is not null) lastGroup = group;
        }

        return withGroups;
    }

    /// <summary>
    /// Builds the toolbar again after its configuration changed.
    ///
    /// The whole toolbar rather than the buttons alone: the choosers sit
    /// among them and their place depends on what is shown.
    /// </summary>
    internal void RefreshToolbar() => BuildToolbar();

    /// <summary>Every button the toolbar can show, for the settings window.</summary>
    internal IReadOnlyList<(string Id, string Title)> AvailableToolbarButtons() =>
    [
        .. _toolbarButtons
            .Where(a => a.CommandId is not null)
            .Select(a => (a.CommandId!, a.Tooltip))
    ];

    /// <summary>Puts back the startup project the user last chose.</summary>
    private void RestoreStartupProject()
    {
        var saved = _settingsStore.Load().Build.StartupProject;

        if (saved is { Length: > 0 }) ViewModel.SetStartupProject(saved);
    }

    /// <summary>
    /// Offers the projects that can be run.
    ///
    /// Only when there is a choice to make: a solution with one runnable
    /// project would get a list of one, which is clutter rather than a
    /// setting.
    /// </summary>
    private void AddStartupProjectChooser(IdeToolbar toolbar)
    {
        var projects = ViewModel.RunnableProjects;

        if (projects.Count < 2) return;

        var names = projects
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name ?? "")
            .ToList();

        var selected = ViewModel.StartupProject is { } current
            ? Path.GetFileNameWithoutExtension(current) ?? names[0]
            : names[0];

        toolbar.Add(new ToolbarChooser(
            "Startup project",
            names,
            selected,
            chosen =>
            {
                var picked = projects.FirstOrDefault(
                    p => Path.GetFileNameWithoutExtension(p) == chosen);

                if (picked is null) return;

                ViewModel.SetStartupProject(picked);

                var settings = _settingsStore.Load();
                settings.Build.StartupProject = picked;
                _settingsStore.Save(settings);
            })
        { Width = 150 });
    }

    /// <summary>The toolbar, so its buttons can be greyed as the state changes.</summary>
    private IdeToolbar? _toolbar;

    /// <summary>Runs something and keeps the toolbar in step with it.</summary>
    private void Apply(Action change)
    {
        change();
        _toolbar?.RefreshAvailability();
    }

    /// <summary>Shows what this is and which version is running.</summary>
    private void ShowAbout() => _ = new AboutWindow().ShowDialog(this);

    /// <summary>
    /// Shows where the caret is and how the file is stored.
    ///
    /// Read from the editor rather than tracked separately: the editor already
    /// knows, and a second copy of that state would be one more thing to keep
    /// in step.
    /// </summary>
    private void ShowCaretPosition()
    {
        if (CurrentEditor() is not { } editor)
        {
            CaretPositionLabel.Text = "";
            LineEndingLabel.Text = "";
            EncodingLabel.Text = "";
            IndentationLabel.Text = "";
            LanguageLabel.Text = "";
            return;
        }

        IndentationLabel.Text = editor.Options.ConvertTabsToSpaces
            ? $"Spaces: {editor.Options.IndentationSize}"
            : $"Tabs: {editor.Options.IndentationSize}";

        LanguageLabel.Text = ViewModel.ActiveDocument is { } document
            ? _languages.TryGetValue(document.FilePath, out var told)
                ? told
                : DescribeLanguage(document.FilePath)
            : "";

        var caret = editor.TextArea.Caret;
        var selected = editor.SelectionLength;

        CaretPositionLabel.Text = selected > 0
            ? $"Ln {caret.Line}, Col {caret.Column} ({selected} selected)"
            : $"Ln {caret.Line}, Col {caret.Column}";

        LineEndingLabel.Text = IdeStatusBar.DescribeLineEnding(editor.Text);

        // What the file actually is, rather than what it usually is: the
        // label said UTF-8 whatever was on disk, which is a claim and not a
        // reading. Worked out once per document and remembered, since it
        // means reading the bytes.
        EncodingLabel.Text = _encodings.TryGetValue(
            ViewModel.ActiveDocument?.FilePath ?? "", out var encoding)
                ? encoding
                : "UTF-8";
    }

    /// <summary>
    /// What each open file was found to be encoded as.
    ///
    /// Remembered rather than worked out on every caret move, which is when
    /// the status bar is refreshed.
    /// </summary>
    private readonly Dictionary<string, string> _encodings = new(StringComparer.Ordinal);

    /// <summary>Works out an open file's encoding and shows it.</summary>
    private async Task RefreshEncodingAsync(string filePath)
    {
        _encodings[filePath] = await Basalt.Core.Text.FileEncoding
            .DetectAsync(filePath)
            .ConfigureAwait(true);

        ShowCaretPosition();
    }

    /// <summary>
    /// Reopens the active file as another encoding.
    ///
    /// Reopened rather than converted: the bytes on disk have not changed,
    /// and what was wrong was how they were read.
    /// </summary>
    private async void OnStatusEncoding(object? sender, RoutedEventArgs e)
    {
        if (ActingDocument is not { } document) return;

        var chosen = await AskForChoiceAsync(
            "Encoding", "Reopen this file as:", [.. Basalt.Core.Text.FileEncoding.Names]);

        if (chosen is not { Length: > 0 }) return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(document.FilePath);

            var text = Basalt.Core.Text.FileEncoding.For(chosen).GetString(bytes);

            // A byte order mark decodes as a character of its own, which
            // would otherwise appear at the top of the file.
            document.Text = text.TrimStart('\uFEFF');

            _encodings[document.FilePath] = chosen;

            ShowCaretPosition();

            ViewModel.WriteOutput($"[editor] Reopened as {chosen}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.WriteOutput($"[editor] {ex.Message}");
        }
    }

    /// <summary>
    /// Changes which language the active file is treated as.
    ///
    /// Useful where the extension does not say: a .txt holding Basic, or a
    /// file with no extension at all.
    /// </summary>
    private async void OnStatusLanguage(object? sender, RoutedEventArgs e)
    {
        if (ActingDocument is not { } document) return;

        var languages = new List<string> { "Visual Basic", "C#", "QuickBASIC", "Plain Text" };

        var chosen = await AskForChoiceAsync(
            "Language", "Treat this file as:", [.. languages]);

        if (chosen is not { Length: > 0 }) return;

        _languages[document.FilePath] = chosen;

        ShowCaretPosition();

        // Honest about how far this goes: the status bar follows the choice,
        // but the colouring is chosen from the file extension when the editor
        // is built, and re-colouring an open file is not written yet.
        ViewModel.WriteOutput(
            $"[editor] Treating this file as {chosen}. "
          + "The syntax colours still follow the file extension.");
    }

    /// <summary>
    /// The language each open file has been told to be, where the user said.
    ///
    /// Kept apart from the extension so that the choice survives the file
    /// being refreshed, and applies only to the file it was made for.
    /// </summary>
    private readonly Dictionary<string, string> _languages = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a terminal with the user's settings already applied.
    ///
    /// The shell is chosen here rather than afterwards: changing it on a
    /// running session would mean killing the shell the user is working in.
    /// </summary>
    private TerminalView NewTerminal(string workingDirectory)
    {
        var settings = _settingsStore.Load();

        var terminal = new TerminalView(workingDirectory, settings.Terminal.Shell);

        terminal.ApplySettings(settings);

        return terminal;
    }

    /// <summary>
    /// Opens the settings window.
    ///
    /// Changes are applied as they are made, so the window is not modal: the
    /// user can see the effect on the code behind it.
    /// </summary>
    private async Task ShowSettingsAsync()
    {
        var window = new SettingsWindow(_settingsStore, Languages);

        window.SettingsChanged += (_, settings) => ApplySettings(settings);

        await window.ShowDialog(this);
    }

    /// <summary>
    /// Applies settings to what is already open.
    ///
    /// Without this a font size takes effect only on the next file opened,
    /// which reads as the setting not working.
    /// </summary>
    private void ApplySettings(IdeSettings settings)
    {
        foreach (var editor in _contents.Values.OfType<CodeEditor>())
            editor.ApplySettings(settings);

        foreach (var terminal in _contents.Values.OfType<TerminalView>())
            terminal.ApplySettings(settings);

        ApplyTheme(settings.Appearance.Theme);
    }

    /// <summary>
    /// Switches the interface between light and dark.
    ///
    /// Set on the application rather than the window so that dialogs and
    /// floating panels follow: a floated tool window left in the old theme
    /// would look broken.
    /// </summary>
    private static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is not { } application) return;

        // IdeThemes is the one place that knows the variants, so a theme
        // added there needs no change here.
        application.RequestedThemeVariant =
            IdeThemes.VariantFor(theme) ?? ThemeVariant.Default;
    }

    /// <summary>
    /// Runs whatever command the pressed keys are bound to.
    ///
    /// Asking the registry rather than matching keys here is what makes a
    /// reassigned shortcut work: this method never learns which key does what.
    /// </summary>
    private async Task<bool> TryRunShortcutAsync(KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                  or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return false;
        }

        var gesture = ShortcutRecorder.Describe(e.Key, e.KeyModifiers);

        if (_commands.ForGesture(gesture) is not { } command) return false;

        await RunCommandAsync(command.Id);

        return true;
    }

    /// <summary>Opens the palette and runs whatever was chosen.</summary>
    private async Task ShowCommandPaletteAsync()
    {
        var palette = new CommandPalette(_commands);

        await palette.ShowDialog(this);

        if (palette.Chosen is { } command) await RunCommandAsync(command.Id);
    }

    /// <summary>
    /// Carries out a command by its id.
    ///
    /// The registry says what exists; this says what each one does. Keeping
    /// them apart is what lets the palette list everything without knowing
    /// how any of it works.
    /// </summary>
    private async Task RunCommandAsync(string commandId)
    {
        switch (commandId)
        {
            case IdeCommands.FileNewSolution:
                await ShowNewSolutionDialogAsync();
                break;

            case IdeCommands.FileSave:
                await ViewModel.SaveCommand.ExecuteAsync(null);
                break;

            case IdeCommands.EditUndo:
                ViewModel.UndoCommand.Execute(null);
                break;

            case IdeCommands.EditRedo:
                ViewModel.RedoCommand.Execute(null);
                break;

            case IdeCommands.EditFind:
            case IdeCommands.EditReplace:
                ShowSearch();
                break;

            case IdeCommands.EditFormatDocument:
                await ViewModel.FormatDocumentCommand.ExecuteAsync(null);
                break;

            case IdeCommands.ViewCommandPalette:
                await ShowCommandPaletteAsync();
                break;

            case IdeCommands.ViewSolutionExplorer:
                ShowTool(_factory.SolutionExplorer);
                break;

            case IdeCommands.ViewProblems:
                ShowTool(_factory.Problems);
                break;

            case IdeCommands.ViewOutput:
                ShowTool(_factory.Output);
                break;

            case IdeCommands.ViewTerminal:
                OpenNewTerminal();
                break;

            case IdeCommands.ViewTests:
                ShowTool(_factory.Tests);
                break;

            case IdeCommands.ViewAssistant:
                ShowTool(_factory.Assistant);
                break;

            case IdeCommands.NavigateGoToDefinition:
                await GoToDefinitionAsync();
                break;

            case IdeCommands.NavigateFindReferences:
                await FindReferencesAsync();
                break;

            case IdeCommands.NavigateGoToFile:
                await GoToSymbolInFileAsync();
                break;

            case IdeCommands.NavigateGoToSymbol:
                await GoToSymbolInSolutionAsync();
                break;

            case IdeCommands.NavigateBack:
                await NavigateAsync(ViewModel.History.GoBack());
                break;

            case IdeCommands.NavigateForward:
                await NavigateAsync(ViewModel.History.GoForward());
                break;

            case IdeCommands.BuildSolution:
                await ViewModel.BuildCommand.ExecuteAsync(null);
                break;

            case IdeCommands.DebugStart:
                await StartOrContinueDebuggingAsync();
                break;

            case IdeCommands.DebugStartWithout:
                await ViewModel.RunCommand.ExecuteAsync(null);
                break;

            case IdeCommands.DebugStop:
                await StopDebuggingAsync();
                break;

            // Stepping means nothing unless the program is stopped, and the
            // keys are the editor's the rest of the time.
            case IdeCommands.DebugStepOver when _debug.IsPaused:
                await _debug.StepOverAsync();
                break;

            case IdeCommands.DebugStepInto when _debug.IsPaused:
                await _debug.StepIntoAsync();
                break;

            case IdeCommands.DebugStepOut when _debug.IsPaused:
                await _debug.StepOutAsync();
                break;

            case IdeCommands.DebugStepOver:
            case IdeCommands.DebugStepInto:
            case IdeCommands.DebugStepOut:
                break;

            case IdeCommands.DebugToggleBreakpoint:
                await ToggleBreakpointAtCaretAsync();
                break;

            case IdeCommands.DebugRunToCursor
                when ViewModel.ActiveDocument is { } document && CurrentEditor() is { } editor:
                await RunToLineAsync(document.FilePath, editor.TextArea.Caret.Line);
                break;

            case IdeCommands.TestRunAll:
                await RunTestsAsync(TestExplorerPanel.RunScope.All);
                break;

            case IdeCommands.TestRunSelected:
                await RunTestsAsync(TestExplorerPanel.RunScope.Selected);
                break;

            case IdeCommands.TestRunFailed:
                await RunTestsAsync(TestExplorerPanel.RunScope.Failed);
                break;

            case IdeCommands.TestDebugSelected:
                await DebugSelectedTestAsync();
                break;

            case IdeCommands.GitInitialize:
                await StartRepositoryAsync();
                break;

            case IdeCommands.GitCommit:
                ShowTool(_factory.GitChanges);
                break;

            case IdeCommands.GitBranches:
                ShowTool(_factory.GitBranches);
                break;

            case IdeCommands.GitPull:
                await RunRemoteGitAsync(git => git.PullAsync());
                break;

            case IdeCommands.GitPush:
                await RunRemoteGitAsync(git => git.PushAsync());
                break;

            case IdeCommands.RefactorRename:
                await RenameSymbolAsync();
                break;

            case IdeCommands.RefactorQuickActions:
                await ShowQuickActionsAsync();
                break;

            case IdeCommands.RefactorExtractVariable:
                await ExtractVariableAsync();
                break;

            case IdeCommands.RefactorExtractMethod:
                await ExtractMethodAsync();
                break;

            // Chosen from the palette they used to do nothing: the editor's
            // own bindings cover the keyboard, and nothing covered the rest.
            case IdeCommands.EditCut:
                CurrentEditor()?.Cut();
                break;

            case IdeCommands.EditCopy:
                CurrentEditor()?.Copy();
                break;

            case IdeCommands.EditPaste:
                CurrentEditor()?.Paste();
                break;

            case IdeCommands.FileSaveAll:
                await ViewModel.SaveAllAsync();
                break;

            case IdeCommands.FileClose:
                ViewModel.CloseActiveDocument();
                break;

            case IdeCommands.EditToggleComment:
                ToggleComment();
                break;

            case IdeCommands.EditFormatSelection:
                await FormatSelectionAsync();
                break;

            case IdeCommands.NavigateGoToLine:
                await AskAndGoToLineAsync();
                break;

            case IdeCommands.BuildClean:
                await ViewModel.CleanAsync();
                break;

            case IdeCommands.BuildRebuild:
                await ViewModel.RebuildAsync();
                break;

            case IdeCommands.EditQuickInfo:
                await ShowQuickInfoAtCaretAsync();
                break;

            case IdeCommands.EditToggleFold:
                CurrentCodeEditor()?.ToggleFoldAtCaret();
                break;

            case IdeCommands.EditFoldAll:
                CurrentCodeEditor()?.FoldAll();
                break;

            case IdeCommands.EditUnfoldAll:
                CurrentCodeEditor()?.UnfoldAll();
                break;

            case IdeCommands.RefactorTidyImports:
                await TidyImportsAsync();
                break;

            case IdeCommands.RefactorAddImport:
                await AddImportAsync();
                break;

            case IdeCommands.RefactorExtractConstant:
                await ExtractConstantAsync();
                break;

            case IdeCommands.RefactorInlineVariable:
                await InlineVariableAsync();
                break;

            case IdeCommands.RefactorConvertConditional:
                await ConvertConditionalAsync();
                break;

            case IdeCommands.RefactorGenerateConstructor:
                await GenerateConstructorAsync();
                break;

            case IdeCommands.RefactorGenerateProperty:
                await GeneratePropertyAsync();
                break;

            case IdeCommands.RefactorMoveTypeToFile:
                await MoveTypeToFileAsync();
                break;

            case IdeCommands.RefactorChangeSignature:
                await ChangeSignatureAsync();
                break;

            case IdeCommands.ToolsSettings:
                await ShowSettingsAsync();
                break;

            case IdeCommands.ViewGeneratedCode:
                await ShowGeneratedCodeAsync();
                break;

            default:
                // A command in the registry with nothing behind it yet: said
                // plainly rather than silently doing nothing.
                ViewModel.WriteOutput(
                    $"[command] '{_commands.ById(commandId)?.Title ?? commandId}' "
                  + "is not implemented yet.");
                break;
        }
    }

    /// <summary>
    /// Connects the panels' context menus to what the window can do.
    ///
    /// The panels raise what was chosen; only the window can open a file,
    /// reach the clipboard or start a run.
    /// </summary>
    private void ConnectContextMenus()
    {
        if (_solutionExplorer is not null)
            _solutionExplorer.CommandChosen += (_, chosen) => Guarded.Run(
                () => RunExplorerCommandAsync(chosen.Command, chosen.Node),
                ViewModel.WriteOutput, "ide");

        if (_tests is not null)
            _tests.CommandChosen += (_, command) => Guarded.Run(
                () => RunTestCommandAsync(command),
                ViewModel.WriteOutput, "ide");

        if (_gitChanges is not null)
            _gitChanges.CommandChosen += (_, chosen) => Guarded.Run(
                () => RunChangeCommandAsync(chosen.Command, chosen.File),
                ViewModel.WriteOutput, "ide");

        if (_outline is not null)
            _outline.CommandChosen += (_, chosen) => Guarded.Run(
                async () =>
                {
                    if (chosen.Command == OutlinePanel.OutlineCommand.GoTo
                        && chosen.Symbol is { } symbol)
                    {
                        GoToLine(symbol.Range.Start.Line);
                    }
                    else if (chosen.Command == OutlinePanel.OutlineCommand.CopyName
                             && chosen.Symbol is { } named)
                    {
                        await CopyToClipboardAsync(named.Name);
                    }
                },
                ViewModel.WriteOutput, "ide");
    }

    private async Task RunExplorerCommandAsync(
        SolutionExplorerPanel.ExplorerCommand command, SolutionTreeNode? node)
    {
        switch (command)
        {
            case SolutionExplorerPanel.ExplorerCommand.Open when node is not null:
                await ViewModel.OpenFileAsync(node.Path);
                break;

            case SolutionExplorerPanel.ExplorerCommand.OpenInDesigner when node is not null:
                await ViewModel.OpenFileAsync(node.Path, inDesigner: true);
                break;

            case SolutionExplorerPanel.ExplorerCommand.CopyPath when node is not null:
                await CopyToClipboardAsync(node.Path);
                break;

            case SolutionExplorerPanel.ExplorerCommand.CopyRelativePath when node is not null:
                await CopyToClipboardAsync(RelativeToSolution(node.Path));
                break;

            case SolutionExplorerPanel.ExplorerCommand.RevealInFinder when node is not null:
                RevealInFileManager(node.Path);
                break;

            case SolutionExplorerPanel.ExplorerCommand.Refresh
                when ViewModel.SolutionPath is { } solution:
                ViewModel.Explorer.Load(solution);
                break;

            case SolutionExplorerPanel.ExplorerCommand.Properties:
                await ShowProjectPropertiesAsync();
                break;

            case SolutionExplorerPanel.ExplorerCommand.NewFile:
            case SolutionExplorerPanel.ExplorerCommand.NewFolder:
            case SolutionExplorerPanel.ExplorerCommand.Rename:
            case SolutionExplorerPanel.ExplorerCommand.Delete:
                // Each needs a dialog and a confirmation; saying so beats a
                // menu entry that quietly does nothing.
                ViewModel.WriteOutput("[explorer] Not implemented yet.");
                break;
        }
    }

    private async Task RunTestCommandAsync(TestExplorerPanel.TestCommand command)
    {
        switch (command)
        {
            case TestExplorerPanel.TestCommand.Run:
                await RunTestsAsync(TestExplorerPanel.RunScope.Selected);
                break;

            case TestExplorerPanel.TestCommand.Debug:
                await DebugSelectedTestAsync();
                break;

            case TestExplorerPanel.TestCommand.OpenSource
                when _tests?.SelectedResult is { FilePath: { Length: > 0 } path } result:
                await OpenAtAsync(path, result.Line, 1);
                break;

            case TestExplorerPanel.TestCommand.CopyName when _tests?.SelectedTest is { } test:
                await CopyToClipboardAsync(test.FullyQualifiedName);
                break;

            case TestExplorerPanel.TestCommand.CopyFailure
                when _tests?.SelectedResult is { } failure:
                await CopyToClipboardAsync(
                    $"{failure.Message}\n\n{failure.StackTrace}".Trim());
                break;
        }
    }

    private async Task RunChangeCommandAsync(
        GitChangesPanel.ChangeCommand command, FileChange file)
    {
        switch (command)
        {
            case GitChangesPanel.ChangeCommand.Stage:
                await RunGitAsync(git => git.StageAsync([file.Path]));
                break;

            case GitChangesPanel.ChangeCommand.Unstage:
                await RunGitAsync(git => git.UnstageAsync([file.Path]));
                break;

            case GitChangesPanel.ChangeCommand.Discard:
                await DiscardChangesAsync(file);
                break;

            case GitChangesPanel.ChangeCommand.OpenDiff:
                await ShowDiffAsync(file);
                break;

            case GitChangesPanel.ChangeCommand.OpenFile:
                await ViewModel.OpenFileAsync(AbsolutePathOf(file.Path));
                break;

            case GitChangesPanel.ChangeCommand.CopyPath:
                await CopyToClipboardAsync(file.Path);
                break;
        }
    }

    /// <summary>
    /// Throws away a file's changes, after asking.
    ///
    /// The one destructive entry in these menus, and the only one that cannot
    /// be undone: git keeps no record of what was never committed.
    /// </summary>
    private async Task DiscardChangesAsync(FileChange file)
    {
        var confirmed = await ConfirmAsync(
            "Discard changes?",
            $"The changes to {Path.GetFileName(file.Path)} will be lost. "
          + "This cannot be undone.");

        if (!confirmed) return;

        await RunGitAsync(git => git.DiscardChangesAsync([file.Path]));
    }

    /// <summary>Asks the user to confirm something that cannot be undone.</summary>
    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var answered = false;

        var yes = new Button { Content = "Discard", Classes = { "primary" } };
        var no = new Button { Content = "Cancel", IsCancel = true, IsDefault = true };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        yes.Click += (_, _) => { answered = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { no, yes }
                }
            }
        };

        await dialog.ShowDialog(this);

        return answered;
    }

    /// <summary>
    /// Puts text on the clipboard, where there is one.
    ///
    /// Avalonia 12 replaced SetTextAsync with a data transfer, which can carry
    /// more than text; a single text item is what these menus mean.
    /// </summary>
    private async Task CopyToClipboardAsync(string text)
    {
        if (Clipboard is not { } clipboard) return;

        var transfer = new Avalonia.Input.DataTransfer();

        transfer.Add(Avalonia.Input.DataTransferItem.CreateText(text));

        await clipboard.SetDataAsync(transfer);
    }

    /// <summary>A path as written from the solution's folder.</summary>
    private string RelativeToSolution(string path)
    {
        if (ViewModel.SolutionPath is not { Length: > 0 } solution) return path;

        var root = Path.GetDirectoryName(Path.GetFullPath(solution));

        return root is null ? path : Path.GetRelativePath(root, path);
    }

    /// <summary>Git reports paths from the repository root; files need full ones.</summary>
    private string AbsolutePathOf(string path)
    {
        if (Path.IsPathRooted(path)) return path;

        if (ViewModel.SolutionPath is not { Length: > 0 } solution) return path;

        var root = Path.GetDirectoryName(Path.GetFullPath(solution));

        return root is null ? path : Path.Combine(root, path);
    }

    /// <summary>
    /// Shows a file in the platform's file manager.
    ///
    /// Each system has its own command; a failure is reported rather than
    /// thrown, since it means only that the file manager did not open.
    /// </summary>
    private void RevealInFileManager(string path)
    {
        var (command, arguments) =
            OperatingSystem.IsMacOS() ? ("open", new[] { "-R", path })
            : OperatingSystem.IsWindows() ? ("explorer.exe", ["/select,", path])
            : ("xdg-open", [Path.GetDirectoryName(path) ?? path]);

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(command)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException)
        {
            ViewModel.WriteOutput($"[explorer] {ex.Message}");
        }
    }

    /// <summary>
    /// Carries out what the editor's context menu asked for.
    ///
    /// The commands are the window's, not the editor's: navigation needs the
    /// solution, and the assistant needs the panel.
    /// </summary>
    private async Task RunEditorCommandAsync(EditorCommand command, CodeEditor editor)
    {
        var text = editor.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>()
            .FirstOrDefault();

        if (text is null) return;

        // The editor the menu was opened on, for as long as the command runs.
        // Nineteen of these commands call helpers that look the editor up
        // through ActiveDocument, and a right click does not change which
        // document is active: with more than one file open they acted on the
        // wrong one.
        _actingEditor = editor;

        try
        {

            switch (command)
            {
                case EditorCommand.Cut:
                    text.Cut();
                    break;

                case EditorCommand.Copy:
                    text.Copy();
                    break;

                case EditorCommand.Paste:
                    text.Paste();
                    break;

                case EditorCommand.SelectAll:
                    text.SelectAll();
                    break;

                case EditorCommand.GoToDefinition:
                    await GoToDefinitionAsync();
                    break;

                case EditorCommand.FindReferences:
                    await FindReferencesAsync();
                    break;

                case EditorCommand.FormatDocument:
                    await ViewModel.FormatDocumentCommand.ExecuteAsync(null);
                    break;

                case EditorCommand.FormatSelection:
                    await editor.FormatCurrentLineAsync();
                    break;

                case EditorCommand.ToggleBreakpoint:
                    await ToggleBreakpointAtCaretAsync();
                    break;

                case EditorCommand.RunToHere when ViewModel.ActiveDocument is { } document:
                    await RunToLineAsync(document.FilePath, text.TextArea.Caret.Line);
                    break;

                case EditorCommand.ExplainWithAssistant:
                case EditorCommand.AskAssistant:
                    ShowTool(_factory.Assistant);
                    break;

                case EditorCommand.Rename:
                    await RenameSymbolAsync();
                    break;

                case EditorCommand.QuickActions:
                    await ShowQuickActionsAsync();
                    break;

                case EditorCommand.ExtractVariable:
                    await ExtractVariableAsync();
                    break;

                case EditorCommand.ExtractMethod:
                    await ExtractMethodAsync();
                    break;

                case EditorCommand.ExtractConstant:
                    await ExtractConstantAsync();
                    break;

                case EditorCommand.InlineVariable:
                    await InlineVariableAsync();
                    break;

                case EditorCommand.ConvertConditional:
                    await ConvertConditionalAsync();
                    break;

                case EditorCommand.GenerateConstructor:
                    await GenerateConstructorAsync();
                    break;

                case EditorCommand.GenerateProperty:
                    await GeneratePropertyAsync();
                    break;

                case EditorCommand.MoveTypeToFile:
                    await MoveTypeToFileAsync();
                    break;

                case EditorCommand.ChangeSignature:
                    await ChangeSignatureAsync();
                    break;

                case EditorCommand.AddImport:
                    await AddImportAsync();
                    break;

                case EditorCommand.ExtractPartial:
                    await ExtractPartialAsync();
                    break;

                case EditorCommand.TidyImports:
                    await TidyImportsAsync();
                    break;
            }
        }
        finally
        {
            _actingEditor = null;
        }
    }

    /// <summary>
    /// Asks for a breakpoint's condition and applies it.
    ///
    /// The breakpoint is created if the line has none: asking for a condition
    /// on a line without one plainly means wanting both.
    /// </summary>
    private async Task EditBreakpointConditionAsync(string filePath, int line)
    {
        var existing = _debug.BreakpointsIn(filePath).FirstOrDefault(b => b.Line == line);

        if (existing is null)
        {
            await _debug.ToggleBreakpointAsync(filePath, line);

            existing = _debug.BreakpointsIn(filePath).FirstOrDefault(b => b.Line == line);

            if (existing is null) return;
        }

        var dialog = new BreakpointConditionDialog(existing);

        await dialog.ShowDialog(this);

        if (dialog.Result is not { } updated) return;

        await _debug.SetBreakpointConditionAsync(filePath, line, updated.Condition);
        await _debug.SetBreakpointHitCountAsync(filePath, line, updated.HitCount);
    }

    /// <summary>
    /// Runs as far as a line.
    ///
    /// A breakpoint is set, the program continued, and the breakpoint removed
    /// again unless the user had one there already — which is what "run to
    /// here" means everywhere else.
    /// </summary>
    private async Task RunToLineAsync(string filePath, int line)
    {
        var alreadyThere = _debug.BreakpointsIn(filePath).Any(b => b.Line == line);

        if (!alreadyThere) await _debug.ToggleBreakpointAsync(filePath, line);

        if (_debug.IsPaused) await _debug.ContinueAsync();
        else await StartOrContinueDebuggingAsync();

        if (alreadyThere) return;

        // Removed once execution has stopped, so the temporary breakpoint does
        // not linger in the list.
        void Remove(object? sender, StackFrame frame)
        {
            _debug.Paused -= Remove;
            _ = _debug.ToggleBreakpointAsync(filePath, line);
        }

        _debug.Paused += Remove;
    }

    /// <summary>Sets or clears a breakpoint on the line the caret is in.</summary>
    private async Task ToggleBreakpointAtCaretAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        await _debug.ToggleBreakpointAsync(document.FilePath, editor.TextArea.Caret.Line);
    }

    private async Task StopDebuggingAsync()
    {
        await _debug.StopAsync();
        OnDebuggerResumed();
    }

    /// <summary>Builds the test window and connects it to the runner.</summary>
    private void BuildTestPanel()
    {
        _tests = new TestExplorerPanel();

        _tests.RunRequested += (_, scope) => Guarded.Run(
            () => RunTestsAsync(scope),
            ViewModel.WriteOutput, "ide");

        _tests.DebugRequested += (_, _) => Guarded.Run(
            () => DebugSelectedTestAsync(),
            ViewModel.WriteOutput, "ide");

        _tests.FailureActivated += (_, result) => Guarded.Run(
            async () =>
            {
                if (result.FilePath is { Length: > 0 } path)
                    await OpenAtAsync(path, result.Line, 1);
            },
            ViewModel.WriteOutput, "ide");

        _testService.OutputReceived += (_, text) => ViewModel.WriteOutput(text);

        _contents[_factory.Tests] = _tests;

        // Discovering on open means the window has something in it before the
        // user asks, which is what makes it worth opening.
        ViewModel.PropertyChanged += (_, e) => Guarded.Run(
            async () =>
            {
                if (e.PropertyName != nameof(ViewModel.SolutionPath)) return;

                // The runnable projects are not known until a solution is open,
                // so the toolbar is built again with them.
                BuildToolbar();
                RestoreStartupProject();

                await DiscoverTestsAsync();
            },
            ViewModel.WriteOutput, "ide");
    }

    /// <summary>
    /// Finds the tests of the solution.
    ///
    /// Test projects are recognised by referencing the test SDK: a name ending
    /// in "Tests" is a convention, not a fact, and running discovery over
    /// every project would be slow and mostly fruitless.
    /// </summary>
    private async Task DiscoverTestsAsync()
    {
        if (_tests is null || ViewModel.SolutionPath is not { Length: > 0 } solution) return;

        var directory = Path.GetDirectoryName(Path.GetFullPath(solution));
        if (directory is null) return;

        var found = new List<TestCase>();

        foreach (var project in FindTestProjects(directory))
        {
            try
            {
                found.AddRange(await _testService.DiscoverAsync(project).ConfigureAwait(true));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                ViewModel.WriteOutput($"[test] {ex.Message}");
            }
        }

        _tests.Show(found);
    }

    /// <summary>The projects that reference a test runner.</summary>
    private static IEnumerable<string> FindTestProjects(string directory)
    {
        IEnumerable<string> projects;

        try
        {
            projects = Directory
                .EnumerateFiles(directory, "*.*proj", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                         || p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var project in projects)
        {
            string text;

            try
            {
                text = File.ReadAllText(project);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (text.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
                yield return project;
        }
    }

    /// <summary>Runs the tests the user asked for.</summary>
    private async Task RunTestsAsync(TestExplorerPanel.RunScope scope)
    {
        if (_tests is null) return;

        var chosen = scope switch
        {
            TestExplorerPanel.RunScope.Failed => _tests.FailedTests,
            TestExplorerPanel.RunScope.Selected => _tests.SelectedTests,
            _ => _tests.SelectedTests
        };

        if (chosen.Count == 0)
        {
            ViewModel.WriteOutput("[test] There is nothing to run.");
            return;
        }

        _tests.MarkRunning(chosen);

        var results = new List<TestResult>();

        // A run is per project, so tests spread across projects mean one run
        // each; the results are gathered before the window is updated.
        foreach (var byProject in chosen.GroupBy(t => t.ProjectPath, StringComparer.Ordinal))
        {
            var filter = scope == TestExplorerPanel.RunScope.All
                ? null
                : DotnetTestService.FilterFor(byProject.Select(t => t.FullyQualifiedName));

            try
            {
                results.AddRange(await _testService
                    .RunAsync(byProject.Key, filter)
                    .ConfigureAwait(true));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                ViewModel.WriteOutput($"[test] {ex.Message}");
            }
        }

        _tests.ShowResults(results);
    }

    /// <summary>
    /// Debugs the selected test.
    ///
    /// The runner is told to wait for a debugger, and the session attaches to
    /// it: a test cannot be launched directly, since the runner is what has
    /// the entry point.
    /// </summary>
    private async Task DebugSelectedTestAsync()
    {
        if (_tests is null) return;

        var chosen = _tests.SelectedTests;

        if (chosen.Count != 1)
        {
            ViewModel.WriteOutput("[test] Select a single test to debug.");
            return;
        }

        var test = chosen[0];

        ViewModel.WriteOutput($"[test] Debugging {test.FullyQualifiedName}.");

        // VSTEST_HOST_DEBUG makes the runner print its process id and wait,
        // which is the documented way to attach to a test.
        Environment.SetEnvironmentVariable("VSTEST_HOST_DEBUG", "1");

        try
        {
            await _testService
                .RunAsync(test.ProjectPath, DotnetTestService.FilterFor([test.FullyQualifiedName]))
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            ViewModel.WriteOutput($"[test] {ex.Message}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("VSTEST_HOST_DEBUG", null);
        }
    }

    /// <summary>Builds the assistant panel and connects it to a conversation.</summary>
    private void BuildAssistantPanel()
    {
        _assistant = new AiChatPanel();

        _assistant.Asked += (_, request) => Guarded.Run(
            () => AskAssistantAsync(request),
            ViewModel.WriteOutput, "ide");

        _assistant.ApplyRequested += (_, code) => Guarded.Run(
            () => ApplyAssistantCodeAsync(code),
            ViewModel.WriteOutput, "ide");

        _contents[_factory.Assistant] = _assistant;

        ShowAssistantStatus();
    }

    /// <summary>Says which assistant is in use, or that none is set up.</summary>
    private void ShowAssistantStatus()
    {
        if (_assistant is null) return;

        var settings = _settingsStore.Load().Ai;

        if (!Enum.TryParse<AiVendor>(settings.Vendor, out var vendor))
        {
            _assistant.ShowStatus("No assistant chosen. Pick one in Settings.");
            return;
        }

        var provider = AiConversation.ProviderFor(vendor);

        _assistant.ShowStatus(_apiKeys.Has(vendor)
            ? $"{provider.DisplayName} — {ModelNameOf(provider, settings.Model)}"
            : $"{provider.DisplayName} needs an API key. Add it in Settings.");
    }

    private static string ModelNameOf(IAiProvider provider, string modelId) =>
        provider.Models.FirstOrDefault(m => m.Id == modelId)?.DisplayName
        ?? provider.Models[0].DisplayName;

    /// <summary>
    /// Asks the assistant, showing the reply as it arrives.
    ///
    /// Nothing is awaited before the turns are added: the user should see
    /// their question in the conversation the moment they ask it.
    /// </summary>
    private async Task AskAssistantAsync(AiRequest request)
    {
        if (_assistant is null) return;

        var settings = _settingsStore.Load().Ai;

        if (!Enum.TryParse<AiVendor>(settings.Vendor, out var vendor))
        {
            _assistant.ShowStatus("No assistant chosen. Pick one in Settings.");
            return;
        }

        if (_apiKeys.Get(vendor) is not { Length: > 0 } apiKey)
        {
            _assistant.ShowStatus("That assistant needs an API key. Add it in Settings.");
            return;
        }

        var provider = AiConversation.ProviderFor(vendor);

        var model = settings.Model is { Length: > 0 } chosen ? chosen : provider.Models[0].Id;

        var question = AiConversation.InstructionFor(ActionName(request.Action), request.Question);

        _assistant.Append(Microsoft.Extensions.AI.ChatRole.User,
            request.Question.Length > 0 ? request.Question : ActionName(request.Action));

        _assistant.Append(Microsoft.Extensions.AI.ChatRole.Assistant, "");

        using var running = new CancellationTokenSource();
        _assistant.BeginReply(running);

        try
        {
            using var client = provider.CreateClient(apiKey, model);

            await foreach (var piece in _conversation
                .AskAsync(client, question, GatherContext(settings), running.Token)
                .ConfigureAwait(true))
            {
                _assistant.AppendToLast(piece);
            }
        }
        catch (OperationCanceledException)
        {
            _assistant.AppendToLast("\n\n(stopped)");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                      or IOException)
        {
            _assistant.AppendToLast($"\n\n{ex.Message}");
        }
        finally
        {
            _assistant.EndReply();
        }
    }

    private static string ActionName(AiAction action) => action switch
    {
        AiAction.Explain => "Explain",
        AiAction.FixError => "Fix the error",
        AiAction.GenerateTests => "Generate tests",
        AiAction.Document => "Document",
        _ => "Ask"
    };

    /// <summary>
    /// What the assistant is told about the code.
    ///
    /// The selection where there is one, the file otherwise, and the current
    /// errors — each only if the user has left that setting on.
    /// </summary>
    private string GatherContext(AiSettings settings)
    {
        if (CurrentEditor() is not { } editor) return "";
        if (ViewModel.ActiveDocument is not { } document) return new AiContext().Describe();

        var context = new AiContext
        {
            FilePath = document.FilePath,
            Language = document.Language.ToString(),
            SelectedText = editor.SelectionLength > 0 ? editor.SelectedText : null,
            FileText = settings.SendFileContext && editor.SelectionLength == 0
                ? editor.Text
                : null,
            Errors = settings.SendErrors
                ? [.. ViewModel.Diagnostics
                    .Where(d => d.Severity == Basalt.Core.Model.DiagnosticSeverity.Error)
                    .Select(d => $"{d.Id}: {d.Message}")]
                : []
        };

        return context.Describe();
    }

    /// <summary>
    /// Puts the assistant's code into the editor.
    ///
    /// It replaces the selection where there is one and is inserted at the
    /// caret otherwise; either way it is one edit, which undo takes back in
    /// one step.
    /// </summary>
    private async Task ApplyAssistantCodeAsync(string code)
    {
        if (CurrentEditor() is not { } editor)
        {
            ViewModel.WriteOutput("[assistant] Open a file to apply the change to.");
            return;
        }

        await Task.Yield();

        if (editor.SelectionLength > 0)
        {
            editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, code);
            return;
        }

        editor.Document.Insert(editor.CaretOffset, code);
    }

    /// <summary>Builds the source control panels and connects them to git.</summary>
    private void BuildGitPanels()
    {
        _gitChanges = new GitChangesPanel();

        _gitChanges.StageRequested += (_, change) => Guarded.Run(
            () => RunGitAsync(git => git.StageAsync([change.Path])),
            ViewModel.WriteOutput, "ide");

        _gitChanges.UnstageRequested += (_, change) => Guarded.Run(
            () => RunGitAsync(git => git.UnstageAsync([change.Path])),
            ViewModel.WriteOutput, "ide");

        _gitChanges.CommitRequested += (_, request) => Guarded.Run(
            async () =>
            {
                if (await RunGitAsync(git => git.CommitAsync(request.Message, request.Amend)))
                {
                    _gitChanges.ClearMessage();
                    await RefreshGitAsync();
                }
            },
            ViewModel.WriteOutput, "ide");

        // Amending usually means correcting the previous message, so it is
        // loaded rather than left for the user to retype.
        _gitChanges.AmendRequested += (_, _) => Guarded.Run(
            async () =>
            {
                if (ViewModel.Repository is not { } git) return;

                try
                {
                    if (await git.GetLastCommitMessageAsync().ConfigureAwait(true) is { } message)
                        _gitChanges.CommitMessage = message;
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    ViewModel.WriteOutput($"[git] {ex.Message}");
                }
            },
            ViewModel.WriteOutput, "ide");

        _contents[_factory.GitChanges] = _gitChanges;

        _gitChanges.DiffRequested += (_, change) => Guarded.Run(
            () => ShowDiffAsync(change),
            ViewModel.WriteOutput, "ide");

        _gitHistory = new GitHistoryPanel();

        // Picking a commit used to do nothing at all: the event was raised
        // and nobody was listening.
        _gitHistory.CommitSelected += (_, commit) => Guarded.Run(
            () => ShowCommitDiffAsync(commit), ViewModel.WriteOutput, "git");

        _contents[_factory.GitHistory] = _gitHistory;

        _diff = new DiffView();
        _contents[_factory.GitDiff] = _diff;

        _branches = new BranchPanel();

        _branches.CreateRequested += (_, name) => Guarded.Run(
            () => RunGitAsync(git => git.CreateBranchAsync(name)),
            ViewModel.WriteOutput, "ide");

        _branches.CheckoutRequested += (_, branch) => Guarded.Run(
            () => RunGitAsync(git => git.CheckoutAsync(branch.Name)),
            ViewModel.WriteOutput, "ide");

        _branches.MergeRequested += (_, branch) => Guarded.Run(
            () => RunGitAsync(git => git.MergeAsync(branch.Name)),
            ViewModel.WriteOutput, "ide");

        _branches.DeleteRequested += (_, branch) => Guarded.Run(
            () => RunGitAsync(git => git.DeleteBranchAsync(branch.Name)),
            ViewModel.WriteOutput, "ide");

        _branches.FetchRequested += (_, _) => Guarded.Run(
            () => RunRemoteGitAsync(git => git.FetchAsync()),
            ViewModel.WriteOutput, "ide");
        _branches.PullRequested += (_, _) => Guarded.Run(
            () => RunRemoteGitAsync(git => git.PullAsync()),
            ViewModel.WriteOutput, "ide");
        _branches.PushRequested += (_, _) => Guarded.Run(
            () => RunRemoteGitAsync(git => git.PushAsync()),
            ViewModel.WriteOutput, "ide");

        _contents[_factory.GitBranches] = _branches;

        ViewModel.RepositoryChanged += (_, _) => Guarded.Run(
            () => RefreshGitAsync(),
            ViewModel.WriteOutput, "ide");
    }

    /// <summary>
    /// Runs a git operation, then refreshes what the panels show.
    ///
    /// Failures are reported to the output panel rather than thrown: a refused
    /// commit or a conflicted merge is something the user needs to read, not a
    /// crash.
    /// </summary>
    private async Task<bool> RunGitAsync(Func<ISourceControlService, Task> operation)
    {
        if (ViewModel.Repository is not { } git) return false;

        try
        {
            await operation(git).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ViewModel.WriteOutput($"[git] {ex.Message}");
            return false;
        }

        await ViewModel.RefreshRepositoryAsync();
        return true;
    }

    /// <summary>
    /// Runs an operation that talks to a remote, reporting what it said.
    ///
    /// These print progress even when they succeed, and the user wants to read
    /// it: "Everything up-to-date" is an answer, not silence.
    /// </summary>
    private async Task RunRemoteGitAsync(Func<ISourceControlService, Task<string>> operation)
    {
        if (ViewModel.Repository is not { } git) return;

        try
        {
            ViewModel.WriteOutput($"[git] {await operation(git).ConfigureAwait(true)}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ViewModel.WriteOutput($"[git] {ex.Message}");
            return;
        }

        await ViewModel.RefreshRepositoryAsync();
    }

    /// <summary>
    /// Shows what a commit changed, and brings the diff to the front.
    /// </summary>
    private async Task ShowCommitDiffAsync(CommitInfo commit)
    {
        if (ViewModel.Repository is not Basalt.Workspace.GitSourceControlService git
            || _diff is null) return;

        var text = await git.GetCommitDiffAsync(commit.Sha).ConfigureAwait(true);

        _diff.Show(text.Length > 0
            ? text
            : $"{commit.Sha[..Math.Min(8, commit.Sha.Length)]} changed nothing.");

        if (_factory.BottomArea is not null)
            _factory.ShowTool(_factory.GitDiff, _factory.BottomArea);
    }

    /// <summary>Shows what changed in a file, and brings the diff to the front.</summary>
    private async Task ShowDiffAsync(FileChange change)
    {
        if (ViewModel.Repository is not { } git || _diff is null) return;

        try
        {
            _diff.Show(await git
                .GetDiffAsync(change.Path, change.Staged)
                .ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ViewModel.WriteOutput($"[git] {ex.Message}");
            return;
        }

        if (_factory.BottomArea is not null)
            _factory.ShowTool(_factory.GitDiff, _factory.BottomArea);
    }

    /// <summary>Re-reads the working tree and the history into the panels.</summary>
    private async Task RefreshGitAsync()
    {
        if (ViewModel.Repository is not { } git) return;

        try
        {
            if (_gitChanges is not null)
            {
                var status = await git.GetStatusAsync().ConfigureAwait(true);
                _gitChanges.Show(ViewModel.CurrentBranch, status);
            }

            if (_gitHistory is not null)
            {
                // Enough commits to show how the branches relate without
                // reading a history of unbounded length at every refresh.
                var log = await git.GetLogAsync(200).ConfigureAwait(true);
                _gitHistory.Show(log);
            }

            _branches?.Show(await git.GetBranchesAsync().ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ViewModel.WriteOutput($"[git] {ex.Message}");
        }
    }

    /// <summary>Lists everywhere the symbol under the caret is used.</summary>
    private async Task FindReferencesAsync()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;
        if (_references is null || _factory.BottomArea is null) return;

        var locations = await ViewModel
            .FindReferencesAsync(document.FilePath, editor.Text, editor.CaretOffset)
            .ConfigureAwait(true);

        var word = WordAt(editor.Text, editor.CaretOffset);

        await _references.ShowAsync(word, locations);
        _factory.ShowTool(_factory.References, _factory.BottomArea);
    }

    /// <summary>The identifier the caret sits in, used to label results.</summary>
    private static string WordAt(string text, int caret)
    {
        var position = Math.Clamp(caret, 0, text.Length);

        var start = position;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
            start--;

        var end = position;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            end++;

        return end > start ? text[start..end] : "";
    }

    /// <summary>Offers the symbols of the open document to jump to.</summary>
    private async Task GoToSymbolInFileAsync()
    {
        if (ViewModel.ActiveDocument is not { OpenInDesigner: false } document) return;

        var symbols = await ViewModel
            .GetDocumentSymbolsAsync(document.FilePath, document.Text)
            .ConfigureAwait(true);

        var choices = Flatten(symbols, document.FilePath).ToList();

        var dialog = new GoToSymbolDialog(choices, Localizer.Get(StringKeys.MenuNavigateGoToFileSymbol));
        var chosen = await dialog.ShowDialog<SymbolChoice?>(this);

        if (chosen is not null)
        {
            RecordCurrentPosition();
            GoToLine(chosen.Line);
        }
    }

    private static IEnumerable<SymbolChoice> Flatten(
        IReadOnlyList<Basalt.Extensibility.DocumentSymbol> symbols, string filePath)
    {
        foreach (var symbol in symbols)
        {
            yield return new SymbolChoice(
                symbol.Name, symbol.Kind, filePath, symbol.Range.Start.Line);

            foreach (var child in Flatten(symbol.Children, filePath)) yield return child;
        }
    }

    /// <summary>Offers symbols from anywhere in the solution.</summary>
    private async Task GoToSymbolInSolutionAsync()
    {
        var dialog = new GoToSymbolDialog(
            async query =>
            {
                var found = await ViewModel.SearchSymbolsAsync(query).ConfigureAwait(true);

                return found
                    .Select(l => new SymbolChoice(
                        Path.GetFileNameWithoutExtension(l.FilePath),
                        Basalt.Extensibility.SymbolKind.Unknown,
                        l.FilePath,
                        l.Range.Start.Line))
                    .ToList();
            },
            Localizer.Get(StringKeys.MenuNavigateGoToSolutionSymbol));

        var chosen = await dialog.ShowDialog<SymbolChoice?>(this);

        if (chosen is not null)
        {
            RecordCurrentPosition();
            await OpenAtAsync(chosen.FilePath, chosen.Line, 1);
        }
    }

    private async Task OpenLocationAsync(Basalt.Extensibility.SourceLocation location)
    {
        RecordCurrentPosition();
        await OpenAtAsync(location.FilePath, location.Range.Start.Line, location.Range.Start.Column);
    }

    /// <summary>Opens a file and puts the caret on a line.</summary>
    private async Task OpenAtAsync(string filePath, int line, int column)
    {
        if (!File.Exists(filePath)) return;

        await ViewModel.OpenFileAsync(filePath);

        // The editor is created by the document change, so moving the caret
        // waits until that has happened.
        Dispatcher.UIThread.Post(
            () => GoToLine(line, column), DispatcherPriority.Background);
    }

    private void GoToLine(int line, int column = 1)
    {
        if (CurrentEditor() is not { } editor) return;
        if (line < 1 || line > editor.Document.LineCount) return;

        var documentLine = editor.Document.GetLineByNumber(line);
        editor.CaretOffset = Math.Clamp(
            documentLine.Offset + column - 1, documentLine.Offset, documentLine.EndOffset);

        editor.ScrollToLine(line);
        editor.TextArea.Focus();
    }

    /// <summary>
    /// The text editor of the document being worked in.
    ///
    /// The active one, not simply the first: this used to take whatever came
    /// first out of the content dictionary, so with more than one file open
    /// every refactoring read the caret from the wrong document — and did
    /// nothing, or something surprising, without saying why.
    /// </summary>
    private AvaloniaEdit.TextEditor? CurrentEditor()
    {
        // A context-menu command acts on the editor it was opened from, which
        // is not always the active document: a right click does not activate.
        if (_actingEditor is { } acting && InnerEditor(acting) is { } inner)
            return inner;

        if (ViewModel.ActiveDocument is { } active
            && EditorFor(active) is { } editorForActive)
            return editorForActive;

        // No active document, or it has no editor yet: the only editor open,
        // when there is exactly one, is unambiguous.
        var editors = _contents.Values.OfType<CodeEditor>().ToList();

        return editors.Count == 1 ? InnerEditor(editors[0]) : null;
    }

    /// <summary>
    /// Comments the selected lines out, or brings them back.
    ///
    /// Whole lines rather than a selection inside one: commenting half a line
    /// leaves the other half as code, which never reads as what was meant.
    /// </summary>
    private void ToggleComment()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var prefix = document.Language == SourceLanguage.VisualBasic ? "'" : "//";

        var first = editor.Document.GetLineByOffset(editor.SelectionStart);

        var last = editor.SelectionLength > 0
            ? editor.Document.GetLineByOffset(editor.SelectionStart + editor.SelectionLength)
            : first;

        var lines = new List<AvaloniaEdit.Document.DocumentLine>();

        for (var line = first; line is not null && line.LineNumber <= last.LineNumber;
             line = line.NextLine)
            lines.Add(line);

        // Uncommenting only when every line carries the prefix: a mixed
        // selection is being commented, not uncommented.
        var allCommented = lines
            .Where(l => editor.Document.GetText(l).Trim().Length > 0)
            .All(l => editor.Document.GetText(l).TrimStart()
                     .StartsWith(prefix, StringComparison.Ordinal));

        editor.Document.BeginUpdate();

        try
        {
            // Backwards, so an edit does not move the lines still to come.
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var line = lines[i];
                var text = editor.Document.GetText(line);

                if (text.Trim().Length == 0) continue;

                var indent = text.Length - text.TrimStart().Length;

                if (allCommented)
                {
                    var at = line.Offset + indent;

                    editor.Document.Remove(at, prefix.Length);
                }
                else
                {
                    editor.Document.Insert(line.Offset + indent, prefix);
                }
            }
        }
        finally
        {
            editor.Document.EndUpdate();
        }
    }

    /// <summary>
    /// Describes the symbol at the caret, without reaching for the pointer.
    /// </summary>
    private async Task ShowQuickInfoAtCaretAsync()
    {
        if (CurrentCodeEditor() is not { } editor) return;

        if (!await editor.ShowHoverAtCaretAsync())
            ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusNothingToDescribe);
    }

    /// <summary>Tidies just the selected lines.</summary>
    private async Task FormatSelectionAsync()
    {
        if (CurrentCodeEditor() is not { } editor) return;

        await editor.FormatSelectionAsync();
    }

    /// <summary>Asks for a line number and goes there.</summary>
    private async Task AskAndGoToLineAsync()
    {
        if (CurrentEditor() is not { } editor) return;

        var line = await AskForNumberAsync(
            "Go to Line", "Line number:", editor.TextArea.Caret.Line);

        if (line is { } wanted)
            GoToLine(Math.Clamp(wanted, 1, editor.Document.LineCount));
    }

    /// <summary>
    /// The document a command should act on.
    ///
    /// The one behind the editor the menu was opened from, when there is one:
    /// taking the caret from that editor and the path from the active
    /// document would rename in the wrong file, which is worse than doing
    /// nothing.
    /// </summary>
    private EditorDocumentViewModel? ActingDocument =>
        _actingEditor?.Document ?? ViewModel.ActiveDocument;

    /// <summary>
    /// The editor a context-menu command is acting on, while it acts.
    ///
    /// Null the rest of the time, when the active document decides.
    /// </summary>
    private CodeEditor? _actingEditor;

    /// <summary>
    /// The code editor of the active document, folding and all.
    ///
    /// Unlike CurrentEditor, which gives the inner text control, this is the
    /// wrapper that owns the fold marks.
    /// </summary>
    private CodeEditor? CurrentCodeEditor()
    {
        if (_actingEditor is { } acting) return acting;

        if (ViewModel.ActiveDocument is not { } active) return null;

        foreach (var (dockable, content) in _contents)
        {
            if (dockable is not IdeDocument ide || !ReferenceEquals(ide.Document, active))
                continue;

            return content switch
            {
                CodeEditor editor => editor,
                DesignerHost host => host.TextView as CodeEditor,
                DockPanel panel => panel.Children.OfType<CodeEditor>().FirstOrDefault(),
                _ => null
            };
        }

        return null;
    }

    /// <summary>The editor showing a document, if it is open in one.</summary>
    private AvaloniaEdit.TextEditor? EditorFor(ViewModels.EditorDocumentViewModel document)
    {
        foreach (var (dockable, content) in _contents)
        {
            if (dockable is not IdeDocument ide || !ReferenceEquals(ide.Document, document))
                continue;

            if (content is CodeEditor editor) return InnerEditor(editor);
        }

        return null;
    }

    private static AvaloniaEdit.TextEditor? InnerEditor(CodeEditor editor) =>
        editor.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().FirstOrDefault();

    private void RecordCurrentPosition()
    {
        if (CurrentEditor() is not { } editor) return;
        if (ActingDocument is not { } document) return;

        var line = editor.Document.GetLineByOffset(
            Math.Clamp(editor.CaretOffset, 0, editor.Document.TextLength)).LineNumber;

        ViewModel.History.Record(new NavigationPoint(document.FilePath, editor.CaretOffset, line));
    }

    private async Task NavigateAsync(NavigationPoint? point)
    {
        if (point is not { } target) return;

        await ViewModel.OpenFileAsync(target.FilePath);
        Dispatcher.UIThread.Post(() => GoToLine(target.Line), DispatcherPriority.Background);
    }

    /// <summary>Brings up the search panel, ready for a term.</summary>
    private void ShowSearch()
    {
        if (_search is null || _factory.BottomArea is null) return;

        _search.SearchRoot = ViewModel.SolutionPath is { } path
            ? Path.GetDirectoryName(Path.GetFullPath(path))
            : null;

        _factory.ShowTool(_factory.Search, _factory.BottomArea);

        Dispatcher.UIThread.Post(
            () => _search.FocusSearchBox(), DispatcherPriority.Background);
    }

    private async void OpenDiagnostic(IdeDiagnostic diagnostic)
    {
        if (diagnostic.FilePath is null || !File.Exists(diagnostic.FilePath)) return;
        await ViewModel.OpenFileAsync(diagnostic.FilePath);
    }

    /// <summary>
    /// Builds the menu and installs it where the platform expects it:
    /// in the system menu bar on macOS, inside the window elsewhere.
    /// </summary>
    private void InstallMenu(MainWindowViewModel viewModel)
    {
        var entries = BuildMenuEntries(viewModel);

        if (IdeMenu.UsesSystemMenuBar)
        {
            NativeMenu.SetMenu(this, IdeMenu.BuildNative(entries));
            WindowMenu.IsVisible = false;
        }
        else
        {
            WindowMenu.ItemsSource = IdeMenu.BuildManaged(entries);
        }
    }

    /// <summary>The real menu, for tests.</summary>
    /// <summary>
    /// Fills the list of recent solutions on the welcome screen.
    ///
    /// On the screen and not only in a menu: opening yesterday's work is the
    /// commonest thing to do on starting, and it should not need a menu.
    /// </summary>
    private void ShowRecentOnWelcome()
    {
        var recent = _settingsStore.Load().Recent.ExistingSolutions();

        WelcomeRecent.Children.Clear();
        WelcomeRecent.IsVisible = recent.Count > 0;

        if (recent.Count == 0) return;

        WelcomeRecent.Children.Add(new TextBlock
        {
            Text = Localizer.Get(StringKeys.WelcomeRecent),
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // Five: enough to reach yesterday without turning the welcome screen
        // into a list.
        foreach (var path in recent.Take(5))
        {
            var target = path;

            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new IconView { Kind = IconKind.Solution, IconSize = 13 },
                        new TextBlock { Text = Path.GetFileNameWithoutExtension(target) }
                    }
                },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2),
                [ToolTip.TipProperty] = target
            };

            button.Click += (_, _) => Guarded.Run(
                () => ViewModel.OpenSolutionAsync(target), ViewModel.WriteOutput, "ide");

            WelcomeRecent.Children.Add(button);
        }
    }

    /// <summary>
    /// Puts a solution or a file at the top of the list it belongs to.
    /// </summary>
    private void Remember(string path, bool isSolution)
    {
        var settings = _settingsStore.Load();

        var before = Listed(settings.Recent, isSolution);

        if (isSolution) settings.Recent.RememberSolution(path);
        else settings.Recent.RememberFile(path);

        _settingsStore.Save(settings);

        // Only when the list really changed. Rebuilding on every file opened
        // meant replacing the whole macOS menu bar each time, which leaves it
        // stale: reopening the same file changes nothing and was rebuilding
        // it anyway.
        if (string.Equals(before, Listed(settings.Recent, isSolution), StringComparison.Ordinal))
            return;

        InstallMenu(ViewModel);
        ShowRecentOnWelcome();
    }

    /// <summary>The list as one string, to tell whether it moved.</summary>
    private static string Listed(Basalt.Core.Settings.RecentSettings recent, bool solutions) =>
        string.Join("\n", solutions ? recent.Solutions : recent.Files);

    /// <summary>
    /// The entries under Recent Solutions or Recent Files.
    ///
    /// Only what is still on disk: an entry that has been moved or deleted
    /// would open onto an error, and offering it is worse than forgetting it.
    /// </summary>
    private IReadOnlyList<MenuEntry> RecentEntries(bool isSolutions)
    {
        var recent = _settingsStore.Load().Recent;

        var paths = isSolutions ? recent.ExistingSolutions() : recent.ExistingFiles();

        if (paths.Count == 0)
            return [new(Localizer.Get(StringKeys.MenuFileRecentNone)) { IsEnabled = false }];

        var entries = new List<MenuEntry>();

        foreach (var path in paths)
        {
            var target = path;

            entries.Add(new(
                Path.GetFileName(target),
                Action: () => Guarded.Run(
                    () => isSolutions
                        ? ViewModel.OpenSolutionAsync(target)
                        : ViewModel.OpenFileAsync(target),
                    ViewModel.WriteOutput, "ide"))
            {
                // The folder as well: two projects called Index.vbhtml are
                // told apart by where they are, not by their name.
                ToolTip = target
            });
        }

        entries.Add(MenuEntry.Separator);
        entries.Add(new(Localizer.Get(StringKeys.MenuFileRecentClear), Action: () =>
        {
            var settings = _settingsStore.Load();

            if (isSolutions) settings.Recent.Solutions.Clear();
            else settings.Recent.Files.Clear();

            _settingsStore.Save(settings);

            InstallMenu(ViewModel);
        }));

        return entries;
    }

    internal IReadOnlyList<MenuEntry> MenuEntriesForTests() => BuildMenuEntries(ViewModel);

    /// <summary>The editor a refactoring would act on.</summary>
    internal AvaloniaEdit.TextEditor? CurrentEditorForTests() => CurrentEditor();

    /// <summary>The code editor showing a file, for the tests.</summary>
    internal CodeEditor? CodeEditorForTests(string filePath)
    {
        // Built on demand: a document that has never been shown has no
        // content yet, so it is asked for the way Dock asks for it.
        foreach (var (document, dockable) in _documents)
        {
            if (!string.Equals(document.FilePath, filePath, StringComparison.Ordinal))
                continue;

            return ContentFor(dockable) switch
            {
                DesignerHost host => host.TextView as CodeEditor,
                CodeEditor editor => editor,
                _ => null
            };
        }

        return _contents.Values.OfType<CodeEditor>()
            .FirstOrDefault(e => string.Equals(e.FilePath, filePath, StringComparison.Ordinal));
    }

    /// <summary>
    /// Which document a command opened from an editor would act on.
    /// </summary>
    internal EditorDocumentViewModel? DocumentACommandWouldActOn(CodeEditor? from)
    {
        _actingEditor = from;

        try
        {
            return ActingDocument;
        }
        finally
        {
            _actingEditor = null;
        }
    }

    private IReadOnlyList<MenuEntry> BuildMenuEntries(MainWindowViewModel vm) =>
    [
        new(Localizer.Get(StringKeys.MenuFile), Children:
        [
            new(Localizer.Get(StringKeys.MenuFileNewSolution),
                Action: () => _ = ShowNewSolutionDialogAsync(),
                Gesture: new KeyGesture(Key.N, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.New },
            new(Localizer.Get(StringKeys.MenuFileOpenSolution),
                Action: () => _ = ShowOpenSolutionDialogAsync(),
                Gesture: new KeyGesture(Key.O, PlatformCommandModifier)) { Icon = IconKind.Open },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuFileRecentSolutions),
                Children: RecentEntries(isSolutions: true)) { Icon = IconKind.Solution },
            new(Localizer.Get(StringKeys.MenuFileRecentFiles),
                Children: RecentEntries(isSolutions: false)) { Icon = IconKind.History },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuFileSave), vm.SaveCommand,
                Gesture: new KeyGesture(Key.S, PlatformCommandModifier)) { Icon = IconKind.Save },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuFileExit), Action: Close) { Icon = IconKind.Exit }
        ]),
        new(Localizer.Get(StringKeys.MenuEdit), Children:
        [
            new(Localizer.Get(StringKeys.MenuEditUndo), vm.UndoCommand,
                Gesture: new KeyGesture(Key.Z, PlatformCommandModifier)) { Icon = IconKind.Undo },
            new(Localizer.Get(StringKeys.MenuEditRedo), vm.RedoCommand,
                Gesture: new KeyGesture(Key.Z, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.Redo },
            MenuEntry.Separator,
            // Same shortcut Visual Studio and VS Code use for formatting.
            // No icon: the only one that fit was the hammer that means Build,
            // and formatting is not building.
            new(Localizer.Get(StringKeys.MenuEditFormatDocument), vm.FormatDocumentCommand,
                Gesture: new KeyGesture(Key.D, PlatformCommandModifier | KeyModifiers.Alt)),
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuEditFind), Action: ShowSearch,
                Gesture: new KeyGesture(Key.F, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.Find },
            new(Localizer.Get(StringKeys.MenuEditReplace), Action: ShowSearch,
                Gesture: new KeyGesture(Key.H, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.Replace }
        ]),
        new(Localizer.Get(StringKeys.MenuView), Children:
        [
            new(Localizer.Get(StringKeys.ToolSolutionExplorer),
                Action: () => ShowTool(_factory.SolutionExplorer)) { Icon = IconKind.Explorer },
            new(Localizer.Get(StringKeys.ToolToolbox),
                Action: () => ShowTool(_factory.Toolbox)) { Icon = IconKind.Toolbox },
            new(Localizer.Get(StringKeys.ToolProperties),
                Action: () => ShowTool(_factory.Properties)) { Icon = IconKind.Properties },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.ToolProblems),
                Action: () => ShowTool(_factory.Problems)) { Icon = IconKind.Problems },
            new(Localizer.Get(StringKeys.ToolOutput),
                Action: () => ShowTool(_factory.Output)) { Icon = IconKind.Output },
            new(Localizer.Get(StringKeys.ToolSearch), Action: ShowSearch) { Icon = IconKind.Find },
            new(Localizer.Get(StringKeys.ToolOutline),
                Action: () => ShowTool(_factory.Outline)) { Icon = IconKind.Outline },
            new(Localizer.Get(StringKeys.ToolReferences),
                Action: () => ShowTool(_factory.References)) { Icon = IconKind.References },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuViewNewTerminal), Action: OpenNewTerminal,
                Gesture: new KeyGesture(Key.OemBackslash, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.Terminal },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuViewResetLayout), Action: ResetLayout) { Icon = IconKind.Layout }
        ]),
        new(Localizer.Get(StringKeys.MenuNavigate), Children:
        [
            new(Localizer.Get(StringKeys.MenuNavigateGoToDefinition),
                Action: () => _ = GoToDefinitionAsync(),
                Gesture: new KeyGesture(Key.F12)) { Icon = IconKind.GoToDefinition },
            new(Localizer.Get(StringKeys.MenuNavigateFindReferences),
                Action: () => _ = FindReferencesAsync(),
                Gesture: new KeyGesture(Key.F12, KeyModifiers.Shift)) { Icon = IconKind.FindReferences },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuNavigateGoToFileSymbol),
                Action: () => _ = GoToSymbolInFileAsync(),
                Gesture: new KeyGesture(Key.O, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.GoToSymbol },
            new(Localizer.Get(StringKeys.MenuNavigateGoToSolutionSymbol),
                Action: () => _ = GoToSymbolInSolutionAsync(),
                Gesture: new KeyGesture(Key.T, PlatformCommandModifier)) { Icon = IconKind.GoToSymbol },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuNavigateBack),
                Action: () => _ = NavigateAsync(ViewModel.History.GoBack()),
                Gesture: new KeyGesture(Key.OemMinus, PlatformCommandModifier)) { Icon = IconKind.Back },
            new(Localizer.Get(StringKeys.MenuNavigateForward),
                Action: () => _ = NavigateAsync(ViewModel.History.GoForward()),
                Gesture: new KeyGesture(Key.OemMinus, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.Forward }
        ]),
        new(Localizer.Get(StringKeys.MenuProject), Children:
        [
            new(Localizer.Get(StringKeys.MenuProjectAddWindow),
                Action: () => _ = ShowAddWindowDialogAsync()) { Icon = IconKind.Window },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuProjectProperties),
                Action: () => _ = ShowProjectPropertiesAsync(),
                Gesture: new KeyGesture(Key.Enter, PlatformCommandModifier | KeyModifiers.Alt)) { Icon = IconKind.Properties }
        ]),
        new(Localizer.Get(StringKeys.MenuBuild), Children:
        [
            new(Localizer.Get(StringKeys.MenuBuildBuildSolution), vm.BuildCommand,
                Gesture: new KeyGesture(Key.F6)) { Icon = IconKind.Build }
        ]),
        new(Localizer.Get(StringKeys.MenuTest), Children:
        [
            new(Localizer.Get(StringKeys.MenuTestRunAll),
                Action: () => _ = RunTestsAsync(TestExplorerPanel.RunScope.All),
                Gesture: new KeyGesture(Key.T, PlatformCommandModifier | KeyModifiers.Alt))
                { Icon = IconKind.Run },
            new(Localizer.Get(StringKeys.ToolTests),
                Action: () => ShowTool(_factory.Tests)) { Icon = IconKind.Tests }
        ]),
        new(Localizer.Get(StringKeys.MenuGit), Children:
        [
            new(Localizer.Get(StringKeys.MenuGitInitialize),
                Action: () => Guarded.Run(
                    StartRepositoryAsync, ViewModel.WriteOutput, "git")),
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuGitCommit),
                Action: () => ShowTool(_factory.GitChanges)) { Icon = IconKind.Commit },
            new(Localizer.Get(StringKeys.MenuGitBranches),
                Action: () => ShowTool(_factory.GitBranches)) { Icon = IconKind.Branch },
            new(Localizer.Get(StringKeys.MenuGitHistory),
                Action: () => ShowTool(_factory.GitHistory)) { Icon = IconKind.History },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuGitFetch),
                Action: () => _ = RunRemoteGitAsync(git => git.FetchAsync()))
                { Icon = IconKind.Refresh },
            new(Localizer.Get(StringKeys.MenuGitPull),
                Action: () => _ = RunRemoteGitAsync(git => git.PullAsync())) { Icon = IconKind.Pull },
            new(Localizer.Get(StringKeys.MenuGitPush),
                Action: () => _ = RunRemoteGitAsync(git => git.PushAsync())) { Icon = IconKind.Push }
        ]),
        new(Localizer.Get(StringKeys.MenuTools), Children:
        [
            new(Localizer.Get(StringKeys.MenuToolsAssistant),
                Action: () => ShowTool(_factory.Assistant),
                Gesture: new KeyGesture(Key.I, PlatformCommandModifier | KeyModifiers.Shift)) { Icon = IconKind.Assistant },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuToolsSettings),
                Action: () => _ = ShowSettingsAsync(),
                Gesture: new KeyGesture(Key.OemComma, PlatformCommandModifier)) { Icon = IconKind.Settings }
        ]),
        new(Localizer.Get(StringKeys.MenuDebug), Children:
        [
            new(Localizer.Get(StringKeys.MenuDebugStart),
                Action: () => _ = StartOrContinueDebuggingAsync(),
                Gesture: new KeyGesture(Key.F5)) { Icon = IconKind.Debug },
            new(Localizer.Get(StringKeys.MenuDebugStop),
                Action: () => _ = StopDebuggingAsync(),
                Gesture: new KeyGesture(Key.F5, KeyModifiers.Shift)) { Icon = IconKind.Stop },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuDebugStepOver),
                Action: () => _ = _debug.StepOverAsync(),
                Gesture: new KeyGesture(Key.F10)) { Icon = IconKind.StepOver },
            new(Localizer.Get(StringKeys.MenuDebugStepInto),
                Action: () => _ = _debug.StepIntoAsync(),
                Gesture: new KeyGesture(Key.F11)) { Icon = IconKind.StepInto },
            new(Localizer.Get(StringKeys.MenuDebugStepOut),
                Action: () => _ = _debug.StepOutAsync(),
                Gesture: new KeyGesture(Key.F11, KeyModifiers.Shift)) { Icon = IconKind.StepOut },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuDebugToggleBreakpoint),
                Action: () => _ = ToggleBreakpointAtCaretAsync(),
                Gesture: new KeyGesture(Key.F9)) { Icon = IconKind.Breakpoint }
        ]),
        new(Localizer.Get(StringKeys.MenuRun), Children:
        [
            new(Localizer.Get(StringKeys.MenuRunWithoutDebugging), vm.RunCommand,
                Gesture: new KeyGesture(Key.F5, PlatformCommandModifier))
                { Icon = IconKind.Run },
            new(Localizer.Get(StringKeys.MenuRunStop), vm.StopRunCommand)
                { Icon = IconKind.Stop }
        ]),
        new(Localizer.Get(StringKeys.MenuWindow), Children:
        [
            new(Localizer.Get(StringKeys.MenuWindowMinimize),
                Action: () => WindowState = WindowState.Minimized,
                Gesture: new KeyGesture(Key.M, PlatformCommandModifier)) { Icon = IconKind.Minimize },
            new(Localizer.Get(StringKeys.MenuWindowZoom),
                Action: () => WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized) { Icon = IconKind.Zoom },
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuViewNewTerminal), Action: OpenNewTerminal)
                { Icon = IconKind.Terminal },
            new(Localizer.Get(StringKeys.MenuViewResetLayout), Action: ResetLayout) { Icon = IconKind.Layout }
        ]),
        new(Localizer.Get(StringKeys.MenuHelp), Children:
        [
            new(Localizer.Get(StringKeys.MenuHelpAbout), Action: ShowAbout)
                { Icon = IconKind.Information }
        ])
    ];

    /// <summary>
    /// On macOS the canonical shortcut uses Cmd, on other platforms Ctrl.
    /// </summary>
    private static KeyModifiers PlatformCommandModifier =>
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private void ShowTool(IDockable pannello)
    {
        var fallback = pannello == _factory.Problems || pannello == _factory.Output
            ? _factory.BottomArea
            : null;

        if (fallback is not null) _factory.ShowTool(pannello, fallback);
        else _factory.SetActiveDockable(pannello);
    }

    private void OpenNewTerminal()
    {
        _factory.TerminalWorkingDirectory = ViewModel.SolutionPath is { } path
            ? Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory()
            : Directory.GetCurrentDirectory();

        var terminal = _factory.OpenNewTerminal();

        // Focus goes to the command line, as in a freshly opened terminal.
        Dispatcher.UIThread.Post(() =>
        {
            if (ContentFor(terminal) is TerminalView view) view.FocusInput();
        }, DispatcherPriority.Background);
    }

    /// <summary>Rebuilds the default panel arrangement.</summary>
    private void ResetLayout()
    {
        // The contents of the fixed panels are reused; terminals and documents
        // are recreated along with the new layout.
        foreach (var (dockable, content) in _contents.ToList())
        {
            if (dockable is TerminalTool or IdeDocument)
            {
                (content as IDisposable)?.Dispose();
                _contents.Remove(dockable);
            }
        }
        _documents.Clear();

        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);
        Docking.Layout = layout;

        // Documents that are already open go back to the central area.
        foreach (var document in ViewModel.OpenDocuments)
        {
            ViewModel.ActiveDocument = document;
            ShowActiveDocument();
        }
    }

    /// <summary>
    /// Shuts down everything the window owns.
    ///
    /// Terminals hold a live child process each: without this, closing the IDE
    /// would leave orphaned shells running.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        foreach (var content in _contents.Values) (content as IDisposable)?.Dispose();
        _contents.Clear();

        (DataContext as IDisposable)?.Dispose();

        base.OnClosed(e);
    }

    /// <summary>Opens the properties of the project being built and run.</summary>
    private async Task ShowProjectPropertiesAsync()
    {
        var project = SelectedProjectPath();

        if (project is null)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusOpenProjectFirst);
            return;
        }

        var dialog = ProjectPropertiesDialog.For(project);
        var saved = await dialog.ShowDialog<bool>(this);

        if (saved) ViewModel.StatusMessage = Localizer.Get(StringKeys.StatusPropertiesSaved);
    }

    /// <summary>
    /// The project the properties window should open.
    ///
    /// A project selected in the tree wins over the startup project, so the
    /// window follows what the user is looking at.
    /// </summary>
    private string? SelectedProjectPath()
    {
        if (ViewModel.Explorer.SelectedNode is { Kind: NodeKind.Project } node &&
            Path.GetExtension(node.Path) is ".vbproj" or ".csproj")
            return node.Path;

        return ViewModel.StartupProject;
    }

    private void OnNewSolutionClick(object? sender, RoutedEventArgs e) =>
        _ = ShowNewSolutionDialogAsync();

    private void OnOpenSolutionClick(object? sender, RoutedEventArgs e) =>
        _ = ShowOpenSolutionDialogAsync();

    private async Task ShowOpenSolutionDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri soluzione o progetto",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Soluzioni e progetti .NET")
                {
                    Patterns = ["*.sln", "*.slnx", "*.csproj", "*.vbproj"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) await ViewModel.OpenSolutionAsync(path);
    }

    /// <summary>Creates a new Avalonia window in the selected project and opens it in the designer.</summary>
    private async Task ShowAddWindowDialogAsync()
    {
        if (ViewModel.StartupProject is null)
        {
            ViewModel.StatusMessage = "Aprire prima un progetto.";
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(ViewModel.StartupProject))!;
        var language = SourceLanguageExtensions.FromPath(ViewModel.StartupProject);

        var dialog = new NewWindowDialog(directory, language);
        var created = await dialog.ShowDialog<NewFormResult?>(this);
        if (created is null) return;

        // The tree must immediately show the two newly created files.
        if (ViewModel.SolutionPath is not null) ViewModel.Explorer.Load(ViewModel.SolutionPath);

        await ViewModel.OpenFileAsync(created.XamlPath, inDesigner: true);
        ViewModel.StatusMessage = $"Creata {Path.GetFileName(created.XamlPath)}";
    }

    /// <summary>Creates a new solution with an initial project and opens it.</summary>
    private async Task ShowNewSolutionDialogAsync()
    {
        var dialog = new NewSolutionDialog();
        var created = await dialog.ShowDialog<NewSolutionResult?>(this);
        if (created is null) return;

        await ViewModel.OpenSolutionAsync(created.SolutionPath);

        // The generated main window is opened right away, so the user sees
        // the designer instead of an empty solution.
        if (created.MainWindowXamlPath is not null)
            await ViewModel.OpenFileAsync(created.MainWindowXamlPath, inDesigner: true);
    }
}
