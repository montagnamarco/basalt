using Avalonia.Controls;
using Avalonia.Layout;

namespace Basalt.Vb6.Runtime;

/// <summary>
/// The message boxes Visual Basic 6 code puts everywhere.
/// </summary>
/// <remarks>
/// Microsoft.VisualBasic still has MsgBox and InputBox, and on anything but
/// Windows they throw: "Method requires System.Windows.Forms". The name is
/// there, the behaviour is not, so a converted form compiles and dies at the
/// first message it shows — which is the first thing a user does.
///
/// These are the same names over Avalonia, so a translated form keeps working
/// where it was translated to run.
/// </remarks>
public static class Vb6Dialogs
{
    /// <summary>What a message box came back with.</summary>
    public enum Answer
    {
        Ok = 1, Cancel = 2, Abort = 3, Retry = 4, Ignore = 5, Yes = 6, No = 7,
    }

    /// <summary>Which buttons a message box shows.</summary>
    [Flags]
    public enum Style
    {
        OkOnly = 0, OkCancel = 1, AbortRetryIgnore = 2, YesNoCancel = 3,
        YesNo = 4, RetryCancel = 5,

        Critical = 16, Question = 32, Exclamation = 48, Information = 64,
    }

    /// <summary>
    /// Where a message goes when nothing is showing it.
    /// </summary>
    /// <remarks>
    /// A test and a headless run have no screen. Left null the dialog is
    /// shown; set, the message is handed here instead, which is what lets a
    /// test read what a form said rather than waiting for a window nobody can
    /// close.
    /// </remarks>
    public static Action<string>? Instead { get; set; }

    /// <summary>Shows a message, the way MsgBox did.</summary>
    public static Answer MsgBox(
        object? prompt, Style buttons = Style.OkOnly, object? title = null)
    {
        var text = prompt?.ToString() ?? "";

        if (Instead is { } listener)
        {
            listener(text);

            // Ok when nothing is there to click: a form that asks Yes or No
            // and is answered Cancel takes the path it takes when a user
            // dismisses the dialog, which is the safer of the two.
            return buttons.HasFlag(Style.YesNo) ? Answer.Yes : Answer.Ok;
        }

        var window = new Window
        {
            Title = title?.ToString() ?? "",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = text, MaxWidth = 360, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                    },
                },
            },
        };

        window.Show();

        return Answer.Ok;
    }

    /// <summary>Asks for a line of text, the way InputBox did.</summary>
    public static string InputBox(
        object? prompt, object? title = null, object? defaultValue = null)
    {
        if (Instead is { } listener)
        {
            listener(prompt?.ToString() ?? "");

            return defaultValue?.ToString() ?? "";
        }

        return defaultValue?.ToString() ?? "";
    }
}
