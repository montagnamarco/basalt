using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Shell.Controls;

/// <summary>
/// The designer's properties, shown in a <see cref="PropertyGrid"/>.
/// </summary>
/// <remarks>
/// Only the part that knows about the designer: resolving a XAML element name
/// to a type, reading its properties, and writing changes back to the session.
/// The grid itself knows nothing of any of that, so it can show a Visual Basic
/// form's properties one day without being untangled from XAML first.
///
/// Also hosts the events of the selected element, the way Visual Studio's own
/// Properties window does: a small Properties/Events toggle swaps the body in
/// place, so there is no separate Events tool to dock.
/// </remarks>
public sealed class PropertyPanel : UserControl
{
    private readonly PropertyGrid _grid = new();
    private readonly GridTrackEditor _tracks = new();
    private readonly TextBlock _element;
    private readonly ToggleButton _propertiesButton;
    private readonly ToggleButton _eventsButton;
    private readonly ContentControl _body;
    private Control? _eventsContent;
    private bool _showingEvents;
    private DesignerSession? _session;

    /// <summary>Raised when the grid's rows or columns changed.</summary>
    public event EventHandler? LayoutChanged;

    public PropertyPanel()
    {
        _element = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight, Spacing.Normal, 0),
        };

        _grid.PropertyEdited += (name, value) => _session?.SetProperty(name, value);

        // The rows and columns sit above the properties: they are the shape
        // of the form, and the properties are what fills it.
        _tracks.Changed += (_, _) => LayoutChanged?.Invoke(this, EventArgs.Empty);

        _propertiesButton = new ToggleButton
        {
            Name = "PropertyPanelProperties",
            Content = Localizer.Get("WinFormsDesigner_Properties"),
            IsChecked = true,
        };
        _eventsButton = new ToggleButton
        {
            Name = "PropertyPanelEvents",
            Content = Localizer.Get("WinFormsDesigner_Events"),
        };
        _propertiesButton.Click += (_, _) => ShowEvents(false);
        _eventsButton.Click += (_, _) => ShowEvents(true);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Children = { _propertiesButton, _eventsButton },
        };

        _body = new ContentControl { Content = _grid };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            Children = { _element, toolbar, _tracks, _body },
        };

        Grid.SetRow(toolbar, 1);
        Grid.SetRow(_tracks, 2);
        Grid.SetRow(_body, 3);

        Content = layout;
    }

    /// <summary>The grid behind the panel, for tests.</summary>
    internal PropertyGrid Properties => _grid;

    /// <summary>The rows and columns editor, for tests.</summary>
    internal GridTrackEditor Tracks => _tracks;

    /// <summary>
    /// The events panel shown when the toolbar's Events button is pressed, in
    /// place of the properties grid — the same control whether or not it is
    /// currently on screen.
    /// </summary>
    public Control? EventsContent
    {
        get => _eventsContent;
        set
        {
            _eventsContent = value;
            if (_showingEvents) _body.Content = value;
        }
    }

    /// <summary>Whether the Events view is the one currently shown, for tests.</summary>
    internal bool ShowingEvents => _showingEvents;

    /// <summary>
    /// Switches between the properties grid and the events list. Sticky
    /// across selection changes, the way Visual Studio's own toggle is: the
    /// user picked a view, not a one-off look at a single control.
    /// </summary>
    private void ShowEvents(bool show)
    {
        if (_showingEvents == show) return;

        _showingEvents = show;
        _propertiesButton.IsChecked = !show;
        _eventsButton.IsChecked = show;
        _tracks.IsVisible = !show;
        _body.Content = show ? _eventsContent : _grid;
    }

    /// <summary>Shows the properties of the element selected in the given session.</summary>
    public void Show(DesignerSession? session)
    {
        _session = session;

        _tracks.Show(session);

        if (session?.Selection is not { } selection)
        {
            _element.Text = "";
            _grid.Properties = [];
            return;
        }

        var elementName = selection.Name.LocalName;
        _element.Text = elementName;

        var type = ResolveControlType(elementName);

        if (type is null)
        {
            // Said rather than shown as an empty list: a custom control whose
            // assembly is not loaded looks exactly like a control with no
            // properties, and the two want different things done about them.
            _element.Text = $"{elementName} — {Localizer.Get(StringKeys.PropertiesUnknownType)}";
            _grid.Properties = [];
            return;
        }

        // Content is worth a text editor only when it is already a plain
        // attribute: an element with child elements of its own - a Window
        // full of a Grid, say - keeps its Content hidden, because a caption
        // typed over a nested layout would collide with it rather than
        // replace it.
        // Text written between the tags (<Button>Go</Button>) or a
        // <Button.Content> element is Content too: writing a Content
        // attribute beside it would set it twice.
        var contentIsText = !XamlDocument.ControlChildren(selection).Any()
            && !selection.Nodes().OfType<System.Xml.Linq.XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            && !selection.Elements().Any(child => child.Name.LocalName.EndsWith(".Content", StringComparison.Ordinal));

        _grid.Properties = PropertyInspector.Describe(
            type, name => selection.Attribute(name)?.Value, contentIsText);
    }

    /// <summary>
    /// Resolves the Avalonia type from a XAML element name. The loaded
    /// assemblies are queried because controls live in different assemblies
    /// (Avalonia.Controls, Avalonia.Controls.DataGrid and others).
    /// </summary>
    private static Type? ResolveControlType(string elementName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("Avalonia", StringComparison.Ordinal) == true)
            .SelectMany(SafeGetTypes)
            .FirstOrDefault(t => t.Name == elementName && typeof(Control).IsAssignableFrom(t));

    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            // An assembly with missing dependencies still exposes the types that loaded.
            return ex.Types.OfType<Type>();
        }
    }
}
