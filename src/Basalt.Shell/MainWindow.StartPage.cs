using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;

namespace Basalt.Shell;

/// <summary>
/// The welcome screen's own two actions and its list of recent work.
///
/// Visual Studio's start page puts "Open recent" and "Get started" side by
/// side; the right column is static markup in MainWindow.axaml, but the left
/// one depends on the settings file and changes while the IDE runs, so it is
/// filled from here.
/// </summary>
public partial class MainWindow
{
    private void OnNewSolutionClick(object? sender, RoutedEventArgs e) =>
        _ = ShowNewSolutionDialogAsync();

    private void OnOpenSolutionClick(object? sender, RoutedEventArgs e) =>
        _ = ShowOpenSolutionDialogAsync();

    /// <summary>
    /// Fills the list of recent solutions on the welcome screen.
    ///
    /// Always shown, even with nothing to list: a heading with nothing under
    /// it reads as broken, where a line saying there is nothing yet reads as
    /// the normal state of a freshly installed IDE.
    /// </summary>
    private void ShowRecentOnWelcome()
    {
        var recent = _settingsStore.Load().Recent.ExistingSolutions();

        WelcomeRecent.Children.Clear();

        if (recent.Count == 0)
        {
            WelcomeRecent.Children.Add(new TextBlock
            {
                Text = Localizer.Get(StringKeys.WelcomeRecentEmpty),
                FontSize = 12,
                Opacity = 0.6,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            return;
        }

        // Five: enough to reach yesterday without turning the welcome screen
        // into a list.
        foreach (var path in recent.Take(5))
        {
            var target = path;
            var folder = Path.GetDirectoryName(target) ?? "";

            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new IconView { Kind = IconKind.Solution, IconSize = 15 },
                        new StackPanel
                        {
                            Spacing = 0,
                            Children =
                            {
                                new TextBlock { Text = Path.GetFileNameWithoutExtension(target) },

                                // The folder, the way Visual Studio's own
                                // recent list shows it: the name alone cannot
                                // tell two solutions called "App" apart.
                                new TextBlock
                                {
                                    Text = folder,
                                    FontSize = 11,
                                    Opacity = 0.6,
                                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                                }
                            }
                        }
                    }
                },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4),
                [ToolTip.TipProperty] = target
            };

            button.Click += (_, _) => Guarded.Run(
                () => OpenRecentSolutionAsync(target), ViewModel.WriteOutput, "ide");

            WelcomeRecent.Children.Add(button);
        }
    }

    /// <summary>
    /// Opens a solution from the welcome list, or takes it off the list when
    /// it has gone.
    ///
    /// The list was read when the window was built, and a folder can be moved
    /// or deleted since. Opening such a path would make an empty solution that
    /// the IDE calls open, so it is checked here, said to the user, and
    /// dropped from the recent list.
    /// </summary>
    private Task OpenRecentSolutionAsync(string path)
    {
        if (File.Exists(path))
            return OpenSolutionAskingToSaveAsync(path);

        ViewModel.WriteOutput(Localizer.Get(StringKeys.WelcomeRecentMissing, path));

        var settings = _settingsStore.Load();

        if (settings.Recent.Prune())
        {
            _settingsStore.Save(settings);
            InstallMenu(ViewModel);
        }

        ShowRecentOnWelcome();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Puts a solution or a file at the top of the list it belongs to.
    /// </summary>
    private void Remember(string path, bool isSolution)
    {
        var settings = _settingsStore.Load();

        var before = Listed(settings.Recent, isSolution);

        if (isSolution) settings.Recent.RememberSolution(path);
        else settings.Recent.RememberFile(path);

        _settingsStore.Save(settings);

        // Only when the list really changed. Rebuilding on every file opened
        // meant replacing the whole macOS menu bar each time, which leaves it
        // stale: reopening the same file changes nothing and was rebuilding
        // it anyway.
        if (string.Equals(before, Listed(settings.Recent, isSolution), StringComparison.Ordinal))
            return;

        InstallMenu(ViewModel);
        ShowRecentOnWelcome();
    }

    /// <summary>The list as one string, to tell whether it moved.</summary>
    private static string Listed(Basalt.Core.Settings.RecentSettings recent, bool solutions) =>
        string.Join("\n", solutions ? recent.Solutions : recent.Files);
}
