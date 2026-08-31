using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

/// <summary>
/// The events of the selected control, and which of them are handled.
/// </summary>
/// <remarks>
/// Double-clicking a control gives it the one event it obviously means; this
/// is for the others. A button that should also respond to the keyboard, a
/// text box that should react when focus leaves it — without a list there is
/// no way to reach those from the designer at all, and the name of the event
/// has to be looked up before it can be typed by hand.
///
/// The ones already wired show the method they call, so the list doubles as
/// an answer to "what does this control already do".
/// </remarks>
public sealed class EventsPanel : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = Spacing.Hairline };
    private readonly TextBlock _heading;
    private readonly TextBlock _nothing;

    private DesignerSession? _session;

    /// <summary>Raised when an event should be wired to a handler.</summary>
    public event EventHandler<string>? HandlerRequested;

    public EventsPanel()
    {
        _heading = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight, Spacing.Normal, Spacing.Tight),
        };

        _nothing = new TextBlock
        {
            Text = Localizer.Get(StringKeys.EventsNothingSelected),
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(Spacing.Normal, Spacing.Normal),
            TextWrapping = TextWrapping.Wrap,
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel { Children = { _heading, _nothing, _rows } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>The events listed, for tests.</summary>
    internal IReadOnlyList<string> Events =>
        [.. _rows.Children.OfType<Control>().Select(row => (string)row.Tag!)];

    /// <summary>The handlers shown beside them, for tests.</summary>
    internal IReadOnlyDictionary<string, string> Handlers { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Shows the events of whatever the session has selected.</summary>
    public void Show(DesignerSession? session)
    {
        _session = session;

        _rows.Children.Clear();

        var handlers = new Dictionary<string, string>(StringComparer.Ordinal);
        Handlers = handlers;

        if (session?.Selection is not { } element)
        {
            _heading.Text = "";
            _nothing.IsVisible = true;
            return;
        }

        _nothing.IsVisible = false;

        var kind = element.Name.LocalName;

        _heading.Text = Basalt.Designer.Model.XamlDocument.GetName(element) is { Length: > 0 } name
            ? $"{name}  ({kind})"
            : kind;

        foreach (var each in EventHandlers.EventsFor(kind))
        {
            // What the XAML already says this event calls, if anything.
            var handler = element.Attribute(each)?.Value;

            if (handler is { Length: > 0 }) handlers[each] = handler;

            _rows.Children.Add(Row(each, handler));
        }
    }

    /// <summary>
    /// One event: its name, and the method it calls if it has one.
    /// </summary>
    /// <remarks>
    /// A double click writes the handler, the same gesture as on the surface:
    /// the list is a way to choose <em>which</em> event, not a different way
    /// of wiring one.
    /// </remarks>
    private Control Row(string name, string? handler)
    {
        var label = new TextBlock
        {
            Text = name,
            FontSize = 12,
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var method = new TextBlock
        {
            Text = handler ?? "",
            FontSize = 12,
            Opacity = handler is null ? 0.5 : 1,
            FontWeight = handler is null ? FontWeight.Normal : FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var row = new Border
        {
            Padding = new Thickness(Spacing.Normal, Spacing.Tight),
            Background = Brushes.Transparent,
            Tag = name,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = Spacing.Tight,
                Children = { label, method },
            },
        };

        row.DoubleTapped += (_, e) =>
        {
            HandlerRequested?.Invoke(this, name);
            e.Handled = true;
        };

        // The wired ones stand out, so the list answers "what does this
        // control already do" without being read line by line.
        if (handler is { Length: > 0 })
            row.Background = new SolidColorBrush(Color.FromArgb(24, 30, 144, 255));

        return row;
    }

    /// <summary>Asks for a handler, as double-clicking a row does. For tests.</summary>
    internal void RequestForTests(string eventName) =>
        HandlerRequested?.Invoke(this, eventName);
}
