using System.ComponentModel;
using System.Drawing;
using Basalt.Designer;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>Builds previews from supported constants, never from compiled application code.</summary>
internal sealed partial class WinFormsDesignerSession : IDisposable
{
    private sealed record HistoryEntry(string Source, Action? Restore = null, Action? Reverse = null,
        Func<Task>? Prepare = null, Func<Task>? ReversePrepare = null);
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

    public WinFormsDesignerSession(WinFormsDesignerDocument document, WinFormsResourceDocument? resources = null)
    {
        Document = document;
        _resources = resources;
        BuildPreview();
    }

    public string NameOf(Forms.Control control) => _controls.First(pair => ReferenceEquals(pair.Value, control)).Key;
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
        if (!typeof(Forms.Control).IsAssignableFrom(type) || type.Assembly != typeof(Forms.Control).Assembly)
            throw new NotSupportedException("Only built-in Forms controls are available in this toolbox.");
        var number = 1;
        var name = type.Name + number;
        while (Document.DeclaredNames.Contains(name, StringComparer.OrdinalIgnoreCase)) name = type.Name + ++number;
        await ApplyAsync(document => document.AddControl(name, "Global." + type.FullName, parentName));
    }

    public Task DeleteAsync(Forms.Control control, Action? commitCompanion = null,
        Action? undoCompanion = null, Action? redoCompanion = null) =>
        ApplyAsync(document => document.RemoveControl(NameOf(control)), commitCompanion, undoCompanion, redoCompanion);
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
    public Task UndoAsync() => RestoreAsync(_undo, _redo);
    public Task RedoAsync() => RestoreAsync(_redo, _undo);

    private async Task RestoreAsync(Stack<HistoryEntry> from, Stack<HistoryEntry> to)
    {
        if (from.Count == 0) return;
        var entry = from.Peek();
        var original = Document;
        if (entry.Prepare is not null) await entry.Prepare();
        var parsed = await Task.Run(() => new WinFormsDesignerDocument(entry.Source));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (from.Count == 0 || !ReferenceEquals(entry, from.Peek()) || !ReferenceEquals(original, Document))
            throw new InvalidOperationException("The document changed during undo or redo.");
        SourceChanging?.Invoke();
        entry.Restore?.Invoke();
        ObjectDisposedException.ThrowIf(_disposed, this);
        from.Pop();
        to.Push(new(Document.Source, entry.Reverse, entry.Restore, entry.ReversePrepare, entry.Prepare));
        Replace(parsed);
    }

    private async Task ApplyAsync(Func<WinFormsDesignerDocument, string> edit, Action? commit = null,
        Action? undo = null, Action? redo = null, bool commitUnchangedSource = false,
        Func<Task>? undoPrepare = null, Func<Task>? redoPrepare = null)
    {
        var original = Document;
        var parsed = await Task.Run(() => new WinFormsDesignerDocument(edit(original)));
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(original, Document)) throw new InvalidOperationException("The document changed during the edit.");
        if (parsed.Source == original.Source && !commitUnchangedSource) return;
        SourceChanging?.Invoke();
        commit?.Invoke();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _undo.Push(new(original.Source, undo, redo, undoPrepare, redoPrepare));
        _redo.Clear();
        Replace(parsed);
    }

    private void Replace(WinFormsDesignerDocument document)
    {
        var previous = Form;
        var previousFonts = _previewFonts.ToArray();
        var previousComponents = TakeComponentPreviewsForDisposal();
        _previewFonts.Clear();
        Document = document;
        BuildPreview();
        Changed?.Invoke();
        previous.Dispose();
        DisposeComponentPreviews(previousComponents);
        foreach (var font in previousFonts) font.Dispose();
    }

    private WinFormsDesignerControl Model(Forms.Control control) => ReferenceEquals(control, Form)
        ? Document.Root : Document.Controls.Single(model => model.Name.Equals(NameOf(control), StringComparison.OrdinalIgnoreCase));

    private void BuildPreview()
    {
        _controls.Clear();
        _unresolvedProperties.Clear();
        _resourceProperties.Clear();
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
            var type = typeof(Forms.Control).Assembly.GetType(name, false, true);
            if (type is null || !typeof(Forms.Control).IsAssignableFrom(type) || type.IsAbstract)
            {
                problems.Add(model.Name + ": " + model.TypeName);
                continue;
            }
            try
            {
                var control = (Forms.Control)Activator.CreateInstance(type)!;
                control.Name = model.Name;
                _controls[model.Name] = control;
            }
            catch (Exception exception) { problems.Add(model.Name + ": " + exception.GetBaseException().Message); }
        }
        BuildComponentPreviews(problems);
        WinFormsCollectionPreview.Apply(Document, _controls, problems, _resources);
        ApplyProperties(Document.Root, Form, problems);
        foreach (var model in Document.Controls)
        {
            if (!_controls.TryGetValue(model.Name, out var control)) continue;
            ApplyProperties(model, control, problems);
            if (_controls.TryGetValue(model.ParentName, out var parent)) parent.Controls.Add(control);
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
