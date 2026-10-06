using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>One row the "&lt;Type&gt; Tasks" flyout shows.</summary>
internal abstract record WinFormsSmartTagItem;

/// <summary>A one-click task, such as "Insert Standard Items" or the dock/undock toggle.
/// <paramref name="Enabled"/> false greys the row out (as Visual Studio does for "Insert
/// Standard Items" once the strip already has items) without removing it from the list.</summary>
internal sealed record WinFormsSmartTagCommand(string Text, Action Invoke, bool Enabled = true) : WinFormsSmartTagItem;

/// <summary>A task that picks one of a fixed set of values, such as Dock or Size Mode.</summary>
internal sealed record WinFormsSmartTagChoice(
    string Label, IReadOnlyList<string> Options, string Current, Action<string> Select) : WinFormsSmartTagItem;

/// <summary>
/// Visual Studio's square "smart tag" glyph a selected control shows at the top-right outside
/// corner of its selection border when it has designer tasks, and the "&lt;Type&gt; Tasks" flyout
/// it opens. <see cref="WinFormsDesignSurface"/> only hosts and positions this control; which
/// tasks a given selection offers, and what each one does to the document, is decided by
/// <c>WinFormsDesignerView.SmartTags.cs</c>, the same split <see cref="WinFormsToolStripDesignEditor"/>
/// and <see cref="WinFormsMenuDesignEditor"/> use for their own inline overlays.
/// </summary>
internal sealed class WinFormsSmartTag : Canvas
{
    internal const double GlyphSize = 11;

    private readonly Border _flyout = new()
    {
        Name = "WinFormsSmartTagFlyout",
        Background = Brushes.White,
        BorderBrush = Brushes.DodgerBlue,
        BorderThickness = new Thickness(1),
        IsVisible = false,
        MinWidth = 160,
    };

    private string? _key;
    private bool _open;

    /// <summary>Closes the flyout and forgets which selection it was built for, so a later
    /// <see cref="Show"/> for that same control starts collapsed again instead of resurrecting
    /// whatever open/closed state this control happened to be left in.</summary>
    internal void Close()
    {
        _key = null;
        _open = false;
        _flyout.IsVisible = false;
    }

    internal WinFormsSmartTag()
    {
        var glyph = new Border
        {
            Name = "WinFormsSmartTagGlyph",
            Width = GlyphSize,
            Height = GlyphSize,
            Background = Brushes.Orange,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "▸", // VS draws a right-pointing arrow on its smart tag glyph.
                FontSize = 7,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var glyphButton = new Button
        {
            Name = "WinFormsSmartTagGlyphButton",
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = glyph,
        };
        glyphButton.Click += (_, _) =>
        {
            _open = !_open;
            _flyout.IsVisible = _open;
        };

        Width = GlyphSize;
        Height = GlyphSize;
        Children.Add(glyphButton);
        Children.Add(_flyout);
        SetTop(_flyout, GlyphSize + 2);
    }

    /// <summary>Rebuilds the flyout for the given selection. <paramref name="key"/> identifies the
    /// selected control (its field name): switching to a different control always closes a flyout
    /// left open from the previous one, the same way the ToolStrip/StatusStrip "Type Here" editor
    /// resets its own open drop-down when the strip it is showing changes.</summary>
    internal void Show(string key, string typeName, IReadOnlyList<WinFormsSmartTagItem> items)
    {
        if (_key != key)
        {
            _open = false;
        }
        _key = key;

        var body = new StackPanel { Spacing = 4, Margin = new Thickness(6) };
        body.Children.Add(new TextBlock
        {
            Name = "WinFormsSmartTagHeader",
            Text = Localizer.Get(StringKeys.WinFormsSmartTagTasksHeader, typeName),
            FontWeight = FontWeight.Bold,
        });

        foreach (var item in items)
        {
            body.Children.Add(item switch
            {
                WinFormsSmartTagCommand command => BuildCommandRow(command),
                WinFormsSmartTagChoice choice => BuildChoiceRow(choice),
                _ => throw new NotSupportedException(),
            });
        }

        _flyout.Child = body;
        _flyout.IsVisible = _open;
    }

    private Button BuildCommandRow(WinFormsSmartTagCommand command)
    {
        var button = new Button
        {
            Name = "WinFormsSmartTagAction_" + Sanitize(command.Text),
            Content = command.Text,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = command.Enabled,
        };
        button.Click += (_, _) =>
        {
            // Closed before invoking: the action may select a different control, or rebuild the
            // preview entirely, by which point this flyout's own open/closed state is moot.
            _open = false;
            _flyout.IsVisible = false;
            command.Invoke();
        };
        return button;
    }

    private StackPanel BuildChoiceRow(WinFormsSmartTagChoice choice)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(new TextBlock { Text = choice.Label, VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox
        {
            Name = "WinFormsSmartTagCombo_" + Sanitize(choice.Label),
            ItemsSource = choice.Options,
            SelectedItem = choice.Current,
            MinWidth = 90,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value && value != choice.Current)
            {
                _open = false;
                _flyout.IsVisible = false;
                choice.Select(value);
            }
        };
        row.Children.Add(combo);
        return row;
    }

    private static string Sanitize(string text) => new([.. text.Where(char.IsLetterOrDigit)]);
}
