using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>
/// A .axaml shown either as a drawing or as its markup, with a way to change
/// which.
///
/// The two used to be separate documents, chosen when the file was opened and
/// unchangeable afterwards: seeing the markup of a window meant closing it and
/// opening it again from a different menu entry. They are one file, so they
/// belong in one tab.
/// </summary>
public sealed class DesignerHost : UserControl
{
    private readonly ContentControl _host = new();
    private readonly Func<Control> _designer;
    private readonly Func<Control> _text;

    private readonly RadioButton _designerButton;
    private readonly RadioButton _textButton;

    private Control? _designerView;
    private Control? _textView;

    public DesignerHost(
        EditorDocumentViewModel document, Func<Control> designer, Func<Control> text,
        bool startInDesigner = true)
    {
        Document = document;
        _designer = designer;
        _text = text;

        // Radio buttons for the behaviour — one of two, mutually exclusive —
        // but styled as flat tabs: the native bullet beside an icon and a
        // word reads as a form to fill in rather than a view to switch to.
        _designerButton = Choice("Designer", IconKind.DesignerFile);
        _textButton = Choice("XAML", IconKind.XmlFile);

        _designerButton.IsCheckedChanged += (_, _) =>
        {
            if (_designerButton.IsChecked == true) ShowDesigner();
        };

        _textButton.IsCheckedChanged += (_, _) =>
        {
            if (_textButton.IsChecked == true) ShowText();
        };

        var bar = new Border
        {
            Padding = new Thickness(8, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { _designerButton, _textButton }
            }
        };

        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Top);

        Content = new DockPanel { Children = { bar, _host } };

        if (startInDesigner) _designerButton.IsChecked = true;
        else _textButton.IsChecked = true;
    }

    public EditorDocumentViewModel Document { get; }

    /// <summary>Which half is on screen, for the tests and the shell.</summary>
    public bool ShowingDesigner => _designerButton.IsChecked == true;

    /// <summary>The text editor, once it has been asked for.</summary>
    public Control? TextView => _textView;

    /// <summary>Puts the drawing on screen.</summary>
    public void ShowDesigner()
    {
        // Built on first use and then kept: rebuilding would throw away the
        // selection, and for the editor the caret and the undo history.
        _designerView ??= _designer();
        _host.Content = _designerView;

        _designerButton.IsChecked = true;
    }

    /// <summary>Puts the markup on screen.</summary>
    public void ShowText()
    {
        _textView ??= _text();
        _host.Content = _textView;

        _textButton.IsChecked = true;
    }

    private static RadioButton Choice(string label, IconKind icon) =>
        new()
        {
            Classes = { "viewSwitch" },
            GroupName = "DesignerHostView",
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new IconView { Kind = icon, IconSize = 13 },
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }
                }
            },
            Padding = new Thickness(8, 2),
            MinWidth = 0
        };
}
