using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>Why the Git panels have no repository to show.</summary>
public enum GitAbsence
{
    /// <summary>No solution is open: there is no folder to look in.</summary>
    NoSolution,

    /// <summary>The solution's folder is not a repository: one can be started there.</summary>
    NotARepository,

    /// <summary>git itself could not be run.</summary>
    GitNotInstalled
}

/// <summary>
/// What the Git Changes and Branches panels show in a folder that is not a
/// repository: one sentence and the button that starts one. The panel's own
/// boxes, buttons and lists are hidden while this is up, rather than left to
/// look like a repository with nothing in it.
/// </summary>
internal sealed class GitNoRepositoryView : Border
{
    private readonly TextBlock _message;

    /// <param name="startRepository">Runs when the button is pressed: the same command the Git menu runs.</param>
    public GitNoRepositoryView(Action startRepository)
    {
        _message = new TextBlock
        {
            Text = Localizer.Get(StringKeys.GitNotARepository),
            FontSize = PanelMetrics.FontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Center
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        Start = new Button
        {
            Content = Localizer.Get(StringKeys.MenuGitInitialize),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Start.Click += (_, _) => startRepository();

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = Spacing.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new IconView
                {
                    Kind = IconKind.Commit,
                    IconSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Center
                }.Themed(IconView.ForegroundProperty, "DescriptionForegroundBrush"),
                _message,
                Start
            }
        };

        Padding = new Thickness(Spacing.Normal * 2, Spacing.Loose);
        Child = stack;
    }

    /// <summary>
    /// Says why there is nothing to show. Only a folder that is not a
    /// repository offers to start one: with no solution there is no folder,
    /// and without git the start would fail.
    /// </summary>
    public void Show(GitAbsence absence)
    {
        _message.Text = Localizer.Get(absence switch
        {
            GitAbsence.NoSolution => StringKeys.GitNoSolution,
            GitAbsence.GitNotInstalled => StringKeys.GitNotInstalled,
            _ => StringKeys.GitNotARepository
        });
        Start.IsVisible = absence == GitAbsence.NotARepository;
    }

    /// <summary>The sentence shown, for the tests.</summary>
    internal string Message => _message.Text ?? "";

    /// <summary>The button that starts a repository, for the tests and for the panel's focus.</summary>
    internal Button Start { get; }
}
