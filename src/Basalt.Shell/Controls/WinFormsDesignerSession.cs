using System.ComponentModel;
using System.Drawing;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// Builds previews from supported constants. Project controls are the exception: they are
/// created from the project's own build output in a design context (see WinFormsDesignAssemblyContext).
/// </summary>
internal sealed partial class WinFormsDesignerSession : IDisposable
{
    /// <summary>
    /// One undo step. <see cref="Culture"/> is the language the edit was made in (null for "(Default)"): undo and
    /// redo show that language before they apply the edit, because Designer.vb is shared by every language.
    /// </summary>
    private sealed record HistoryEntry(string Source, Action? Restore = null, Action? Reverse = null,
        Func<Task>? Prepare = null, Func<Task>? ReversePrepare = null, string? Culture = null);
    private readonly Stack<HistoryEntry> _undo = [];
    private readonly Stack<HistoryEntry> _redo = [];
    private readonly Dictionary<string, Forms.Control> _controls = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private readonly List<Font> _previewFonts = [];
    private readonly HashSet<(string Control, string Property)> _unresolvedProperties = [];
    public WinFormsDesignerDocument Document { get; private set; }
    public Forms.Form Form { get; private set; } = null!;
    public IReadOnlyList<string> PreviewProblems { get; private set; } = [];
    public event Action? Changed;
    public event Action? SourceChanging;

    /// <summary>
    /// The .vbproj the edited form belongs to, or null when none was found: resolved once at
    /// construction (the form does not move projects mid-session) so the live preview can
    /// decode a "Project resource file" Image/Icon the same way
    /// <see cref="WinFormsImageResourceDialog"/>'s "Project resource file" list already does.
    /// </summary>
    private readonly string? _projectPath;

    /// <summary>
    /// The build output the project's own controls come from, or null before the first successful
    /// build. The designer owns it; <see cref="UseDesignContext"/> swaps it after a rebuild.
    /// </summary>
    private WinFormsDesignAssemblyContext? _designContext;

    public WinFormsDesignerSession(WinFormsDesignerDocument document, WinFormsResourceDocument? resources = null,
        string? projectPath = null, WinFormsDesignAssemblyContext? designContext = null)
    {
        Document = document;
        _resources = resources;
        _projectPath = projectPath;
        _designContext = designContext;
        try
        {
            BuildPreview();
        }
        catch
        {
            // A failed build leaves what it made, and nothing shows it. It is released as a failed rebuild releases its own, from
            // an empty snapshot: the shown preview before the build was nothing.
            ReleasePartialPreview(new PreviewSnapshot(null!, new(StringComparer.OrdinalIgnoreCase),
                new(StringComparer.OrdinalIgnoreCase), [], [], [], [], [], [], []));
            throw;
        }
    }

    /// <summary>
    /// The designer's name of a control: its name, or for a container's panel (SplitContainer1.Panel1) the owner and the panel as
    /// the designer's code names it. Any other control that is not named throws.
    /// </summary>
    public string NameOf(Forms.Control control)
    {
        var match = _controls.FirstOrDefault(pair => ReferenceEquals(pair.Value, control));
        if (match.Key is not null) return match.Key;
        return PanelKeyOf(control) ?? _controls.First(pair => ReferenceEquals(pair.Value, control)).Key;
    }

    /// <summary>The owner and panel name of a container's panel that is shown, found through the typed panels only.</summary>
    private string? PanelKeyOf(Forms.Control control)
    {
        foreach (var (owner, ownerControl) in _controls)
            foreach (var panel in PanelNames)
                if (ReferenceEquals(HolderOf(ownerControl, panel), control) && !ReferenceEquals(ownerControl, control))
                    return owner + "." + panel;
        return null;
    }
    public Forms.Control? FindControl(string name) => _controls.GetValueOrDefault(name);
    public bool IsExplicit(Forms.Control control, string property) => Model(control).Properties.ContainsKey(property);
    public string? ExplicitValue(Forms.Control control, string property) => IsExplicit(control, property)
        ? ReadPreviewProperty(control, property) : null;

    internal Task ApplyCollectionAsync(Func<WinFormsDesignerDocument, string> edit) => ApplyAsync(edit);

    public Task SetPropertyAsync(Forms.Control control, string property, string? text)
    {
        if (text is null) return ApplyPropertyChangesAsync([ResetChange(control, property)]);
        var value = ConvertValue(control, property, text);
        try { return ApplyPropertyChangesAsync([ValueChange(control, property, value)]); }
        finally { if (value is Font { IsSystemFont: false } font) font.Dispose(); }
    }

    public Task SetBoundsAsync(Forms.Control control, Rectangle bounds)
    {
        if (ReferenceEquals(control, Form))
        {
            var width = bounds.Width - (Form.Width - Form.ClientSize.Width);
            var height = bounds.Height - (Form.Height - Form.ClientSize.Height);
            return ApplyPropertyChangesAsync([ValueChange(control, "ClientSize", new Size(width, height))]);
        }
        return ApplyPropertyChangesAsync([
            ValueChange(control, "Location", bounds.Location),
            ValueChange(control, "Size", bounds.Size)]);
    }

    public async Task AddControlAsync(Type type, string parentName)
    {
        if (!typeof(Forms.Control).IsAssignableFrom(type) || !WinFormsDesignTypes.IsDesignable(type, _designContext))
            throw new NotSupportedException(Localizer.Get(StringKeys.WinFormsDesignerNotDesignable));
        var number = 1;
        var name = type.Name + number;
        while (Document.DeclaredNames.Contains(name, StringComparer.OrdinalIgnoreCase)) name = type.Name + ++number;
        await ApplyControlEditAsync(document => document.AddControl(name, "Global." + type.FullName, parentName));
    }

    public Task DeleteAsync(Forms.Control control, Action? commitCompanion = null,
        Action? undoCompanion = null, Action? redoCompanion = null) =>
        ApplyControlEditAsync(document => document.RemoveControl(NameOf(control)), commitCompanion, undoCompanion, redoCompanion);
    public Task SetBoundsBatchAsync(IReadOnlyList<WinFormsBoundsEdit> edits)
    {
        var changes = new List<PropertyChange>();
        foreach (var edit in edits)
        {
            if (ReferenceEquals(edit.Control, Form))
                throw new ArgumentException("The form cannot be moved as part of a control group.");
            changes.Add(ValueChange(edit.Control, "Location", edit.After.Location));
            changes.Add(ValueChange(edit.Control, "Size", edit.After.Size));
        }
        return ApplyPropertyChangesAsync(changes);
    }
    /// <summary>The undo steps the session holds. Tests read it to prove that a refused or failed edit adds none.</summary>
    internal int UndoDepth => _undo.Count;
    public Task UndoAsync() => RestoreAsync(_undo, _redo);
    public Task RedoAsync() => RestoreAsync(_redo, _undo);

    private async Task RestoreAsync(Stack<HistoryEntry> from, Stack<HistoryEntry> to)
    {
        if (from.Count == 0) return;
        var entry = from.Peek();
        var original = Document;
        // Designer.vb is shared by every language, so the edit is applied only while the language it was made in is shown.
        await ShowEditCultureAsync(entry.Culture);
        if (entry.Prepare is not null) await entry.Prepare();
        var parsed = await Task.Run(() => new WinFormsDesignerDocument(entry.Source));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (from.Count == 0 || !ReferenceEquals(entry, from.Peek()) || !ReferenceEquals(original, Document))
            throw new InvalidOperationException("The document changed during undo or redo.");
        SourceChanging?.Invoke();
        entry.Restore?.Invoke();
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The step moves to the other stack before the rebuild, so Changed listeners see the new state, and moves back if the
        // rebuild fails. The companion edits are reverted by the same failure, so the step and its files stay in agreement.
        from.Pop();
        to.Push(new(original.Source, entry.Reverse, entry.Restore, entry.ReversePrepare, entry.Prepare, entry.Culture));
        try
        {
            ReplaceWithCompanion(parsed, entry.Reverse);
        }
        catch
        {
            to.Pop();
            from.Push(entry);
            throw;
        }
    }

    /// <summary>
    /// Shows <paramref name="document"/>. A rebuild that throws puts the shown preview back (see <see cref="Replace"/>) and
    /// then runs <paramref name="revert"/>, the companion edit that goes with the step, so the files the step wrote are
    /// put back with the document. The exception of the rebuild is rethrown; a revert that also fails is reported with it.
    /// </summary>
    private void ReplaceWithCompanion(WinFormsDesignerDocument document, Action? revert)
    {
        try
        {
            Replace(document);
        }
        catch (Exception rebuildFailure)
        {
            if (revert is not null)
            {
                try
                {
                    revert();
                }
                catch (Exception revertFailure)
                {
                    throw new AggregateException(rebuildFailure, revertFailure);
                }
            }
            throw;
        }
    }

    private async Task ApplyAsync(Func<WinFormsDesignerDocument, string> edit, Action? commit = null,
        Action? undo = null, Action? redo = null, bool commitUnchangedSource = false,
        Func<Task>? undoPrepare = null, Func<Task>? redoPrepare = null)
    {
        // Captured before the first await: the language this edit is made in, which its undo and redo show again.
        var culture = _culture;
        var original = Document;
        var parsed = await Task.Run(() => new WinFormsDesignerDocument(edit(original)));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(original, Document)) throw new InvalidOperationException("The document changed during the edit.");
        // Designer.vb is shared by every language: a structural or component edit made while a culture is
        // shown would change every language, so it is refused before anything is written. The culture's own
        // resx writes keep Designer.vb unchanged and reach this method only through commitUnchangedSource.
        if (_culture is not null && parsed.Source != original.Source)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerNotInLanguage));
        if (parsed.Source == original.Source && !commitUnchangedSource) return;
        SourceChanging?.Invoke();
        commit?.Invoke();
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The step is pushed before the rebuild so that Changed listeners already see it. A rebuild that fails rolls the
        // document back, so the step and the redo steps it cleared are taken back too: Undo must never revert an edit
        // that did not reach the document.
        var redoBefore = _redo.ToArray();
        _undo.Push(new(original.Source, undo, redo, undoPrepare, redoPrepare, culture));
        _redo.Clear();
        try
        {
            // The commit ran the companion edit, so a failed rebuild reverts it through the undo companion.
            ReplaceWithCompanion(parsed, commit is null ? null : undo);
        }
        catch
        {
            _undo.Pop();
            RestoreRedoSteps(redoBefore);
            throw;
        }
    }

    /// <summary>Puts back the redo steps a failed edit had cleared. <paramref name="steps"/> is in stack order, top first.</summary>
    private void RestoreRedoSteps(IReadOnlyList<HistoryEntry> steps)
    {
        _redo.Clear();
        // Pushed from the bottom up, so the step that was on top before the edit is on top again.
        for (var index = steps.Count - 1; index >= 0; index--) _redo.Push(steps[index]);
    }

    /// <summary>
    /// Shows the preview built from <paramref name="document"/>. BuildPreview fills the session's own state as it goes,
    /// and a project control's Controls.Add can throw partway, so the shown preview is captured first. A failed build
    /// puts it back: the Document, the Form the surface holds, the control map and the rest stay as they were, and the
    /// build's own controls are disposed. Only a built preview is shown, and only then is the previous one disposed.
    /// </summary>
    private void Replace(WinFormsDesignerDocument document)
    {
        var previousDocument = Document;
        var shown = CapturePreview();
        // The components and fonts of the shown preview leave the session for the build to make its own. They are
        // disposed once the new preview is shown; a failed build gives them back from the snapshot.
        TakeComponentPreviewsForDisposal();
        _previewFonts.Clear();
        Document = document;
        try
        {
            BuildPreview();
        }
        catch
        {
            Document = previousDocument;
            ReleasePartialPreview(shown);
            RestoreFailureRecords(shown);
            throw;
        }
        Changed?.Invoke();
        shown.Form.Dispose();
        DisposeComponentPreviews(shown.Components.Values);
        foreach (var font in shown.Fonts) font.Dispose();
    }

    private WinFormsDesignerControl Model(Forms.Control control) => ReferenceEquals(control, Form)
        ? Document.Root : Document.Controls.Single(model => model.Name.Equals(NameOf(control), StringComparison.OrdinalIgnoreCase));

    private void BuildPreview()
    {
        _controls.Clear();
        _unresolvedProperties.Clear();
        _resourceProperties.Clear();
        _userCodeFailures.Clear();
        var problems = new List<string>();
        Form = new Forms.Form
        {
            Name = Document.ClassName,
            Text = Document.ClassName,
            ClientSize = new Size(800, 500),
            VisualStylesOverride = true
        };
        _controls["Me"] = Form;
        foreach (var model in Document.Controls)
        {
            var name = model.TypeName.Replace("Global.", "", StringComparison.OrdinalIgnoreCase);
            var type = WinFormsDesignTypes.Find(name, _designContext);
            if (type is null || !typeof(Forms.Control).IsAssignableFrom(type) || type.IsAbstract)
            {
                problems.Add(model.Name + ": " + model.TypeName);
                continue;
            }
            try
            {
                _controls[model.Name] = CreateControl(model, type);
            }
            catch (Exception exception) { problems.Add(model.Name + ": " + exception.GetBaseException().Message); }
        }
        BuildComponentPreviews(problems);
        WinFormsCollectionPreview.Apply(Document, _controls, problems, _resources, _projectPath);
        ApplyProperties(Document.Root, Form, problems);
        foreach (var model in Document.Controls)
        {
            if (!_controls.TryGetValue(model.Name, out var control)) continue;
            ApplyProperties(model, control, problems);
            if (_controls.TryGetValue(model.ParentName, out var parent)) AddToPlacement(parent, model, control, problems);
            else problems.Add(model.Name + ": " + model.ParentName);
        }
        ApplyZOrderPreview();
        PreviewProblems = problems;
    }

    private void ApplyProperties(WinFormsDesignerControl model, Forms.Control control, List<string> problems)
    {
        ApplyResourceProperties(model, control, problems);
    }

    private static object? ConvertValue(Forms.Control control, string property, string text)
    {
        var descriptor = TypeDescriptor.GetProperties(control)[property]
            ?? throw new ArgumentException("The property does not exist.", nameof(property));
        return descriptor.Converter.ConvertFromInvariantString(text);
    }

    public void Dispose()
    {
        _disposed = true;
        Form.Dispose();
        DisposeComponentPreviews(TakeComponentPreviewsForDisposal());
        foreach (var font in _previewFonts) font.Dispose();
        _previewFonts.Clear();
    }
}
