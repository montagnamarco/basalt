using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

/// <summary>
/// The designer's properties, shown in a <see cref="PropertyGrid"/>.
/// </summary>
/// <remarks>
/// Only the part that knows about the designer: resolving a XAML element name
/// to a type, reading its properties, and writing changes back to the session.
/// The grid itself knows nothing of any of that, so it can show a Visual Basic
/// form's properties one day without being untangled from XAML first.
/// </remarks>
public sealed class PropertyPanel : UserControl
{
    private readonly PropertyGrid _grid = new();
    private readonly TextBlock _element;
    private DesignerSession? _session;

    public PropertyPanel()
    {
        _element = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight, Spacing.Normal, 0),
        };

        _grid.PropertyEdited += (name, value) => _session?.SetProperty(name, value);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { _element, _grid },
        };

        Grid.SetRow(_grid, 1);

        Content = layout;
    }

    /// <summary>The grid behind the panel, for tests.</summary>
    internal PropertyGrid Properties => _grid;

    /// <summary>Shows the properties of the element selected in the given session.</summary>
    public void Show(DesignerSession? session)
    {
        _session = session;

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

        _grid.Properties =
            PropertyInspector.Describe(type, name => selection.Attribute(name)?.Value);
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
