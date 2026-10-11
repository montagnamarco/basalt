using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Basalt.Core.Model;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Core.Services;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Workspace;
using Basalt.Workspace.Languages;
using LanguageRegistry = Basalt.Extensibility.LanguageRegistry;

namespace Basalt.Shell.ViewModels;

/// <summary>Coordinates explorer, editor, designer, build and diagnostics.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly RoslynLanguageService _languageService = new();
    private readonly MsBuildBuildService _buildService = new();
    private readonly ProcessRunService _runService = new();
    private readonly RoslynFormattingService _formattingService = new();

    /// <summary>
    /// The languages the IDE knows.
    ///
    /// Built here because it needs the shared Roslyn services: a solution is
    /// opened once and both code languages read from that one workspace.
    /// </summary>
    public LanguageRegistry Languages { get; }
    private GitSourceControlService? _git;

    [ObservableProperty] private EditorDocumentViewModel? _activeDocument;
    [ObservableProperty] private DesignerSession? _activeDesigner;
    [ObservableProperty] private string _statusMessage = Localizer.Get(StringKeys.StatusReady);
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcomeVisible))]
    private string? _solutionPath;
    [ObservableProperty] private string? _currentBranch;
    [ObservableProperty] private bool _isRunning;

    /// <summary>
    /// Counts the runs started through <see cref="RunBuiltProjectAsync"/>.
    ///
    /// "dotnet run" exits on a threadpool thread some time after Stop asks it
    /// to, and by then the user may already have started the next run. The
    /// generation that exited is attached to the handler at Start, so a late
    /// exit can be told apart from the one everybody currently cares about.
    /// </summary>
    private int _runGeneration;

    /// <summary>The generation Stop most recently asked to end, or none.</summary>
    private int _stoppedRunGeneration = -1;

    public MainWindowViewModel()
    {
        ShareCompatibilitySession();
        Languages = LanguageCatalog.CreateDefault(_languageService, _formattingService);

        _buildService.OutputReceived += (_, line) => AppendOutput(line);
        Diagnostics.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ErrorSummary));
        OpenDocuments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsWelcomeVisible));
        OpenDocuments.CollectionChanged += ForwardSavesOfOpenDocuments;
        _runService.OutputReceived += (_, line) => AppendOutput(line);
    }

    /// <summary>
    /// True while the welcome screen should cover the docking area.
    ///
    /// It hides as soon as a solution is open, not only when a document is:
    /// opening a solution fills the explorer and the panels behind it, and a
    /// welcome screen still on top would make the IDE look like nothing had
    /// loaded.
    /// </summary>
    public bool IsWelcomeVisible => SolutionPath is null && OpenDocuments.Count == 0;

    /// <summary>Error and warning counts shown in the status bar.</summary>
    public string ErrorSummary
    {
        get
        {
            var errors = Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warnings = Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            return $"\u2297 {errors}   \u26A0 {warnings}";
        }
    }

    public SolutionExplorerViewModel Explorer { get; } = new();
    public ObservableCollection<EditorDocumentViewModel> OpenDocuments { get; } = [];
    public ReplaceableCollection<IdeDiagnostic> Diagnostics { get; } = [];
    public ReplaceableCollection<string> BuildOutput { get; } = [];

    /// <summary>Project used to build and run.</summary>
    public string? StartupProject { get; internal set; }

    /// <summary>
    /// Every project in the solution that can be run.
    ///
    /// A solution with a web project and a console tool has two, and which one
    /// runs is the user's choice rather than whichever was found first.
    /// </summary>
    public IReadOnlyList<string> RunnableProjects { get; private set; } = [];

    /// <summary>
    /// Chooses which project runs.
    ///
    /// Ignored when the path is not one of the solution's own: a stale setting
    /// from another solution should not leave the IDE unable to run anything.
    /// </summary>
    public void SetStartupProject(string projectPath)
    {
        if (!RunnableProjects.Contains(projectPath, StringComparer.Ordinal)) return;

        StartupProject = projectPath;
        OnPropertyChanged(nameof(StartupProject));
    }

    /// <summary>
    /// The long operations in progress, so the status bar can show one and
    /// offer to stop it.
    /// </summary>
    public OperationTracker Operations { get; } = new();

    /// <summary>
    /// Raised when a solution or a file has been opened, so it can be
    /// remembered.
    ///
    /// An event rather than a settings store held here: the window owns the
    /// store, and the model has no business writing to disk.
    /// </summary>
    public event EventHandler<(string Path, bool IsSolution)>? Opened;

    /// <summary>
    /// What a designer edit should leave in the document.
    /// </summary>
    /// <remarks>
    /// For a .frm, the form's own text with the changed positions written into
    /// it — never the markup. Writing XAML into a .frm destroys it, and the
    /// file is the one thing this whole approach promises not to touch.
    ///
    /// The original is edited rather than regenerated because a .frm holds
    /// more than the converter understands: fonts, an OCX's property bag, the
    /// .frx offsets pointing at binary beside it. All of that survives by
    /// never being written.
    /// </remarks>
    private static string WriteBack(EditorDocumentViewModel document, DesignerSession session)
    {
        var markup = session.Document.ToXaml();

        if (document.FilePath is not { } path
            || !path.EndsWith(".frm", StringComparison.OrdinalIgnoreCase))
            return markup;

        try
        {
            return Basalt.Vb6.AxamlToForm.Apply(document.Text, markup);
        }
        catch (Exception)
        {
            // The form as it was. A failure here would otherwise replace a
            // Visual Basic 6 form with Avalonia markup, which is not a change
            // anybody could undo.
            return document.Text;
        }
    }

    /// <summary>
    /// Turns a Visual Basic 6 project into one this IDE can open.
    /// </summary>
    /// <remarks>
    /// Written beside the .vbp rather than over it. What could not be brought
    /// over — a control from an OCX this machine does not have — is put in the
    /// output window, because a conversion that quietly leaves something out
    /// is found later and by accident.
    /// </remarks>
    private string ConvertVisualBasic6Project(string vbpPath)
    {
        var result = Basalt.Vb6.ProjectConversion.Convert(vbpPath);

        AppendOutput($"[vb6] {Path.GetFileName(vbpPath)} -> {Path.GetFileName(result.ProjectPath)}");

        foreach (var source in result.Sources)
            AppendOutput($"[vb6] {source}");

        foreach (var ocx in result.MissingObjects)
            AppendOutput(
                $"[warning] {ocx} is not available here. Controls from it open " +
                "as placeholders.");

        return result.ProjectPath;
    }

    /// <summary>
    /// What was open, so it can be put back next time.
    /// </summary>
    /// <remarks>
    /// Captured rather than tracked as it changes: the state is only wanted
    /// at the moment a solution closes, and keeping it up to date on every
    /// tab would be work done for nothing.
    /// </remarks>
    public SolutionSession CaptureSession() => new()
    {
        SolutionPath = SolutionPath ?? "",
        OpenFiles = [.. OpenDocuments.Select(d => d.FilePath)],
        OpenTabs = [.. OpenDocuments.Select(d => new OpenDocumentSession
        {
            Path = d.FilePath,
            InDesigner = d.OpenInDesigner
        })],
        ActiveFile = ActiveDocument?.FilePath,
        ActiveInDesigner = ActiveDocument?.OpenInDesigner ?? false,
        InDesigner = [.. OpenDocuments.Where(d => d.OpenInDesigner).Select(d => d.FilePath)],
        ExpandedFolders = [.. ReadExpandedFolders?.Invoke() ?? []],
        SelectedPath = Explorer.SelectedNode?.Path,
        PublishRids = [.. MakeExecutableChoice?.Rids ?? []],
        PublishOutputRoot = MakeExecutableChoice?.OutputRoot,
    };

    /// <summary>
    /// How the window lends its solution explorer to the session.
    /// </summary>
    /// <remarks>
    /// The tree is a control and lives in the window; what was open in it is
    /// part of the session and lives here. Two callbacks rather than a
    /// reference to the panel, so this stays testable without one.
    /// </remarks>
    public Func<IReadOnlyList<string>>? ReadExpandedFolders { get; set; }

    /// <summary>Opens the folders a session remembered.</summary>
    public Action<IReadOnlyList<string>>? WriteExpandedFolders { get; set; }

    /// <summary>
    /// Reopens what a solution had open when it was last closed.
    /// </summary>
    /// <remarks>
    /// A file that has gone since is skipped rather than reported: it was
    /// deleted or moved on purpose, and a dialog about it on every startup
    /// would be the IDE nagging about a decision already made.
    /// </remarks>
    public async Task RestoreSessionAsync(SolutionSession session)
    {
        RestoreMakeExecutableChoice(session);

        var tabs = session.OpenTabs.Count > 0
            ? session.OpenTabs
            : session.OpenFiles.Select(file => new OpenDocumentSession
            {
                Path = file,
                InDesigner = session.InDesigner.Contains(file)
            });

        foreach (var tab in tabs)
        {
            if (!File.Exists(tab.Path)) continue;

            await OpenFileAsync(tab.Path, tab.InDesigner).ConfigureAwait(true);
        }

        // After the files, so the tree has been rebuilt for whatever they
        // brought with them.
        WriteExpandedFolders?.Invoke(session.ExpandedFolders);
        if (session.SelectedPath is { Length: > 0 } selected)
            Explorer.SelectPathQuietly(selected);

        if (session.ActiveFile is { Length: > 0 } active
            && OpenDocuments.FirstOrDefault(d =>
                string.Equals(d.FilePath, active, StringComparison.Ordinal)
                && d.OpenInDesigner == (session.OpenTabs.Count > 0
                    ? session.ActiveInDesigner
                    : session.InDesigner.Contains(active))) is { } document)
        {
            ActivateDocument(document);
        }
    }

    /// <summary>Records what the current solution has open, if any.</summary>
    public void RememberSession()
    {
        if (SolutionPath is not { Length: > 0 }) return;
        var session = CaptureSession();
        SolutionUserSessionStore.Write(session);

        if (LoadSettings is null || SaveSettings is null) return;

        var settings = LoadSettings();
        settings.Session.Remember(session);
        SaveSettings(settings);
    }

    /// <summary>
    /// Closes everything belonging to the solution being left.
    /// </summary>
    /// <remarks>
    /// Opening a second solution used to leave the first one's files in their
    /// tabs, where they belong to a project that is no longer loaded: their
    /// diagnostics are stale and going to a definition lands nowhere.
    /// </remarks>
    public void CloseAllDocuments()
    {
        foreach (var document in OpenDocuments.ToList()) ForgetDesignerSession(document);

        OpenDocuments.Clear();
        ActiveDocument = null;
    }

    /// <summary>
    /// Where the remembered sessions are kept, when there is somewhere.
    /// </summary>
    /// <remarks>
    /// Set by the window, which owns the settings file. Null in a test that
    /// never asked for the behaviour, where opening a solution should not
    /// start writing to the user's settings.
    /// </remarks>
    public Func<IdeSettings>? LoadSettings { get; set; }

    /// <summary>Writes the settings back; set together with <see cref="LoadSettings"/>.</summary>
    public Action<IdeSettings>? SaveSettings { get; set; }

    public async Task OpenSolutionAsync(string path)
    {
        // What the solution being left had open, before it is left: after the
        // documents are closed there is nothing to record.
        RememberSession();

        // And the documents themselves. Opening a second solution used to
        // leave the first one's files in their tabs, belonging to a project
        // no longer loaded.
        CloseAllDocuments();

        BeginBusy();
        StatusMessage = Localizer.Get(StringKeys.StatusOpening, Path.GetFileName(path));
        try
        {
            // A Visual Basic 6 project is opened by writing a .NET one beside
            // it and opening that: from there it is an ordinary project, with
            // the IntelliSense and the debugger that already work. The .frm
            // and .bas files are left exactly as Visual Basic 6 wrote them and
            // are read during the build.
            if (Path.GetExtension(path).Equals(".vbp", StringComparison.OrdinalIgnoreCase))
                path = ConvertVisualBasic6Project(path);

            // A single source file that another language claims (QuickBASIC
            // today) is not a project MSBuild can load. It is opened as a
            // document and Roslyn is told nothing about it; see
            // MainWindowViewModel.SourceFiles.cs for how it is run.
            var sourceFileLanguage = RoutedProviderFor(path);

            SolutionPath = path;
            Explorer.Load(path);
            Opened?.Invoke(this, (path, true));
            RunnableProjects = FindRunnableProjects(path);
            StartupProject = FindStartupProject(path);

            // Also makes this the solution Windows desktop projects are
            // retargeted for (see Compatibility).
            if (sourceFileLanguage is null)
                await _languageService.OpenSolutionAsync(path).ConfigureAwait(true);
            else
                await _languageService.CloseSolutionAsync().ConfigureAwait(true);

            foreach (var warning in _languageService.LoadWarnings) AppendOutput($"[warning] {warning}");
            await ReportMissingWindowsDesktopSourcesAsync(path).ConfigureAwait(true);

            await DetectRepositoryAsync(path).ConfigureAwait(true);

            // Back to whatever was open last time, which is what makes
            // reopening a project resuming rather than starting again.
            if ((SolutionUserSessionStore.Read(path)
                 ?? LoadSettings?.Invoke().Session.For(path)) is { } remembered)
                await RestoreSessionAsync(remembered).ConfigureAwait(true);

            // The source file is what the user opened, so it is the document
            // in front; OpenFileAsync leaves it alone when a restored tab has
            // it already.
            if (sourceFileLanguage is not null)
                await OpenFileAsync(path).ConfigureAwait(true);

            // Guarded: opening a solution runs in the background from the
            // moment the window loads, and nothing else waits for it. By the
            // time every await above finishes, a debug session can already
            // be running — "Opened X" would then land after, and overwrite,
            // whatever that session's own status (say, Solution Explorer's
            // "while debugging" refusal) had just told the user. Opened is
            // no longer news at that point either, so it is simply skipped
            // rather than raced against.
            if (IsDebuggingProbe?.Invoke() != true)
                StatusMessage = Localizer.Get(StringKeys.StatusOpened, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusOpenFailed, ex.Message);
            AppendOutput($"[error] {ex.Message}");
        }
        finally
        {
            EndBusy();
        }
    }

    /// <summary>Opens a file: .axaml files in the designer, everything else in the text editor.</summary>
    public async Task OpenFileAsync(string path, bool inDesigner = false)
    {
        var existing = OpenDocuments.FirstOrDefault(d =>
            string.Equals(d.FilePath, path, StringComparison.Ordinal) && d.OpenInDesigner == inDesigner);

        if (existing is not null)
        {
            ActivateDocument(existing);
            return;
        }

        var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        var document = new EditorDocumentViewModel(path, text, inDesigner);
        OpenDocuments.Add(document);
        ActivateDocument(document);

        Opened?.Invoke(this, (path, false));
    }

    /// <summary>
    /// Why a document could not be opened in the designer, when it could not.
    ///
    /// Kept so the view can say what is wrong instead of showing an empty
    /// surface: a broken XAML is repaired by editing it.
    /// </summary>
    public Dictionary<EditorDocumentViewModel, string> DesignerProblems { get; } = [];

    /// <summary>The parser's complaint, with the line it is on.</summary>
    private static string Describe(System.Xml.XmlException ex) =>
        ex.LineNumber > 0
            ? $"This file cannot be shown in the designer: {ex.Message} "
            + $"(line {ex.LineNumber}, column {ex.LinePosition})"
            : $"This file cannot be shown in the designer: {ex.Message}";

    /// <summary>
    /// Raised once both the active document and the design session have been
    /// updated.
    ///
    /// The view must rebuild its content here and not by reacting to the
    /// ActiveDocument change: that notification arrives while ActiveDesigner
    /// still holds the previous value, and the view would pick the wrong
    /// control.
    /// </summary>
    public event EventHandler? ActiveDocumentChanged;

    /// <summary>
    /// Raised whenever an open document is written to disk, by any path (see
    /// <see cref="EditorDocumentViewModel.Saved"/>); possibly off the interface thread.
    ///
    /// A file just saved can be a file git has not seen yet: without this,
    /// the Git Changes panel kept showing whatever it last read, blind to an
    /// edit the user had just written to disk, until some unrelated git
    /// command happened to refresh it.
    /// </summary>
    public event EventHandler? DocumentSaved;

    private readonly HashSet<EditorDocumentViewModel> _forwardingSaves = [];

    /// <summary>
    /// Follows the open documents by comparing with what is followed, not from the change's old and
    /// new items: Clear (closing a solution) reports a reset with no old items, and the closed
    /// documents would have gone on reporting their saves.
    /// </summary>
    private void ForwardSavesOfOpenDocuments(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var closed in _forwardingSaves.Where(document => !OpenDocuments.Contains(document)).ToArray())
        {
            closed.Saved -= OnOpenDocumentSaved;
            _forwardingSaves.Remove(closed);
        }

        foreach (var opened in OpenDocuments.Where(_forwardingSaves.Add))
            opened.Saved += OnOpenDocumentSaved;
    }

    private void OnOpenDocumentSaved(object? sender, EventArgs e) => DocumentSaved?.Invoke(sender, e);

    /// <summary>
    /// Active design sessions, one per document open in the designer.
    ///
    /// With dockable panels several designers can stay visible at the same
    /// time, even side by side: a single shared session would show the wrong
    /// content in all but one of them.
    /// </summary>
    private readonly Dictionary<EditorDocumentViewModel, DesignerSession> _designerSessions = [];

    /// <summary>
    /// The document text each live session in <see cref="_designerSessions"/>
    /// was last known to agree with.
    /// </summary>
    /// <remarks>
    /// A session built from text the user has since edited by hand (outside
    /// the designer) is a stale tree, and the next designer edit would write
    /// it back over what they typed: <see cref="DesignerSessionFor"/> rebuilds
    /// when the document's text no longer matches this stored value.
    ///
    /// The comparison used to recompute <c>session.Document.ToXaml()</c> and
    /// compare that against <c>document.Text</c> on every call. That is wrong
    /// at the one moment it matters: a hand-written file's formatting (spacing,
    /// attribute order, comments, line endings) almost never matches the
    /// serialiser's, so the very first comparison after opening such a file
    /// already called the brand-new session stale and rebuilt it with new
    /// <c>XElement</c> instances. Worse, <c>RefreshDesignerToolDocks</c>
    /// (MainWindow.DesignerToolDocks.cs) asks for the session twice — once
    /// synchronously, once deferred at <c>DispatcherPriority.Loaded</c> — and a
    /// rebuild between those two calls handed the Element Tree / Properties /
    /// Events docks a session that <see cref="ActiveDesigner"/> (set only once,
    /// in <see cref="ActivateDocument"/>) never saw: a tree click then selected
    /// an <c>XElement</c> nobody else was showing.
    ///
    /// Remembering the exact text a session last produced — its parse source
    /// right after creation, or whatever it already wrote back after an edit —
    /// and comparing the document's current text against that stored value,
    /// instead of recomputing a fresh serialisation, keeps the two in step for
    /// the document's whole lifetime: the stored value only goes stale when
    /// something outside the session changes the text directly (reopening the
    /// file as another encoding, a future external-reload or text-undo
    /// feature) — the one case a rebuild is genuinely needed, and then every
    /// caller in the same dispatcher turn sees the new session because the
    /// comparison, not a retry, is what decides.
    /// </remarks>
    private readonly Dictionary<EditorDocumentViewModel, string> _designerSessionText = [];

    /// <summary>Forgets a session and the text it was last known to agree with.</summary>
    private void ForgetDesignerSession(EditorDocumentViewModel document)
    {
        _designerSessions.Remove(document);
        _designerSessionText.Remove(document);
    }

    /// <summary>
    /// Design session for the given document, created on first request.
    /// Returns null for documents opened in the text editor.
    /// </summary>
    public DesignerSession? DesignerSessionFor(EditorDocumentViewModel document)
    {
        if (!document.OpenInDesigner) return null;
        if (document.FilePath.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase)) return null;

        if (_designerSessions.ContainsKey(document)
            && (!_designerSessionText.TryGetValue(document, out var lastSynced)
                || !string.Equals(document.Text, lastSynced, StringComparison.Ordinal)))
        {
            ForgetDesignerSession(document);
        }

        if (!_designerSessions.TryGetValue(document, out var session))
        {
            // The code-behind language follows that of the project containing
            // the file, not the .axaml extension.
            var language = DetectProjectLanguage(document.FilePath);

            try
            {
                // A Visual Basic 6 form is read and turned into the markup the
                // designer works on. Its controls carry Canvas.Left and
                // Canvas.Top, which is what a .frm is: everything positioned
                // absolutely and staying where it was put.
                var markup = document.FilePath is { } path
                    && path.EndsWith(".frm", StringComparison.OrdinalIgnoreCase)
                        ? Basalt.Vb6.FormToAxaml.Convert(
                            Basalt.Vb6.FormFile.Parse(document.Text),
                            Path.GetFileNameWithoutExtension(path))
                        : document.Text;

                session = new DesignerSession(
                    XamlDocument.Parse(markup, document.FilePath), language);
            }
            catch (System.Xml.XmlException ex)
            {
                // A .axaml that will not parse is the normal state of one
                // being written. Throwing here left the tab blank with no
                // hint of what was wrong; the caller shows the text instead.
                DesignerProblems[document] = Describe(ex);
                return null;
            }

            // Every designer edit written back into the document, so the tab
            // shows its dot and Save writes what was drawn. Without this the
            // XAML lived only in the session: controls were added, the
            // preview showed them, and closing the file threw them away with
            // nothing warning that anything was unsaved.
            //
            // The text just written is also what this session now agrees
            // with: remembered so the next DesignerSessionFor call compares
            // against it instead of re-serialising (see _designerSessionText).
            session.DocumentModified += (_, _) =>
            {
                document.Text = WriteBack(document, session);
                _designerSessionText[document] = document.Text;
            };

            _designerSessions[document] = session;
            _designerSessionText[document] = document.Text;
        }

        DesignerProblems.Remove(document);

        return session;
    }

    private void ActivateDocument(EditorDocumentViewModel document)
    {
        ActiveDesigner = DesignerSessionFor(document);

        // Last: updating the active tab notifies the view.
        ActiveDocument = document;
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Activates the document the user picked in the tab bar.
    /// The binding has already updated ActiveDocument, but the design session
    /// must be realigned to the new document.
    /// </summary>
    public void ActivateFromTabSelection(EditorDocumentViewModel document) =>
        ActivateDocument(document);

    [RelayCommand]
    private async Task SaveAsync()
    {
        var document = ActiveDocument;
        if (document is null) return;

        await SaveDocumentAsync(document).ConfigureAwait(true);
        StatusMessage = Localizer.Get(StringKeys.StatusSaved, document.FileName);
    }

    /// <summary>
    /// Writes one document to disk, with every step a save needs: designer
    /// write-back, format-on-save and keeping the language service in sync.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="SaveAsync"/> and <see cref="SaveAllAsync"/> so
    /// Save All cannot drift from what a single Save does to the active
    /// document — it used to call <see cref="EditorDocumentViewModel.SaveAsync"/>
    /// directly, which only writes the file: every other document saved this
    /// way kept its unformatted text, a stale Roslyn copy, and (for a WinForms
    /// designer tab that was not the active one) markup drawn but never
    /// written back from its session.
    /// </remarks>
    private async Task SaveDocumentAsync(EditorDocumentViewModel document)
    {
        // In the designer the source of truth is the XAML document, not the
        // tab's text: it must be realigned before writing to disk. Through the
        // same path as an edit, because this is the one that reaches the file
        // — writing markup here would leave Avalonia XAML in a .frm.
        //
        // Looked up per document, not through ActiveDesigner: that only
        // describes whichever document is active, and Save All walks every
        // modified document, active or not.
        //
        // Only while the session still describes the text: the same tab can be
        // switched to its text view and edited by hand, and the session does
        // not follow that. Writing it back then would overwrite what was typed
        // with the markup from before; the text is what gets saved, and the
        // designer is rebuilt from it next time it is shown.
        if (_designerSessions.TryGetValue(document, out var session))
        {
            if (_designerSessionText.TryGetValue(document, out var produced) && produced == document.Text)
            {
                document.Text = WriteBack(document, session);
                _designerSessionText[document] = document.Text;
            }
            else
            {
                ForgetDesignerSession(document);
            }
        }

        if (FormatOnSave && !document.OpenInDesigner)
            await FormatDocumentAsync(document).ConfigureAwait(true);

        await document.SaveAsync().ConfigureAwait(true);
        await _languageService.UpdateDocumentAsync(document.FilePath, document.Text).ConfigureAwait(true);
        await SaveWinFormsCompanionAsync(document).ConfigureAwait(true);
    }

    /// <summary>
    /// Tidies a run of text rather than the whole file.
    ///
    /// The formatting service could already do it; nothing exposed it, so the
    /// Format Selection command did nothing when chosen.
    /// </summary>
    public Task<Basalt.Core.Services.FormattingResult> FormatRangeAsync(
        string text, SourceLanguage language, int start, int length) =>
        _formattingService.FormatRangeAsync(text, language, start, length);

    /// <summary>
    /// Saves every document that has unsaved changes.
    ///
    /// The command existed in the registry and did nothing when chosen from
    /// the palette.
    /// </summary>
    public async Task SaveAllAsync()
    {
        var pending = OpenDocuments.Where(d => d.IsModified).ToList();

        foreach (var document in pending)
        {
            // A form's companion files are saved together with whichever of them comes first;
            // saving one again here would write it twice.
            if (!document.IsModified) continue;

            await SaveDocumentAsync(document).ConfigureAwait(true);
        }

        StatusMessage = pending.Count switch
        {
            0 => Localizer.Get(StringKeys.StatusNothingToSave),
            1 => Localizer.Get(StringKeys.StatusSaved, pending[0].FileName),
            _ => Localizer.Get(StringKeys.StatusSavedMany, pending.Count)
        };

    }

    /// <summary>
    /// Makes the language service forget an edit the user chose not to save.
    /// </summary>
    /// <remarks>
    /// Closing a tab with "Don't Save" leaves whatever the user last typed
    /// sitting in Roslyn's in-memory copy of the file: nothing about closing
    /// the tab tells the workspace the edit never happened. Reading the file
    /// back in, off the interface thread, is what keeps another file's
    /// diagnostics, completion and refactorings from answering against text
    /// that was never saved. A no-op when the file is gone (deleted from
    /// Solution Explorer, say): there is nothing on disk to read back.
    /// </remarks>
    public async Task ForgetDiscardedEditAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var diskText = await Task.Run(() => File.ReadAllText(filePath)).ConfigureAwait(true);

        // The read above is the only part of this that takes any real time,
        // and the user may have reopened the file while it ran: that tab
        // already pushed its own text into the language service, newer than
        // the disk snapshot read for the tab that just closed. Pushing the
        // old snapshot in now would overwrite it.
        if (OpenDocuments.Any(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal))) return;

        await _languageService.UpdateDocumentAsync(filePath, diskText).ConfigureAwait(true);
    }

    /// <summary>Closes the document being edited.</summary>
    public void CloseActiveDocument()
    {
        if (ActiveDocument is not { } document) return;

        CloseDocument(document);
    }

    /// <summary>
    /// Closes a document with no save prompt, active or not.
    ///
    /// Used when Solution Explorer deletes the file underneath an open tab:
    /// by then there is nothing left on disk to save unsaved text to, so
    /// asking would only offer a choice with no good answer.
    /// </summary>
    public void CloseDocument(EditorDocumentViewModel document)
    {
        ForgetDesignerSession(document);
        DesignerProblems.Remove(document);

        var wasActive = ActiveDocument == document;

        OpenDocuments.Remove(document);

        if (!wasActive) return;

        // The last one closed leaves nothing active, and the welcome screen
        // comes back on its own.
        if (OpenDocuments.LastOrDefault() is { } next) ActivateDocument(next);
        else ActiveDocument = null;
    }

    /// <summary>
    /// Makes the Roslyn workspace see a rename, move or delete Solution
    /// Explorer just made on disk, off the interface thread: reopening the
    /// solution reads every project file, and on a large one is not free.
    /// </summary>
    /// <remarks>
    /// Reopening re-creates the workspace from disk
    /// (<see cref="RoslynLanguageService.ReloadAsync"/>), which is what
    /// keeps a rename from being written back over by MSBuildWorkspace's own
    /// stale copy of a moved file — see that method's remarks — but it also
    /// means every open tab's unsaved text reverts to what reopening just
    /// read off disk. Pushing every open document's current buffer back in
    /// afterwards is what keeps those edits from being lost, not only the
    /// ones under the path that was renamed or deleted: reopening discards
    /// the whole workspace, not just the affected corner of it.
    /// </remarks>
    public async Task ReloadLanguageServiceAsync()
    {
        await Task.Run(() => _languageService.ReloadAsync()).ConfigureAwait(true);

        // A copy: OpenDocuments can change while this awaits (closing a tab,
        // opening another), and enumerating the live collection across an
        // await throws once it does.
        foreach (var document in OpenDocuments.ToList())
            await _languageService.UpdateDocumentAsync(document.FilePath, document.Text).ConfigureAwait(true);
    }

    /// <summary>Deletes what the last build produced.</summary>
    public Task CleanAsync() => BuildWithTargetAsync("clean");

    /// <summary>Cleans and then builds.</summary>
    public async Task RebuildAsync()
    {
        await CleanAsync().ConfigureAwait(true);
        await BuildAsync().ConfigureAwait(true);
    }

    /// <summary>The process Clean or Rebuild last started, for the tests.</summary>
    internal System.Diagnostics.ProcessStartInfo? LastTargetStartInfo { get; private set; }

    private async Task BuildWithTargetAsync(string target)
    {
        if (StartupProject is null)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return;
        }

        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            ArgumentList = { target, StartupProject, "-c", Configuration, "--nologo" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Clean and rebuild print into the Output panel as well.
        Basalt.Workspace.DotnetProcess.UseInterfaceLanguage(start);

        await AddCompatibilityArgumentsAsync(start).ConfigureAwait(true);

        LastTargetStartInfo = start;
        using var process = System.Diagnostics.Process.Start(start);

        if (process is null) return;

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(true);

        await process.WaitForExitAsync().ConfigureAwait(true);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            AppendOutput(line.TrimEnd());

        StatusMessage = process.ExitCode == 0
            ? Localizer.Get(StringKeys.StatusCleaned)
            : Localizer.Get(StringKeys.StatusCleanFailed);
    }

    /// <summary>Works out what a quick action would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewQuickActionAsync(
        string filePath, Basalt.Workspace.QuickAction action) =>
        _languageService.PreviewQuickActionAsync(filePath, action);

    /// <summary>Works out what renaming a symbol would change.</summary>
    /// <summary>What naming the selected expression as a constant would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewExtractConstantAsync(
        string filePath, int start, int length, string name) =>
        _languageService.PreviewExtractConstantAsync(filePath, start, length, name);

    /// <summary>What putting a variable's value back into its uses would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewInlineVariableAsync(
        string filePath, int position) =>
        _languageService.PreviewInlineVariableAsync(filePath, position);

    /// <summary>What converting an If to a Select Case, or back, would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewConvertConditionalAsync(
        string filePath, int position) =>
        _languageService.PreviewConvertConditionalAsync(filePath, position);

    /// <summary>What generating a constructor from the type's fields would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewGenerateConstructorAsync(
        string filePath, int position) =>
        _languageService.PreviewGenerateConstructorAsync(filePath, position);

    /// <summary>What generating a property in front of a field would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewGeneratePropertyAsync(
        string filePath, int position) =>
        _languageService.PreviewGeneratePropertyAsync(filePath, position);

    /// <summary>What moving the type the caret is in to its own file would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewMoveTypeToFileAsync(
        string filePath, int position) =>
        _languageService.PreviewMoveTypeToFileAsync(filePath, position);

    /// <summary>The parameters of the method the caret is in.</summary>
    public Task<IReadOnlyList<(string Name, string Type)>> GetParametersAsync(
        string filePath, int position) =>
        _languageService.GetParametersAsync(filePath, position);

    /// <summary>What changing a method's parameters, and its calls, would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewChangeSignatureAsync(
        string filePath, int position, Basalt.Workspace.Refactoring.SignatureChange change) =>
        _languageService.PreviewChangeSignatureAsync(filePath, position, change);

    /// <summary>What tidying the file's imports would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewTidyImportsAsync(
        string filePath) => _languageService.PreviewTidyImportsAsync(filePath);

    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewAddImportAsync(
        string filePath, string typeName) =>
        _languageService.PreviewAddImportAsync(filePath, typeName);

    /// <summary>
    /// What moving the selected markup into a partial view would change.
    ///
    /// Not a Roslyn refactoring: a .vbhtml is not a document it holds, so
    /// this one is worked out from the template itself.
    /// </summary>
    public static Basalt.Workspace.Refactoring.RefactoringPreview PreviewExtractPartial(
        string filePath, string text, int start, int length, string name) =>
        new Basalt.Workspace.Refactoring.ExtractPartialRefactoring()
            .Preview(filePath, text, start, length, name);

    /// <summary>What lifting the selected statements into a method would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewExtractMethodAsync(
        string filePath, int start, int length, string name) =>
        _languageService.PreviewExtractMethodAsync(filePath, start, length, name);

    /// <summary>What naming the selected expression would change.</summary>
    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewExtractVariableAsync(
        string filePath, int start, int length, string name) =>
        _languageService.PreviewExtractVariableAsync(filePath, start, length, name);

    public Task<Basalt.Workspace.Refactoring.RefactoringPreview> PreviewRenameAsync(
        string filePath, int position, string newName) =>
        _languageService.PreviewRenameAsync(filePath, position, newName);

    /// <summary>Writes what a preview described.</summary>
    public static Task<bool> ApplyRefactoringAsync(
        Basalt.Workspace.Refactoring.RefactoringPreview preview) =>
        RoslynLanguageService.ApplyAsync(preview);

    /// <summary>
    /// Puts the editor's text into the workspace.
    ///
    /// The workspace holds the file as last saved; a refactoring worked out
    /// from that would move text the user has since changed.
    /// </summary>
    public async Task SyncActiveDocumentAsync()
    {
        if (ActiveDocument is not { } document) return;

        await _languageService
            .UpdateDocumentAsync(document.FilePath, document.Text)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The repository the solution is in, or null when there is none.
    ///
    /// Null is the normal case for a project not under source control, so
    /// callers show empty panels rather than treating it as an error.
    /// </summary>
    public ISourceControlService? Repository => _git;

    /// <summary>Raised when the repository's state may have changed.</summary>
    public event EventHandler? RepositoryChanged;

    /// <summary>
    /// Re-reads the branch, and tells the panels to refresh.
    ///
    /// Called after any operation that can move the repository on: a commit,
    /// a checkout, a merge.
    /// </summary>
    public async Task RefreshRepositoryAsync()
    {
        if (_git is null) return;

        try
        {
            CurrentBranch = await _git.GetCurrentBranchAsync().ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            AppendOutput($"[git] {ex.Message}");
        }

        RepositoryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Detects whether the solution lives inside a git repository.</summary>
    private async Task DetectRepositoryAsync(string solutionPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath));
        if (directory is null) return;

        try
        {
            var git = new GitSourceControlService(directory);
            if (!await git.IsRepositoryAsync(directory).ConfigureAwait(true)) return;

            _git = git;
            CurrentBranch = await git.GetCurrentBranchAsync().ConfigureAwait(true);

            RepositoryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException)
        {
            // git not installed: the IDE stays fully usable without it.
            _git = null;
        }
    }

    /// <summary>
    /// Formats the active document in place.
    ///
    /// Applies to text documents only: in the designer the XAML is generated
    /// from the document tree, so its layout is not the author's to preserve.
    /// </summary>
    [RelayCommand]
    private async Task FormatDocumentAsync()
    {
        if (ActiveDocument is not { OpenInDesigner: false } document) return;

        await FormatDocumentAsync(document).ConfigureAwait(true);
    }

    /// <summary>Formats a given document in place, active or not.</summary>
    private async Task FormatDocumentAsync(EditorDocumentViewModel document)
    {
        if (document.Language is not SourceLanguage.VisualBasic) return;

        var result = await _formattingService
            .FormatAsync(document.Text, document.Language)
            .ConfigureAwait(true);

        if (!result.Changed) return;

        document.Text = result.Text;
        StatusMessage = Localizer.Get(StringKeys.StatusFormatted, document.FileName);
    }

    /// <summary>Formats a document before saving, when the option is enabled.</summary>
    public bool FormatOnSave { get; set; } = true;

    [RelayCommand]
    private void Undo()
    {
        if (DesignerUndoRequested is { } undo) undo();
        else ActiveDesigner?.Undo();
    }

    [RelayCommand]
    private void Redo()
    {
        if (DesignerRedoRequested is { } redo) redo();
        else ActiveDesigner?.Redo();
    }

    /// <summary>Re-indents the caret's line, used by the editor while typing.</summary>
    public Task<TypingFormattingResult> FormatLineAsync(
        string text, SourceLanguage language, int caret) =>
        _formattingService.FormatLineAsync(text, language, caret);

    /// <summary>Whether typing this character should re-indent its line.</summary>
    public bool TriggersFormatting(char character, SourceLanguage language) =>
        _formattingService.TriggersFormatting(character, language);

    /// <summary>Whether typing this character finishes a word in this language.</summary>
    public bool CompletesWord(char character, SourceLanguage language) =>
        _formattingService.CompletesWord(character, language);

    /// <summary>Applies Visual Basic typing conventions to the caret's line.</summary>
    public Task<TypingFormattingResult> ApplyTypingConventionsAsync(
        string text, SourceLanguage language, int caret) =>
        _formattingService.ApplyTypingConventionsAsync(text, language, caret);

    /// <summary>Completes conventions for a line the author has finished.</summary>
    public Task<TypingFormattingResult> CompleteLineAsync(
        string text, SourceLanguage language, int caret) =>
        _formattingService.CompleteLineAsync(text, language, caret);

    /// <summary>
    /// Corrects identifier casing on a line against the symbols in the project,
    /// turning "console.readline" into "Console.ReadLine".
    ///
    /// Returns the text unchanged when the file belongs to no open project, or
    /// while the surrounding code does not resolve.
    /// </summary>
    public async Task<string> CorrectIdentifierCasingAsync(
        string filePath, string text, int line)
    {
        try
        {
            var changes = await _languageService
                .GetIdentifierCasingChangesAsync(filePath, text, line)
                .ConfigureAwait(true);

            if (changes.Count == 0) return text;

            return Microsoft.CodeAnalysis.Text.SourceText.From(text)
                .WithChanges(changes)
                .ToString();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Half-typed code often fails to resolve; casing is not worth an error.
            return text;
        }
    }

    /// <summary>
    /// The edits that write the members a finished Implements or Inherits
    /// line obliges its type to have, none when nothing is missing.
    /// </summary>
    public async Task<IReadOnlyList<Microsoft.CodeAnalysis.Text.TextChange>> ImplementMembersAsync(
        string filePath, string text, int line)
    {
        try
        {
            return await _languageService.ImplementMembersAsync(filePath, text, line).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Half-typed code may not bind; the line then stays as typed.
            return [];
        }
    }

    /// <summary>
    /// Closing line for a block opened on the given line, or null when the line
    /// opens no block or the block is already closed.
    /// </summary>
    public async Task<string?> GetBlockClosingAsync(string text, SourceLanguage language, int line)
    {
        if (language != SourceLanguage.VisualBasic) return null;

        return await VisualBasicBlockCompleter.GetClosingFor(text, line).ConfigureAwait(true);
    }

    /// <summary>Indentation a new line at this position should start with.</summary>
    public Task<int> GetIndentationAsync(string text, SourceLanguage language, int position) =>
        _formattingService.GetIndentationAsync(text, language, position);

    /// <summary>Where the user has been, for stepping back and forward.</summary>
    public NavigationHistory History { get; } = new();

    /// <summary>Declarations in a document, for the outline.</summary>
    public Task<IReadOnlyList<Extensibility.DocumentSymbol>> GetDocumentSymbolsAsync(
        string filePath, string text) =>
        WithUpdatedDocumentAsync(filePath, text,
            () => _languageService.GetDocumentSymbolsAsync(filePath));

    /// <summary>
    /// Where the symbol at a position is declared. A Razor view's model member
    /// goes to the .vb file declaring it, as the language that owns the file
    /// says; Visual Basic is answered by the compiler.
    /// </summary>
    public Task<(string FilePath, int Line, int Column)?> GoToDefinitionAsync(
        string filePath, string text, int position) =>
        RoutedProviderFor(filePath) is { } provider
            ? ProviderDefinitionAsync(provider, filePath, position, text)
            : WithUpdatedDocumentAsync(filePath, text,
                () => _languageService.GoToDefinitionAsync(filePath, position));

    /// <summary>Everywhere the symbol at a position is used.</summary>
    public Task<IReadOnlyList<Extensibility.SourceLocation>> FindReferencesAsync(
        string filePath, string text, int position) =>
        WithUpdatedDocumentAsync(filePath, text,
            () => _languageService.FindReferencesAsync(filePath, position));

    /// <summary>Symbols across the solution matching a query.</summary>
    public Task<IReadOnlyList<Extensibility.SourceLocation>> SearchSymbolsAsync(string query) =>
        _languageService.SearchSymbolsAsync(query);

    /// <summary>
    /// The same search, with each symbol's name and kind, for a list that shows them.
    /// </summary>
    public Task<IReadOnlyList<RoslynLanguageService.FoundSymbol>> SearchSymbolDetailsAsync(string query) =>
        _languageService.SearchSymbolDetailsAsync(query);

    /// <summary>
    /// Pushes the editor's current text to the language service before asking
    /// it something.
    ///
    /// The workspace copy lags what the user is typing, and answering from it
    /// would point at positions that have since moved.
    /// </summary>
    private async Task<T> WithUpdatedDocumentAsync<T>(
        string filePath, string text, Func<Task<T>> ask)
    {
        await _languageService.UpdateDocumentAsync(filePath, text).ConfigureAwait(true);
        return await ask().ConfigureAwait(true);
    }

    /// <summary>
    /// Completions for the editor: Roslyn for Visual Basic, the file's own
    /// language for everything else.
    /// </summary>
    public Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        string filePath, int offset, string? currentText = null, char? typed = null) =>
        RoutedProviderFor(filePath) is { } provider
            ? ProviderCompletionsAsync(provider, filePath, offset, currentText ?? TextOf(filePath))
            : _languageService.GetCompletionsAsync(filePath, offset, currentText, typed: typed);

    /// <summary>Whether typing a character opens the list there, as Roslyn decides.</summary>
    public Task<bool> ShouldTriggerCompletionAsync(string filePath, int offset, string currentText, char typed) =>
        _languageService.ShouldTriggerCompletionAsync(filePath, offset, currentText, typed);

    public Task<string?> GetQuickInfoAsync(string filePath, int offset) =>
        _languageService.GetQuickInfoAsync(filePath, offset);

    /// <summary>
    /// What the symbol at a position is, in the shape every language fills.
    ///
    /// Asked of the language that owns the file rather than of Roslyn: a
    /// QuickBASIC file has no compilation and still has something to say.
    /// </summary>
    public async Task<Basalt.Extensibility.SymbolDescription?> DescribeSymbolAsync(
        string filePath, int offset, string currentText)
    {
        if (DescriberFor(filePath) is { } describer)
            return await describer
                .DescribeAsync(
                    new Basalt.Extensibility.LanguageDocument(filePath, currentText), offset)
                .ConfigureAwait(true);

        // A language with no describer of its own (Razor) answers hover through
        // its navigation provider, as the other routed questions do.
        return RoutedProviderFor(filePath) is { } provider
            ? await ProviderQuickInfoAsync(provider, filePath, offset, currentText).ConfigureAwait(true)
            : null;
    }

    /// <summary>What the call being written at a position expects.</summary>
    public async Task<Basalt.Extensibility.SymbolDescriptionSet?> DescribeCallAsync(
        string filePath, int offset, string currentText)
    {
        if (DescriberFor(filePath) is not { } describer) return null;

        return await describer
            .DescribeCallAsync(
                new Basalt.Extensibility.LanguageDocument(filePath, currentText), offset)
            .ConfigureAwait(true);
    }

    private Basalt.Extensibility.ISymbolDescriptionProvider? DescriberFor(string filePath) =>
        Languages.ForFile(filePath)?.Descriptions;

    /// <summary>
    /// What the call being written expects.
    ///
    /// The language service could answer and the editor never asked: typing
    /// an open bracket showed nothing, and the parameters had to be looked up
    /// somewhere else.
    /// </summary>
    public Task<Basalt.Extensibility.SignatureHelp?> GetSignatureHelpAsync(
        string filePath, int offset, string currentText) =>
        _languageService.GetSignatureHelpAsync(filePath, offset, currentText);

    /// <summary>
    /// Recomputes the problems of one document. A Razor view's problems come
    /// from the Razor provider, already placed on the template; Visual Basic
    /// keeps the compiler path it always had.
    /// </summary>
    public async Task RefreshDiagnosticsAsync(EditorDocumentViewModel document)
    {
        if (RoutedProviderFor(document.FilePath) is { } provider)
        {
            var found = await ProviderDiagnosticsAsync(provider, document.FilePath, document.Text)
                .ConfigureAwait(true);

            ReplaceDiagnosticsOf(document.FilePath, found);
            return;
        }

        await _languageService.UpdateDocumentAsync(document.FilePath, document.Text).ConfigureAwait(true);
        var diagnostics = await _languageService.GetDiagnosticsAsync(document.FilePath).ConfigureAwait(true);

        ReplaceDiagnosticsOf(document.FilePath, diagnostics);
    }

    /// <summary>
    /// Replaces the diagnostics of one file and keeps those of every other, as
    /// the last build produced them, announcing the change once.
    /// </summary>
    internal void ReplaceDiagnosticsOf(string filePath, IEnumerable<IdeDiagnostic> diagnostics)
    {
        var kept = Diagnostics
            .Where(d => !string.Equals(d.FilePath, filePath, StringComparison.Ordinal))
            .Concat(diagnostics);

        Diagnostics.ReplaceAll(kept);
    }

    /// <summary>
    /// Writes a line to the output panel.
    ///
    /// Public so the debugger can report what the program being debugged
    /// prints, which belongs in the same place as build and run output.
    /// </summary>
    public void WriteOutput(string line) => AppendOutput(line);

    /// <summary>
    /// Puts the IDE back to "nothing running", wherever the news came from.
    ///
    /// Process.Exited is raised on a threadpool thread some time after the
    /// process actually dies, so this ignores two kinds of late news: a
    /// generation Stop already reported as stopped, and a generation that is
    /// no longer the one running — either one would overwrite "Application
    /// stopped", or a status a newer run has since set, with a stale exit
    /// code that nobody asked about.
    /// </summary>
    private void OnRunExited(int code, int generation) => OnUiThread(() =>
    {
        if (generation != _runGeneration || generation == _stoppedRunGeneration) return;

        IsRunning = false;
        StatusMessage = Localizer.Get(StringKeys.StatusRunExited, code);
        AppendOutput(Localizer.Get(StringKeys.StatusApplicationExited, code));
    });

    /// <summary>Reports a finished run, as the process does. For tests.</summary>
    internal void ReportRunExitedForTests(int code) => OnRunExited(code, _runGeneration);

    /// <summary>
    /// Reports a finished run tagged with a specific generation, so a test
    /// can simulate the exit of a run that is no longer the current one
    /// arriving after a newer run has already started.
    /// </summary>
    internal void ReportRunExitedForTests(int code, int generation) => OnRunExited(code, generation);

    /// <summary>Says a program is running, for tests that need it to stop.</summary>
    internal void SetRunningForTests(bool running) => IsRunning = running;

    /// <summary>The "dotnet" arguments the last run launched with. For tests.</summary>
    internal IReadOnlyList<string> LastLaunchArgumentsForTests => _runService.LastArguments;

    /// <summary>
    /// Runs something on the interface thread.
    /// </summary>
    /// <remarks>
    /// Anything that reaches this view model from a process, a build or a
    /// debugger arrives on a thread of its own, and everything listening to
    /// it is a control. Posted rather than waited for: the thread that
    /// reported has other work, and blocking it to let the interface catch
    /// up is how a deadlock starts.
    /// </remarks>
    private static void OnUiThread(Action work)
    {
        if (Dispatcher.UIThread.CheckAccess()) work();
        else Dispatcher.UIThread.Post(work);
    }

    private const int MaxOutputLines = 5000;
    private readonly List<string> _pendingOutput = [];
    private bool _outputDrainScheduled;

    private void AppendOutput(string line)
    {
        // Onto the UI thread first: MSBuild reports each line from a worker,
        // and adding to the collection from there raises CollectionChanged on
        // that thread — where the panel handling it touches controls and
        // Avalonia throws. It brought the whole application down mid-build.
        //
        // The lines of a burst are queued and added together, so a build that
        // prints thousands of lines raises a handful of events, not thousands.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            lock (_pendingOutput)
            {
                _pendingOutput.Add(line);
                if (_outputDrainScheduled) return;
                _outputDrainScheduled = true;
            }

            Dispatcher.UIThread.Post(DrainOutput);
            return;
        }

        // Anything queued from a worker came first.
        DrainOutput();
        BuildOutput.AddRange([line], MaxOutputLines);
    }

    private void DrainOutput()
    {
        string[] lines;
        lock (_pendingOutput)
        {
            lines = [.. _pendingOutput];
            _pendingOutput.Clear();
            _outputDrainScheduled = false;
        }

        // The cap keeps unbounded output from eating memory during long builds.
        BuildOutput.AddRange(lines, MaxOutputLines);
    }

    /// <summary>Finds the executable project to build first.</summary>
    /// <summary>
    /// The projects that produce something runnable.
    ///
    /// Read from the project files rather than guessed from their names: a
    /// library called "Runner" is still a library.
    /// </summary>
    private static IReadOnlyList<string> FindRunnableProjects(string solutionOrProjectPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionOrProjectPath));

        if (directory is null) return [];

        try
        {
            return
            [
                .. Directory
                    .EnumerateFiles(directory, "*.*proj", SearchOption.AllDirectories)
                    .Where(p => p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
                             || p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                             && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    .Where(IsRunnable)
                    .Order(StringComparer.Ordinal)
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Whether a project builds something that can be started.</summary>
    private static bool IsRunnable(string projectPath)
    {
        try
        {
            var text = File.ReadAllText(projectPath);

            // The Web SDK infers an executable without saying OutputType.
            return text.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase)
                || text.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Sdk=\"Microsoft.NET.Sdk.Web\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? FindStartupProject(string solutionOrProjectPath)
    {
        if (Path.GetExtension(solutionOrProjectPath) is ".csproj" or ".vbproj")
            return solutionOrProjectPath;

        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionOrProjectPath));
        if (directory is null) return null;

        var projects = Directory
            .EnumerateFiles(directory, "*.*proj", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p) is ".csproj" or ".vbproj")
            // Build output holds copies of the project file, and one picked
            // from there builds into a folder that is deleted on the next
            // clean.
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        // An executable project, and among several the one whose name matches
        // the solution's: a solution called Basalt with a Basalt.Shell and a
        // Basalt.Razor.Vb.LanguageServer has an obvious answer, and taking
        // whichever the filesystem listed first gets it wrong about half the
        // time.
        var executables = projects.Where(IsExecutable).ToList();

        if (executables.Count == 0) return projects.FirstOrDefault();
        if (executables.Count == 1) return executables[0];

        var solutionName = Path.GetFileNameWithoutExtension(solutionOrProjectPath);

        return executables.FirstOrDefault(p =>
                   Path.GetFileNameWithoutExtension(p)
                       .Equals(solutionName, StringComparison.OrdinalIgnoreCase))
            // Then the shallowest: a program sits beside its solution far more
            // often than a tool three folders down does.
            ?? executables.OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
                          .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                          .First();
    }

    /// <summary>Whether a project builds something that can be run.</summary>
    /// <remarks>
    /// By reading OutputType rather than looking for "Exe" anywhere in the
    /// file. The word appears inside "exercising" in a comment, so a test
    /// project was picked as the one to run and Build reported "no project" or
    /// built the wrong thing — decided, in the end, by a sentence of English
    /// prose.
    ///
    /// A project with no OutputType is a library: the SDK defaults it to
    /// Library, and a web project that means to be run says WinExe or Exe like
    /// any other.
    /// </remarks>
    private static bool IsExecutable(string projectPath)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(projectPath),
                @"<OutputType>\s*(\w+)\s*</OutputType>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return match.Success
                && match.Groups[1].Value.EndsWith("Exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Infers the code-behind language from the project containing the file,
    /// walking up the folders to the first project file.
    /// </summary>
    private static SourceLanguage DetectProjectLanguage(string filePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

        while (directory is not null)
        {
            if (Directory.EnumerateFiles(directory, "*.vbproj").Any()) return SourceLanguage.VisualBasic;
            directory = Path.GetDirectoryName(directory);
        }

        // Nothing recognisable above the file: Visual Basic, which is the
        // only language Basalt has, rather than a guess it cannot honour.
        return SourceLanguage.VisualBasic;
    }

    public void Dispose()
    {
        // A single-file program is not owned by the run service, so the run
        // service's Dispose does not end it: closing the IDE would orphan it.
        StopSourceFileRun();

        _languageService.Dispose();
        _runService.Dispose();
        _formattingService.Dispose();
    }
}
