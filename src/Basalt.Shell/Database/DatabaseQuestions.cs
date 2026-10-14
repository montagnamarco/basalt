using Avalonia.Controls;
using Avalonia.Layout;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;

namespace Basalt.Shell.Database;

/// <summary>The questions the Database client asks before it does something it cannot take back.</summary>
public static class DatabaseQuestions
{
    /// <summary>
    /// Asks whether to go ahead. Cancel, Escape and closing the window all answer no.
    /// </summary>
    public static async Task<bool> ConfirmAsync(Window owner, string message, string confirmLabel)
    {
        var answer = false;

        var confirm = new Button { Content = confirmLabel, Name = "DatabaseConfirm", Classes = { "primary" } };
        var cancel = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), Name = "DatabaseConfirmCancel", IsCancel = true };

        var dialog = new Window
        {
            Title = confirmLabel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        confirm.Click += (_, _) => { answer = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Spacing = Spacing.Normal,
            Margin = Spacing.DialogPadding,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = Spacing.Normal,
                    Children = { confirm, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);

        return answer;
    }
}
