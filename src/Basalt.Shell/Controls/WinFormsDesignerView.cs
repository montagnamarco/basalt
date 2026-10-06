using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.ComponentModel;
using System.IO.Ports;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>The Forms designer owns its toolbox and properties independently of XAML sessions.</summary>
internal sealed partial class WinFormsDesignerView : UserControl, IDisposable
{
    private readonly EditorDocumentViewModel _document;
    private readonly WinFormsDesignSurface _surface = new();
    private readonly WinFormsPropertyPanel _properties = new();
    private readonly WinFormsEventPanel _events = new();
    private readonly Func<string, Task<EditorDocumentViewModel>>? _openCode;
    private readonly Func<string, EditorDocumentViewModel?>? _findCode;
    private readonly Func<string, Task<EditorDocumentViewModel>>? _getCode;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolboxList _toolboxPanel = new();
    private ListBox _toolbox => _toolboxPanel.List;
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private WinFormsDesignerSession? _session;
    private bool _busy;
    private bool _attached;
    private string _selectedName = "Me";
    private string[] _editSelectionNames = [];
    private bool _rebuildingSurface;

    public WinFormsDesignerView(EditorDocumentViewModel document,
        Func<string, Task<EditorDocumentViewModel>>? openCode = null,
        Func<string, EditorDocumentViewModel?>? findCode = null,
        Func<string, Task<EditorDocumentViewModel>>? getCode = null,
        Func<string, string, int, string, Task<Basalt.Workspace.Refactoring.RefactoringPreview>>? rename = null,
        bool dockTools = false)
    {
        _document = document;
        _openCode = openCode;
        _findCode = findCode;
        _getCode = getCode;
        _rename = rename;
        var undo = new Button { Content = Localizer.Get(StringKeys.MenuEditUndo) };
        var redo = new Button { Content = Localizer.Get(StringKeys.MenuEditRedo) };
        undo.Click += (_, _) => RunEditAsync(() => _session!.UndoAsync());
        redo.Click += (_, _) => RunEditAsync(() => _session!.RedoAsync());
        var toolbar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Children = { undo, redo, _layoutToolbar }
        };
        _toolboxPanel.Show(
            WinFormsToolboxCatalog.Order(
                WinFormsToolboxCatalog.Describe(typeof(Forms.Control).Assembly, typeof(Forms.Control))
                    .Where(item => !typeof(Forms.Form).IsAssignableFrom(item.ControlType)
                        && item.ControlType != typeof(Forms.WebBrowser) && item.ControlType != typeof(Forms.AxHost)
                        && item.ControlType != typeof(Forms.ContextMenuStrip))
                    .Concat(new[] { typeof(Forms.Timer), typeof(Forms.ImageList), typeof(Forms.ToolTip), typeof(Forms.ContextMenuStrip), typeof(SerialPort) }
                        .Select(type => new WinFormsToolboxItem(type)))),
            item => item.Category,
            item => [item.DisplayName],
            WinFormsToolboxVisuals.CreateRow);
        _toolboxPanel.ItemActivated += (_, activated) => AddTool(activated as WinFormsToolboxItem);
        var body = CreateDesignerBody(dockTools);
        var layout = new DockPanel { Margin = new Thickness(8), Children = { toolbar, _status, body } };
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_status, Avalonia.Controls.Dock.Bottom);
        Content = layout;

        _surface.SelectionChanged += ShowProperties;
        InitializeInlineMenuEditor();
        InitializeInlineToolStripEditor();
        InitializeSmartTag();
        WinFormsToolboxDrag.Attach(_toolbox);
        _surface.ControlDropRequested += drop => RunEditAsync(() => PlaceControlAsync(drop));
        InitializeExtendedDesigner();
        InitializeZOrderActions();
        InitializeClipboardCommands();
        _properties.ObjectSelectionRequested += name =>
        {
            if (_session?.FindComponent(name) is { } component) ShowComponentProperties(component);
            else if (_session?.FindControl(name) is { } control) _surface.Select(control);
        };
        _surface.BoundsEdited += (control, _, bounds) => RunEditAsync(() => _session!.SetBoundsAsync(control, bounds));
        _surface.BoundsEdits += edits => RunEditAsync(() => _session!.SetBoundsBatchAsync(edits));
        _surface.DeleteRequested += control => RunEditAsync(() => DeleteSelectedControlsAsync(_surface.SelectedControls.ToArray()));
    }

    public static bool IsDesignerPath(string path) => path.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        _attached = true;
        AttachDesignerSession();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        _attached = false;
        ++_loadGeneration;
        TrackResourceBuffer(null);
        _retainedSelectionNames = _session is null ? []
            : _surface.SelectedControls.Select(_session.NameOf).ToArray();
        _surface.RootForm = null;
        base.OnDetachedFromVisualTree(args);
    }

    private void SessionChanged()
    {
        if (_session is null) return;
        var selected = _selectedName;
        _document.Text = _session.Document.Source;
        ReplaceSurfaceRoot();
        _tray.Show(_session.Document.Components);
        if (_session.FindComponent(selected) is { } component)
        {
            _tray.Select(selected);
            ShowComponentProperties(component);
        }
        else if (_editSelectionNames.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            var controls = _editSelectionNames.Select(_session.FindControl).OfType<Forms.Control>().ToArray();
            if (controls.Length > 0) _surface.SelectControls(controls);
            else _surface.Select(_session.Form);
        }
        else _surface.Select(_session.FindControl(selected) ?? _session.Form);
        ShowProblems();
    }

    private void ShowProperties(Forms.Control? control)
    {
        if (control is null && (_rebuildingSurface || !_attached))
        {
            return;
        }

        InvalidateMenuItemProperties();
        InvalidateToolStripItemProperties();
        if (_session is null || control is null)
        {
            _properties.Show(null);
            _events.Show(null);
            return;
        }
        _tray.Select(null);
        _layoutToolbar.SelectedCount = _surface.SelectedControls.Count;
        _selectedName = _session.NameOf(control);
        UpdateObjectSelector();
        if (_surface.SelectedControls.Count > 1)
        {
            ShowGroupProperties(_surface.SelectedControls.ToArray());
            return;
        }
        _properties.Show(control, name => _session.ExplicitValue(control, name),
            (name, value) => RunEditAsync(() => SetGridPropertyAsync(control, name, value)),
            collection => OpenCollectionEditor(control, collection),
            ReferenceEquals(control, _session.Form) ? null : _selectedName,
            (property, value) => RunEditAsync(() => _session.SetNestedPropertyAsync(control, property.ParentName!, property.Name, value)),
            property => OpenPropertyEditor(control, property),
            _session.ReferenceNames, name => _session.SourceValueState(control, name),
            name => _session.CanResetResourceProperty(control, name));
        var fieldName = _selectedName;
        _events.Show(control, name => _handlers.GetValueOrDefault(fieldName)?.GetValueOrDefault(name),
            (name, handler) => RunEditAsync(() => SetHandlerAsync(control, fieldName, name, handler)),
            name => (fieldName == "Me" ? _session.Document.ClassName : fieldName) + "_" + name);
    }

    private string CompanionPath => _document.FilePath[..^".Designer.vb".Length] + ".vb";

    // The item the toolbox reports, not its selection: Enter in the filter box
    // activates the first match while the list may still hold an older selection.
    private void AddTool(WinFormsToolboxItem? item)
    {
        if (_session is null || item is null) return;
        RunEditAsync(async () =>
        {
            var session = _session!;
            var originalNames = session.Document.DeclaredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (typeof(Forms.Control).IsAssignableFrom(item.ControlType) && item.ControlType != typeof(Forms.ContextMenuStrip))
            {
                var selected = _surface.SelectedControl;
                var parent = selected is Forms.Panel or Forms.GroupBox or Forms.TabPage or Forms.Form
                    ? selected : selected?.Parent ?? session.Form;
                await session.AddControlAsync(item.ControlType, session.NameOf(parent));
            }
            else await session.AddComponentAsync(item.ControlType);
            var added = session.Document.DeclaredNames.FirstOrDefault(name => !originalNames.Contains(name));
            if (added is not null && session.FindComponent(added) is { } component) ShowComponentProperties(component);
            else if (added is not null && session.FindControl(added) is { } control) _surface.Select(control);
        });
    }

    private async void RunEditAsync(Func<Task> edit)
    {
        if (_busy || _session is null) return;
        _editSelectionNames = _surface.SelectedControls.Select(_session.NameOf).ToArray();
        _busy = true;
        IsEnabled = false;
        _toolboxPanel.IsEnabled = false;
        _properties.IsEnabled = false;
        _events.IsEnabled = false;
        try { await edit(); }
        catch (Exception exception) { ShowError(exception); }
        finally
        {
            _busy = false;
            _editSelectionNames = [];
            IsEnabled = true;
            _toolboxPanel.IsEnabled = true;
            _properties.IsEnabled = true;
            _events.IsEnabled = true;
        }
    }

    private void ShowProblems()
    {
        var problems = (_session?.PreviewProblems ?? []).AsEnumerable();
        if (_resourceProblem is not null) problems = problems.Prepend(_resourceProblem);
        var details = string.Join("; ", problems);
        _status.Text = details.Length > 0 ? Localizer.Get(StringKeys.WinFormsDesignerPreviewProblems, details) : "";
    }
    private void ShowError(Exception exception) => _status.Text = Localizer.Get(StringKeys.WinFormsDesignerError, exception.GetBaseException().Message);
}
