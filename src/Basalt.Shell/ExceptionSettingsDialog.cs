using Avalonia;
using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Core.Services;

namespace Basalt.Shell;

/// <summary>
/// Which exceptions stop the debugger, apart from the ones nothing handles (those always stop).
///
/// Two choices, as netcoredbg offers them: stop at the throw of any exception, and stop on one the program's own
/// code does not handle. The choice is kept in the IDE settings by whoever opened the dialog.
/// </summary>
public sealed class ExceptionSettingsDialog : Window
{
    private readonly CheckBox _whenThrown;
    private readonly CheckBox _userUnhandled;

    /// <summary>What the user chose, or null if they cancelled.</summary>
    public ExceptionBreaks? Result { get; private set; }

    public ExceptionSettingsDialog() : this(new ExceptionBreaks(false, false)) { }

    public ExceptionSettingsDialog(ExceptionBreaks current)
    {
        Title = Localizer.Get(StringKeys.ExceptionSettingsTitle);
        Width = 520;
        Height = 280;

        // A floor, so resizing cannot hide what matters.
        MinWidth = 400; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _whenThrown = new CheckBox
        {
            Content = Localizer.Get(StringKeys.ExceptionSettingsBreakWhenThrown),
            IsChecked = current.WhenThrown
        };

        _userUnhandled = new CheckBox
        {
            Content = Localizer.Get(StringKeys.ExceptionSettingsBreakUserUnhandled),
            IsChecked = current.WhenUserUnhandled
        };

        var ok = new Button { Content = Localizer.Get(StringKeys.ButtonOk), IsDefault = true };
        ok.Click += (_, _) => Accept();

        var cancel = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), IsCancel = true };
        cancel.Click += (_, _) => Close();

        var layout = new StackPanel { Spacing = 12, Margin = new Thickness(20) };

        layout.Children.Add(_whenThrown);
        layout.Children.Add(Hint(Localizer.Get(StringKeys.ExceptionSettingsBreakWhenThrownHelp)));
        layout.Children.Add(_userUnhandled);
        layout.Children.Add(Hint(Localizer.Get(StringKeys.ExceptionSettingsBreakUserUnhandledHelp)));
        layout.Children.Add(DialogButtons.Row(ok, cancel));

        Content = layout;
    }

    /// <summary>The "any exception" box, so a test can read and tick it.</summary>
    internal CheckBox WhenThrownBoxForTests => _whenThrown;

    /// <summary>The "not handled in user code" box.</summary>
    internal CheckBox UserUnhandledBoxForTests => _userUnhandled;

    internal void Accept()
    {
        Result = new ExceptionBreaks(_whenThrown.IsChecked == true, _userUnhandled.IsChecked == true);
        Close();
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.65,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Margin = new Thickness(22, -8, 0, 0)
    };
}
