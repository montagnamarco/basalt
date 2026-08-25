using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Commands;

namespace Basalt.Shell;

/// <summary>
/// Finds and runs any command by name.
///
/// One place that reaches everything the IDE can do, which matters most for
/// the commands that have no shortcut and sit three menus deep.
/// </summary>
public sealed class CommandPalette : Window
{
    private readonly CommandRegistry _registry;
    private readonly TextBox _query;
    private readonly ListBox _matches;

    /// <summary>The command the user chose, or null if they closed the window.</summary>
    public IdeCommand? Chosen { get; private set; }

    public CommandPalette() : this(IdeCommands.CreateRegistry()) { }

    public CommandPalette(CommandRegistry registry)
    {
        _registry = registry;

        Title = "Command Palette";
        Width = 620;
        Height = 420;

        // A floor, so resizing cannot hide what matters.
        MinWidth = 420; MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        // No title bar: a palette is a momentary thing, and chrome around it
        // makes it look like a window to be arranged.
        WindowDecorations = WindowDecorations.BorderOnly;

        _query = new TextBox
        {
            PlaceholderText = "Type a command…",
            Margin = new Thickness(12, 12, 12, 8),
            FontSize = 14
        };

        _matches = new ListBox
        {
            Margin = new Thickness(8, 0, 8, 8),
            ItemTemplate = new FuncDataTemplate<IdeCommand>((command, _) => BuildRow(command))
        };

        _query.KeyDown += OnQueryKey;
        _query.TextChanged += (_, _) => Refresh();

        _matches.DoubleTapped += (_, _) => Accept();

        var layout = new DockPanel();

        DockPanel.SetDock(_query, Avalonia.Controls.Dock.Top);

        layout.Children.Add(_query);
        layout.Children.Add(_matches);

        Content = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.4),
            Child = layout
        };

        Refresh();

        Opened += (_, _) => _query.Focus();
    }

    internal string QueryText
    {
        get => _query.Text ?? "";
        set
        {
            _query.Text = value;
            Refresh();
        }
    }

    internal IReadOnlyList<IdeCommand> Matches =>
        (_matches.ItemsSource as IEnumerable<IdeCommand>)?.ToList() ?? [];

    internal int SelectedIndex
    {
        get => _matches.SelectedIndex;
        set => _matches.SelectedIndex = value;
    }

    internal void AcceptForTests() => Accept();

    /// <summary>
    /// The search box, so a test can raise a real key event on it.
    ///
    /// Raising the event is the point: a handler nothing raises is a handler
    /// nobody has checked.
    /// </summary>
    internal TextBox QueryBoxForTests => _query;

    /// <summary>
    /// Moves through the list from the box.
    ///
    /// The arrows have to work while typing: taking a hand off to reach the
    /// list would defeat the point of a palette.
    /// </summary>
    private void OnQueryKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Accept();
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        var count = Matches.Count;

        if (count == 0) return;

        // Wraps around, so holding Down does not stick at the bottom.
        _matches.SelectedIndex = (_matches.SelectedIndex + delta + count) % count;
        _matches.ScrollIntoView(_matches.SelectedIndex);
    }

    private void Accept()
    {
        if (_matches.SelectedItem is not IdeCommand command) return;

        Chosen = command;
        Close();
    }

    private void Refresh()
    {
        var found = _registry.Search(_query.Text ?? "");

        _matches.ItemsSource = found;
        _matches.SelectedIndex = found.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// One row: what the command is, where it lives, and its shortcut.
    ///
    /// The shortcut is shown because a palette is where people learn the keys
    /// for the things they reach for often.
    /// </summary>
    private Control BuildRow(IdeCommand? command)
    {
        if (command is null) return new TextBlock();

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var left = new StackPanel { Spacing = 1, Margin = new Thickness(4, 4) };

        left.Children.Add(new TextBlock { Text = command.Title, FontSize = 13 });

        left.Children.Add(new TextBlock
        {
            Text = command.Description is { Length: > 0 } description
                ? $"{command.Category} — {description}"
                : command.Category.ToString(),
            FontSize = 11,
            Opacity = 0.6
        });

        var gesture = new TextBlock
        {
            Text = _registry.GestureFor(command.Id) ?? "",
            FontSize = 11,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
            FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace")
        };

        Grid.SetColumn(left, 0);
        Grid.SetColumn(gesture, 1);

        layout.Children.Add(left);
        layout.Children.Add(gesture);

        return layout;
    }
}
