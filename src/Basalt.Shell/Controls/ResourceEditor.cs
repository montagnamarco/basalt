using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

/// <summary>Edits a fixed set of textual resource types without loading serialized objects.</summary>
internal sealed class ResourceEditor : UserControl
{
    private sealed record ResourceKind(Type ValueType, string Example)
    {
        public override string ToString() => ValueType.FullName ?? ValueType.Name;
    }

    private static readonly ResourceKind[] Kinds =
    [
        new(typeof(string), "example"),
        new(typeof(int), "1"),
        new(typeof(bool), "True"),
        new(typeof(System.Drawing.Color), "Red"),
        new(typeof(System.Drawing.Point), "0, 0"),
        new(typeof(System.Drawing.Size), "1, 1"),
    ];

    private readonly EditorDocumentViewModel _document;
    private readonly ListBox _entries = new() { Name = "ResourceEntries" };
    private readonly TextBox _name = new() { Name = "ResourceName" };
    private readonly ComboBox _type = new() { Name = "ResourceType", ItemsSource = Kinds };
    private readonly TextBlock _rawType = new()
    {
        Name = "ResourceRawType",
        IsVisible = false,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private readonly TextBox _value = new() { Name = "ResourceValue", AcceptsReturn = true, MinHeight = 90 };
    private readonly TextBox _comment = new() { Name = "ResourceComment", AcceptsReturn = true, MinHeight = 60 };
    private readonly TextBlock _status = new() { Name = "ResourceStatus", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string _historySource;
    private Control _resourceLayout = null!;
    private Control? _sourceLayout;
    private volatile bool _writing;
    private volatile bool _isAttached;
    private bool _refreshing;
    private bool _adding;
    private bool _validDocument;

    // What the name, value and comment fields held right after they were
    // last loaded (by ShowSelection, ClearDetails or Add) rather than
    // whatever the user has since typed. Ctrl+Z/Y is only the resource's own
    // undo when every field still matches its baseline: otherwise there is
    // an uncommitted draft, and the keystroke belongs to the focused text
    // box so it can undo its own typing instead.
    private string _nameBaseline = "";
    private string _valueBaseline = "";
    private string _commentBaseline = "";

    internal ResourceEditor(EditorDocumentViewModel document, Func<Control> createSourceEditor)
    {
        _document = document;
        _historySource = document.Text;
        _entries.ItemTemplate = new FuncDataTemplate<WinFormsResourceEntry>(
            (entry, _) => EntryRow(entry), supportsRecycling: false);
        _entries.ContainerPrepared += (_, args) =>
        {
            if (args.Container is ListBoxItem row)
            {
                row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            }
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        toolbar.Children.Add(Button("ResourceAdd", StringKeys.ResourceEditorAdd, Add));
        toolbar.Children.Add(Button("ResourceApply", StringKeys.ResourceEditorApply, Apply));
        toolbar.Children.Add(Button("ResourceDelete", StringKeys.DesignerDelete, Remove));
        toolbar.Children.Add(Button("ResourceUndo", StringKeys.MenuEditUndo, () => Restore(_undo, _redo)));
        toolbar.Children.Add(Button("ResourceRedo", StringKeys.MenuEditRedo, () => Restore(_redo, _undo)));
        toolbar.Children.Add(Button("ResourceXml", StringKeys.ResourceEditorXml, () => ShowSource(createSourceEditor)));

        var detail = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
        detail.Children.Add(Label(StringKeys.ResourceEditorName));
        detail.Children.Add(_name);
        detail.Children.Add(Label(StringKeys.ResourceEditorType));
        detail.Children.Add(_type);
        detail.Children.Add(_rawType);
        detail.Children.Add(Label(StringKeys.ResourceEditorValue));
        detail.Children.Add(_value);
        detail.Children.Add(Label(StringKeys.ResourceEditorComment));
        detail.Children.Add(_comment);
        detail.Children.Add(toolbar);
        detail.Children.Add(_status);

        var listPane = new DockPanel { Margin = new Thickness(8) };
        var headings = EntryCells(Localizer.Get(StringKeys.ResourceEditorName),
            Localizer.Get(StringKeys.ResourceEditorType), Localizer.Get(StringKeys.ResourceEditorComment));
        DockPanel.SetDock(headings, Avalonia.Controls.Dock.Top);
        listPane.Children.Add(headings);
        listPane.Children.Add(_entries);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*") };
        layout.Children.Add(listPane);
        Grid.SetColumn(detail, 1);
        layout.Children.Add(detail);
        _resourceLayout = layout;
        Content = layout;

        _entries.SelectionChanged += (_, _) =>
        {
            if (!_refreshing) ShowSelection();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            _document.PropertyChanged += DocumentChanged;
            SynchronizeHistory();
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            _document.PropertyChanged -= DocumentChanged;
        };
        AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        Refresh();
    }

    private void ShowSource(Func<Control> createSourceEditor)
    {
        if (_sourceLayout is null)
        {
            var back = Button("ResourceBack", StringKeys.ResourceEditorBack, () => Content = _resourceLayout);
            var source = new DockPanel();
            DockPanel.SetDock(back, Avalonia.Controls.Dock.Top);
            source.Children.Add(back);
            source.Children.Add(createSourceEditor());
            _sourceLayout = source;
        }

        Content = _sourceLayout;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs args)
    {
        if (!ReferenceEquals(Content, _resourceLayout) || args.Handled)
        {
            return;
        }

        var command = args.KeyModifiers.HasFlag(KeyModifiers.Control)
            || args.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!command || args.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        if ((args.Key == Key.Z || args.Key == Key.Y) && HasUncommittedDraft())
        {
            // A field has text Apply has not turned into a resource change
            // yet. This handler runs on the Tunnel route before the event
            // reaches the focused text box, so leaving it unhandled here is
            // what lets that box undo its own typing instead of the editor
            // reverting the last applied resource change underneath it.
            return;
        }

        if (args.Key == Key.Z && !args.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Restore(_undo, _redo);
        }
        else if (args.Key == Key.Y || args.Key == Key.Z && args.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Restore(_redo, _undo);
        }
        else
        {
            return;
        }

        args.Handled = true;
    }

    private static TextBlock Label(string key) => new() { Text = Localizer.Get(key) };

    private static Button Button(string name, string key, Action action)
    {
        var button = new Button { Name = name, Content = Localizer.Get(key) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Control EntryRow(WinFormsResourceEntry? entry) =>
        entry is null ? new TextBlock() : EntryCells(entry.Name, DisplayType(entry), entry.Comment ?? "");

    private static Grid EntryCells(string name, string type, string comment)
    {
        var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,2*,3*"), MinHeight = 22 };
        AddCell(cells, name, 0);
        AddCell(cells, type, 1);
        AddCell(cells, comment, 2);
        return cells;
    }

    private static void AddCell(Grid cells, string text, int column)
    {
        var cell = new TextBlock
        {
            Text = text,
            Margin = new Thickness(4, 2),
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(cell, column);
        cells.Children.Add(cell);
    }

    private static string DisplayType(WinFormsResourceEntry entry) =>
        KnownKind(entry)?.ValueType.FullName ?? entry.TypeName ?? entry.MimeType ?? typeof(string).FullName!;

    private static ResourceKind? KnownKind(WinFormsResourceEntry entry) => Kinds.FirstOrDefault(kind =>
        WinFormsDesignerResources.TryRead(entry with { Value = kind.Example }, kind.ValueType, out _));

    private void DocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(EditorDocumentViewModel.Text) || _writing) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var observedSource = _document.Text;
            Dispatcher.UIThread.Post(() =>
            {
                if (_isAttached && _document.Text == observedSource) RefreshAfterExternalChange();
            });
            return;
        }

        RefreshAfterExternalChange();
    }

    private void RefreshAfterExternalChange()
    {
        SynchronizeHistory();
        Refresh();
    }

    private bool SynchronizeHistory()
    {
        var currentSource = _document.Text;
        if (currentSource == _historySource) return false;

        _undo.Clear();
        _redo.Clear();
        _historySource = currentSource;
        return true;
    }

    private void Refresh()
    {
        var selectedName = (_entries.SelectedItem as WinFormsResourceEntry)?.Name;
        try
        {
            var resource = new WinFormsResourceDocument(_document.Text);
            _validDocument = true;
            _refreshing = true;
            try
            {
                _entries.ItemsSource = resource.Entries;
                _entries.SelectedItem = resource.Entries.FirstOrDefault(entry => entry.Name == selectedName);
            }
            finally
            {
                _refreshing = false;
            }
            _adding = false;
            ShowSelection();
        }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException)
        {
            _validDocument = false;
            _adding = false;
            _refreshing = true;
            try
            {
                _entries.SelectedItem = null;
                _entries.ItemsSource = Array.Empty<WinFormsResourceEntry>();
            }
            finally
            {
                _refreshing = false;
            }
            ClearDetails();
            _status.Text = Localizer.Get(StringKeys.ResourceEditorInvalid, exception.Message);
        }
    }

    private void ShowSelection()
    {
        if (_entries.SelectedItem is not WinFormsResourceEntry entry)
        {
            if (!_adding) ClearDetails();
            return;
        }

        _adding = false;
        _name.Text = entry.Name;
        _name.IsReadOnly = true;
        _value.Text = entry.Value;
        _comment.Text = entry.Comment ?? "";
        _comment.IsReadOnly = false;
        var kind = KnownKind(entry);
        _type.SelectedItem = kind;
        _type.IsVisible = kind is not null;
        _type.IsEnabled = kind is not null;
        _rawType.Text = kind is null ? entry.TypeName ?? entry.MimeType ?? "" : "";
        _rawType.IsVisible = kind is null;
        _value.IsReadOnly = kind is null;
        _status.Text = kind is null ? Localizer.Get(StringKeys.ResourceEditorReadOnly) : "";
        SetBaseline();
    }

    private void ClearDetails()
    {
        _name.Text = "";
        _name.IsReadOnly = true;
        _type.SelectedItem = null;
        _type.IsVisible = true;
        _type.IsEnabled = false;
        _rawType.Text = "";
        _rawType.IsVisible = false;
        _value.Text = "";
        _value.IsReadOnly = true;
        _comment.Text = "";
        _comment.IsReadOnly = true;
        _status.Text = "";
        SetBaseline();
    }

    private void Add()
    {
        if (!_validDocument) return;
        _entries.SelectedItem = null;
        _adding = true;
        _name.Text = "";
        _name.IsReadOnly = false;
        _type.SelectedItem = Kinds[0];
        _type.IsVisible = true;
        _type.IsEnabled = true;
        _rawType.IsVisible = false;
        _value.Text = "";
        _value.IsReadOnly = false;
        _comment.Text = "";
        _comment.IsReadOnly = false;
        _status.Text = "";
        SetBaseline();
        _name.Focus();
    }

    /// <summary>Records the fields' current text as their baseline, for <see cref="HasUncommittedDraft"/>.</summary>
    private void SetBaseline()
    {
        _nameBaseline = _name.Text ?? "";
        _valueBaseline = _value.Text ?? "";
        _commentBaseline = _comment.Text ?? "";
    }

    /// <summary>Whether a field holds text the user typed that Apply has not yet turned into a resource change.</summary>
    private bool HasUncommittedDraft() =>
        _name.Text != _nameBaseline || _value.Text != _valueBaseline || _comment.Text != _commentBaseline;

    private void Apply()
    {
        if (!_validDocument || string.IsNullOrWhiteSpace(_name.Text)) return;
        try
        {
            var resource = new WinFormsResourceDocument(_document.Text);
            var selected = _entries.SelectedItem as WinFormsResourceEntry;
            var name = selected?.Name ?? _name.Text;
            if (selected is null && resource.TryGet(name) is not null)
                throw new FormatException(Localizer.Get(StringKeys.ResourceEditorDuplicateName));

            var comment = string.IsNullOrEmpty(_comment.Text) ? null : _comment.Text;
            if (selected is not null && KnownKind(selected) is null)
            {
                Commit(resource.SetValue(name, selected.Value, selected.TypeName,
                    selected.MimeType, comment));
            }
            else if (_type.SelectedItem is ResourceKind kind)
            {
                var draft = new WinFormsResourceEntry(name, CanonicalType(kind), null,
                    _value.Text ?? "", comment);
                if (!WinFormsDesignerResources.TryRead(draft, kind.ValueType, out var value)
                    || !WinFormsDesignerResources.TryWrite(value, kind.ValueType, out var text, out var typeName))
                    throw new FormatException(Localizer.Get(StringKeys.ResourceEditorInvalidValue));
                Commit(resource.SetValue(name, text, typeName, null, comment));
            }
            else
            {
                throw new FormatException(Localizer.Get(StringKeys.ResourceEditorUnsupportedScalar));
            }

            SelectEntry(name);
        }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException
            or NotSupportedException or ArgumentException)
        {
            _status.Text = Localizer.Get(StringKeys.ResourceEditorEditFailed, exception.Message);
        }
    }

    private static string? CanonicalType(ResourceKind kind)
    {
        if (kind.ValueType == typeof(string)) return null;
        object sample = kind.ValueType == typeof(int) ? 0
            : kind.ValueType == typeof(bool) ? false
            : kind.ValueType == typeof(System.Drawing.Color) ? System.Drawing.Color.Red
            : kind.ValueType == typeof(System.Drawing.Point) ? System.Drawing.Point.Empty
            : System.Drawing.Size.Empty;
        WinFormsDesignerResources.TryWrite(sample, kind.ValueType, out _, out var typeName);
        return typeName;
    }

    private void SelectEntry(string name)
    {
        _entries.SelectedItem = (_entries.ItemsSource ?? Array.Empty<object>())
            .OfType<WinFormsResourceEntry>().FirstOrDefault(entry => entry.Name == name);
    }

    private void Remove()
    {
        if (!_validDocument || _entries.SelectedItem is not WinFormsResourceEntry entry) return;
        Commit(new WinFormsResourceDocument(_document.Text).Remove(entry.Name));
    }

    private void Commit(string source)
    {
        if (source == _document.Text) return;
        SynchronizeHistory();
        _undo.Push(_document.Text);
        _redo.Clear();
        Write(source);
    }

    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (SynchronizeHistory())
        {
            Refresh();
            return;
        }

        if (!from.TryPop(out var source)) return;
        to.Push(_document.Text);
        Write(source);
    }

    private void Write(string source)
    {
        _writing = true;
        try
        {
            _document.Text = source;
            _historySource = source;
        }
        finally
        {
            _writing = false;
        }
        Refresh();
    }
}
