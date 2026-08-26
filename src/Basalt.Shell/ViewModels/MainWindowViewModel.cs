using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Basalt.Core.Model;
using Basalt.Core.Localization;
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

    public MainWindowViewModel()
    {
        Languages = LanguageCatalog.CreateDefault(_languageService, _formattingService);

        _buildService.OutputReceived += (_, line) => AppendOutput(line);
        Diagnostics.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ErrorSummary));
        OpenDocuments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsWelcomeVisible));
        _runService.OutputReceived += (_, line) => AppendOutput(line);
        _runService.Exited += (_, code) =>
        {
            IsRunning = false;
            AppendOutput(Localizer.Get(StringKeys.StatusApplicationExited, code));
        };
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
    public ObservableCollection<IdeDiagnostic> Diagnostics { get; } = [];
    public ObservableCollection<string> BuildOutput { get; } = [];

    /// <summary>Project used to build and run.</summary>
    public string? StartupProject { get; private set; }

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

    public async Task OpenSolutionAsync(string path)
    {
        IsBusy = true;
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

            SolutionPath = path;
            Explorer.Load(path);
            Opened?.Invoke(this, (path, true));
            RunnableProjects = FindRunnableProjects(path);
            StartupProject = FindStartupProject(path);

            await _languageService.OpenSolutionAsync(path).ConfigureAwait(true);

            foreach (var warning in _languageService.LoadWarnings) AppendOutput($"[warning] {warning}");

            await DetectRepositoryAsync(path).ConfigureAwait(true);
            StatusMessage = Localizer.Get(StringKeys.StatusOpened, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusOpenFailed, ex.Message);
            AppendOutput($"[error] {ex.Message}");
        }
        finally
        {
            IsBusy = false;
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
    /// Active design sessions, one per document open in the designer.
    ///
    /// With dockable panels several designers can stay visible at the same
    /// time, even side by side: a single shared session would show the wrong
    /// content in all but one of them.
    /// </summary>
    private readonly Dictionary<EditorDocumentViewModel, DesignerSession> _designerSessions = [];

    /// <summary>
    /// Design session for the given document, created on first request.
    /// Returns null for documents opened in the text editor.
    /// </summary>
    public DesignerSession? DesignerSessionFor(EditorDocumentViewModel document)
    {
        if (!document.OpenInDesigner) return null;

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
            session.DocumentModified += (_, _) => document.Text = session.Document.ToXaml();

            _designerSessions[document] = session;
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
        if (ActiveDocument is null) return;

        // In the designer the source of truth is the XAML document, not the
        // tab's text: it must be realigned before writing to disk.
        if (ActiveDesigner is not null)
            ActiveDocument.Text = ActiveDesigner.Document.ToXaml();

        if (FormatOnSave && !ActiveDocument.OpenInDesigner)
            await FormatDocumentAsync().ConfigureAwait(true);

        await ActiveDocument.SaveAsync().ConfigureAwait(true);
        await _languageService.UpdateDocumentAsync(ActiveDocument.FilePath, ActiveDocument.Text).ConfigureAwait(true);
        StatusMessage = Localizer.Get(StringKeys.StatusSaved, ActiveDocument.FileName);
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

        foreach (var document in pending) await document.SaveAsync().ConfigureAwait(true);

        StatusMessage = pending.Count switch
        {
            0 => Localizer.Get(StringKeys.StatusNothingToSave),
            1 => Localizer.Get(StringKeys.StatusSaved, pending[0].FileName),
            _ => Localizer.Get(StringKeys.StatusSavedMany, pending.Count)
        };
    }

    /// <summary>Closes the document being edited.</summary>
    public void CloseActiveDocument()
    {
        if (ActiveDocument is not { } document) return;

        OpenDocuments.Remove(document);

        // The last one closed leaves nothing active, and the welcome screen
        // comes back on its own.
        if (OpenDocuments.LastOrDefault() is { } next) ActivateDocument(next);
        else ActiveDocument = null;
    }

    /// <summary>Deletes what the last build produced.</summary>
    public Task CleanAsync() => BuildWithTargetAsync("clean");

    /// <summary>Cleans and then builds.</summary>
    public async Task RebuildAsync()
    {
        await CleanAsync().ConfigureAwait(true);
        await BuildAsync().ConfigureAwait(true);
    }

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

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (StartupProject is null)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return;
        }

        IsBusy = true;
        BuildOutput.Clear();
        Diagnostics.Clear();
        StatusMessage = Localizer.Get(StringKeys.StatusBuilding);

        // Tracked so the status bar can offer to stop it: a build that has
        // gone wrong is one the user wants to end, not wait out.
        var operation = Operations.Start(Localizer.Get(StringKeys.StatusBuilding));

        try
        {
            var result = await _buildService
                .BuildAsync(StartupProject, Configuration, operation.Token)
                .ConfigureAwait(true);

            foreach (var diagnostic in result.Diagnostics) Diagnostics.Add(diagnostic);

            var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            var warnings = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

            StatusMessage = result.Succeeded
                ? Localizer.Get(StringKeys.StatusBuildSucceeded, result.Duration.TotalSeconds, warnings)
                : Localizer.Get(StringKeys.StatusBuildFailed, errors, warnings);
        }
        catch (OperationCanceledException)
        {
            // Asked for, so it is reported as stopped rather than as a
            // failure the user has to look into.
            StatusMessage = "Build stopped.";
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusBuildInterrupted, ex.Message);
        }
        finally
        {
            Operations.Finish(operation);
            IsBusy = false;
        }
    }

    /// <summary>
    /// Builds the startup project and reports the assembly to debug.
    ///
    /// Returns null when the build failed or produced nothing to run, so the
    /// caller does not start a debugger against a stale assembly.
    /// </summary>
    public async Task<string?> BuildForDebuggingAsync()
    {
        if (StartupProject is null)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusNoProject);
            return null;
        }

        await BuildAsync().ConfigureAwait(true);

        if (Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return null;

        return FindOutputAssembly(StartupProject);
    }

    /// <summary>
    /// Which configuration is built and run.
    ///
    /// The toolbar offers Debug and Release and stores the choice; nothing
    /// read it, so Release built Debug and then looked for the result in the
    /// Debug folder — right by accident, and wrong the moment anything asked
    /// for the other one.
    /// </summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>
    /// Finds the assembly a project just built.
    ///
    /// The path is read from the file system rather than from the project
    /// file, because the target framework folder is decided by the build, a
    /// project may target more than one, and the assembly name need not match
    /// the project's.
    /// </summary>
    private string? FindOutputAssembly(string projectPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (directory is null) return null;

        var output = Path.Combine(directory, "bin", Configuration);

        // The chosen configuration first; the other one only as a fallback,
        // because a stale assembly from the wrong configuration is worse than
        // saying nothing was found.
        if (!Directory.Exists(output)) return null;

        var name = Path.GetFileNameWithoutExtension(projectPath);

        // Newest first: with several target frameworks built, the one just
        // produced is the one to debug.
        var byName = Directory
            .EnumerateFiles(output, $"{name}.dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (byName is not null) return byName;

        // AssemblyName can differ from the project's file name. The newest
        // .dll that is not a dependency copied in beside it.
        return Directory
            .EnumerateFiles(output, "*.dll", SearchOption.AllDirectories)
            .Where(HasEntryPointBeside)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether a .dll is the project's own output rather than a dependency.
    ///
    /// The build writes a .runtimeconfig.json beside the assembly it produced
    /// and beside no other.
    /// </summary>
    private static bool HasEntryPointBeside(string assembly) =>
        File.Exists(Path.ChangeExtension(assembly, ".runtimeconfig.json"));

    /// <summary>The folder the debugged program should run in.</summary>
    public string? StartupDirectory => StartupProject is null
        ? null
        : Path.GetDirectoryName(Path.GetFullPath(StartupProject));

    [RelayCommand]
    private async Task RunAsync()
    {
        if (StartupProject is null) return;

        // We build before running: "--no-build" prevents dotnet run from
        // silently rebuilding and hiding the errors from the user.
        await BuildAsync().ConfigureAwait(true);
        if (Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return;

        try
        {
            _runService.Start(StartupProject, Configuration);
            IsRunning = true;
            StatusMessage = Localizer.Get(StringKeys.StatusRunning);
        }
        catch (Exception ex)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusStartFailed, ex.Message);
        }
    }

    [RelayCommand]
    private void StopRun()
    {
        _runService.Stop();
        IsRunning = false;
        StatusMessage = Localizer.Get(StringKeys.StatusStopped);
    }

    /// <summary>The fixes offered for the problems at a position.</summary>
    public Task<IReadOnlyList<Basalt.Workspace.QuickAction>> GetQuickActionsAsync(
        string filePath, int position) =>
        _languageService.GetQuickActionsAsync(filePath, position);

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
    private void Undo() => ActiveDesigner?.Undo();

    [RelayCommand]
    private void Redo() => ActiveDesigner?.Redo();

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

    /// <summary>Where the symbol at a position is declared.</summary>
    public Task<(string FilePath, int Line, int Column)?> GoToDefinitionAsync(
        string filePath, string text, int position) =>
        WithUpdatedDocumentAsync(filePath, text,
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

    /// <summary>Completions for the editor, delegated to Roslyn.</summary>
    public Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        string filePath, int offset, string? currentText = null) =>
        _languageService.GetCompletionsAsync(filePath, offset, currentText);

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
        if (DescriberFor(filePath) is not { } describer) return null;

        return await describer
            .DescribeAsync(
                new Basalt.Extensibility.LanguageDocument(filePath, currentText), offset)
            .ConfigureAwait(true);
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

    public async Task RefreshDiagnosticsAsync(EditorDocumentViewModel document)
    {
        await _languageService.UpdateDocumentAsync(document.FilePath, document.Text).ConfigureAwait(true);
        var diagnostics = await _languageService.GetDiagnosticsAsync(document.FilePath).ConfigureAwait(true);

        // Only the diagnostics of the affected file are replaced, so as not to
        // wipe those produced by the last build on the other files.
        var others = Diagnostics
            .Where(d => !string.Equals(d.FilePath, document.FilePath, StringComparison.Ordinal))
            .ToList();

        Diagnostics.Clear();
        foreach (var d in others) Diagnostics.Add(d);
        foreach (var d in diagnostics) Diagnostics.Add(d);
    }

    /// <summary>
    /// Writes a line to the output panel.
    ///
    /// Public so the debugger can report what the program being debugged
    /// prints, which belongs in the same place as build and run output.
    /// </summary>
    public void WriteOutput(string line) => AppendOutput(line);

    private void AppendOutput(string line)
    {
        // Onto the UI thread first: MSBuild reports each line from a worker,
        // and adding to the collection from there raises CollectionChanged on
        // that thread — where the panel handling it touches controls and
        // Avalonia throws. It brought the whole application down mid-build.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendOutput(line));
            return;
        }

        BuildOutput.Add(line);

        // Unbounded output would eat memory during long builds.
        while (BuildOutput.Count > 5000) BuildOutput.RemoveAt(0);
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
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        // An executable project is preferred, recognizable from OutputType.
        return projects.FirstOrDefault(IsExecutable) ?? projects.FirstOrDefault();
    }

    private static bool IsExecutable(string projectPath)
    {
        try
        {
            return File.ReadAllText(projectPath).Contains("Exe", StringComparison.OrdinalIgnoreCase);
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
        _languageService.Dispose();
        _runService.Dispose();
        _formattingService.Dispose();
    }
}
