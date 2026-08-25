using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Windows.Input;

namespace Basalt.Shell.Controls;

/// <summary>One button on the toolbar.</summary>
public sealed record ToolbarAction(
    IconKind Icon,
    string Tooltip,
    Action Invoke)
{
    /// <summary>A command instead of an action, where one already exists.</summary>
    public ICommand? Command { get; init; }

    /// <summary>Whether a separator is drawn before this button.</summary>
    public bool StartsGroup { get; init; }

    /// <summary>The registry command this stands for, where there is one.</summary>
    public string? CommandId { get; init; }

    /// <summary>Whether the button can be pressed right now; null means always.</summary>
    public Func<bool>? IsAvailable { get; init; }
}

/// <summary>A control that sits on the toolbar without being a button.</summary>
public sealed record ToolbarChooser(
    string Tooltip,
    IReadOnlyList<string> Options,
    string Selected,
    Action<string> Chosen)
{
    public bool StartsGroup { get; init; }

    public double Width { get; init; } = 110;
}

/// <summary>
/// The row of buttons for the actions used most often.
///
/// Icons alone, with the name in a tooltip: a toolbar exists to be reached
/// without reading, and labels would make it three times as tall for actions
/// the menus already spell out.
/// </summary>
public sealed class IdeToolbar : UserControl
{
    private readonly StackPanel _row;

    public IdeToolbar()
    {
        _row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(6, 4)
        };

        Content = _row;
    }

    internal int ButtonCount => _row.Children.OfType<Button>().Count();

    private readonly List<(ToolbarAction Action, Button Button)> _live = [];

    public void Show(IReadOnlyList<ToolbarAction> actions)
    {
        _row.Children.Clear();
        _live.Clear();

        foreach (var action in actions)
        {
            if (action.StartsGroup && _row.Children.Count > 0) _row.Children.Add(Separator());

            var button = Build(action);

            _row.Children.Add(button);
            _live.Add((action, button));
        }

        RefreshAvailability();
    }

    /// <summary>
    /// Adds something that is not a button, such as a chooser.
    ///
    /// The build configuration and the startup project belong on the toolbar
    /// but are chosen from a list, not pressed.
    /// </summary>
    public void Add(ToolbarChooser chooser)
    {
        if (chooser.StartsGroup && _row.Children.Count > 0) _row.Children.Add(Separator());

        var combo = new ComboBox
        {
            ItemsSource = chooser.Options,
            SelectedItem = chooser.Selected,
            Width = chooser.Width,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };

        ToolTip.SetTip(combo, chooser.Tooltip);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string picked) chooser.Chosen(picked);
        };

        _row.Children.Add(combo);
    }

    /// <summary>
    /// Greys out the buttons that cannot be pressed.
    ///
    /// Called when the IDE's state changes, so that Stop is dim while nothing
    /// is running rather than doing nothing when pressed.
    /// </summary>
    public void RefreshAvailability()
    {
        foreach (var (action, button) in _live)
        {
            if (action.IsAvailable is { } available) button.IsEnabled = available();
        }
    }

    internal IReadOnlyList<string> Tooltips =>
        [.. _live.Select(l => l.Action.Tooltip)];

    internal bool IsButtonEnabled(string tooltip) =>
        _live.FirstOrDefault(l => l.Action.Tooltip == tooltip).Button?.IsEnabled ?? false;

    internal int ChooserCount => _row.Children.OfType<ComboBox>().Count();

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 4),
        Background = new SolidColorBrush(Colors.Gray, 0.3)
    };

    private static Button Build(ToolbarAction action)
    {
        var button = new Button
        {
            Content = new IconView { Kind = action.Icon, IconSize = 16 },
            Padding = new Thickness(6, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Command = action.Command
        };

        ToolTip.SetTip(button, action.Tooltip);

        // A command, where given, decides on its own whether it can run; the
        // plain action is for what the window does directly.
        if (action.Command is null) button.Click += (_, _) => action.Invoke();

        return button;
    }
}

/// <summary>
/// The status bar, showing where the caret is and how the file is stored.
///
/// The encoding and line ending matter in a cross-platform IDE more than they
/// do on one system: a file written on Windows and edited on macOS is the
/// normal case here, not an unusual one.
/// </summary>
public sealed class IdeStatusBar : UserControl
{
    private readonly TextBlock _message;
    private readonly TextBlock _position;
    private readonly TextBlock _encoding;
    private readonly TextBlock _lineEnding;
    private readonly TextBlock _language;

    public IdeStatusBar()
    {
        _message = Cell("");
        _position = Cell("");
        _encoding = Cell("");
        _lineEnding = Cell("");
        _language = Cell("");

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        right.Children.Add(_position);
        right.Children.Add(_lineEnding);
        right.Children.Add(_encoding);
        right.Children.Add(_language);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        Grid.SetColumn(_message, 0);
        Grid.SetColumn(right, 1);

        layout.Children.Add(_message);
        layout.Children.Add(right);

        Content = new Border { Padding = new Thickness(12, 4), Child = layout };
    }

    internal string Message => _message.Text ?? "";
    internal string Position => _position.Text ?? "";
    internal string Encoding => _encoding.Text ?? "";
    internal string LineEnding => _lineEnding.Text ?? "";
    internal string Language => _language.Text ?? "";

    private static TextBlock Cell(string text) => new()
    {
        Text = text,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center
    };

    public void ShowMessage(string message) => _message.Text = message;

    /// <summary>Shows where the caret is, counting from one as editors do.</summary>
    public void ShowPosition(int line, int column, int? selectionLength = null)
    {
        _position.Text = selectionLength is > 0
            ? $"Ln {line}, Col {column} ({selectionLength} selected)"
            : $"Ln {line}, Col {column}";
    }

    public void ShowFile(string? encoding, string? lineEnding, string? language)
    {
        _encoding.Text = encoding ?? "";
        _lineEnding.Text = lineEnding ?? "";
        _language.Text = language ?? "";
    }

    public void Clear()
    {
        _position.Text = "";
        _encoding.Text = "";
        _lineEnding.Text = "";
        _language.Text = "";
    }

    /// <summary>
    /// Names the line ending a document uses.
    ///
    /// A file with both is reported as mixed rather than by whichever came
    /// first, since that is a fact worth seeing before saving over it.
    /// </summary>
    public static string DescribeLineEnding(string text)
    {
        var windows = 0;
        var unix = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;

            if (i > 0 && text[i - 1] == '\r') windows++;
            else unix++;
        }

        if (windows > 0 && unix > 0) return "Mixed";
        if (windows > 0) return "CRLF";

        return unix > 0 ? "LF" : "LF";
    }
}
