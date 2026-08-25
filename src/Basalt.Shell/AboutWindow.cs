using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;

namespace Basalt.Shell;

/// <summary>
/// What Basalt is, and what it is running on.
///
/// Three lines of text before: no icon, no runtime, nothing that could be
/// copied. A bug report starts here, so what it says has to be selectable.
/// </summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = Localizer.Get(StringKeys.MenuHelpAbout);
        Width = 460;
        Height = 340;
        MinWidth = 380;
        MinHeight = 280;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var version = typeof(AboutWindow).Assembly.GetName().Version?.ToString() ?? "1.0";

        Details =
            $"Basalt {version}\n"
          + $".NET {Environment.Version}\n"
          + $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n"
          + $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";

        Content = new DockPanel
        {
            Margin = new Thickness(24),
            Children = { Footer(), Body(version) }
        };

        // Esc closes it, as it does everywhere else.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            Close();
            e.Handled = true;
        };
    }

    /// <summary>What the copy button puts on the clipboard.</summary>
    internal string Details { get; }

    /// <summary>
    /// Puts the details on the clipboard, which is where a bug report starts.
    /// </summary>
    internal async Task CopyDetailsAsync()
    {
        if (Clipboard is not { } clipboard) return;

        var transfer = new DataTransfer();

        transfer.Add(DataTransferItem.CreateText(Details));

        await clipboard.SetDataAsync(transfer);
    }

    private Control Body(string version)
    {
        // Bounded: inside a horizontal StackPanel a child is offered infinite
        // width, so TextWrapping never comes into play and the sentence runs
        // off the edge of the window.
        var lines = new StackPanel { Spacing = 2, MaxWidth = 320 };

        lines.Children.Add(new TextBlock
        {
            Text = "Basalt",
            FontSize = 24,
            FontWeight = FontWeight.Light
        });

        lines.Children.Add(new SelectableTextBlock
        {
            Text = $"Version {version}",
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(0, 0, 0, 12)
        });

        lines.Children.Add(new TextBlock
        {
            Text = "An IDE for Visual Basic and the Basic dialects, "
                 + "on macOS, Windows and Linux.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Selectable: this is what goes into a bug report.
        lines.Children.Add(new SelectableTextBlock
        {
            Text = $".NET {Environment.Version}",
            FontSize = 12,
            Opacity = 0.6
        });

        lines.Children.Add(new SelectableTextBlock
        {
            Text = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            FontSize = 12,
            Opacity = 0.6
        });

        lines.Children.Add(new SelectableTextBlock
        {
            Text = "MIT licence",
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(0, 12, 0, 0)
        });

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                new Controls.IconView
                {
                    Kind = Controls.IconKind.Application,
                    IconSize = 56,
                    VerticalAlignment = VerticalAlignment.Top
                },
                lines
            }
        };
    }

    private Control Footer()
    {
        var copy = new Button { Content = "Copy details" };

        copy.Click += (_, _) => Guarded.Run(CopyDetailsAsync, _ => { }, "about");

        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };

        close.Click += (_, _) => Close();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { copy, close }
        };

        DockPanel.SetDock(row, Avalonia.Controls.Dock.Bottom);

        return row;
    }
}
