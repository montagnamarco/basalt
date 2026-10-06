using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Workspace.Ai;

namespace Basalt.Shell.Controls;

/// <summary>The user's decision about proposed changes.</summary>
public sealed record AiChangeDecision(AiChangeSet Set, IReadOnlyList<AiFileChange> Files, bool Accept);

/// <summary>
/// An assistant's proposed changes, file by file, with a diff for each and
/// Accept and Reject beside it, and for all of them at once.
///
/// Nothing in it writes anything: it only reports what the user decided.
/// </summary>
public sealed class AiChangeSetCard : UserControl
{
    private readonly AiChangeSet _set;
    private readonly Action<AiChangeDecision> _decide;

    public AiChangeSetCard(AiChangeSet set, Action<AiChangeDecision> decide)
    {
        _set = set;
        _decide = decide;

        var stack = new StackPanel { Spacing = Spacing.Tight };

        stack.Children.Add(new TextBlock
        {
            Text = set.Summary.Length > 0
                ? Localizer.Get(StringKeys.AiChangesTitle, set.Summary)
                : Localizer.Get(StringKeys.AiChangesTitlePlain),
            FontWeight = FontWeight.SemiBold,
            FontSize = PanelMetrics.FontSize,
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var file in set.Files) stack.Children.Add(BuildFile(file));

        if (set.Pending.Count > 0 && set.Files.All(f => f.State != AiChangeState.Applying))
        {
            var all = Row();
            all.Children.Add(Button(StringKeys.AiChangeAcceptAll, () => Decide(set.Pending, true), "AcceptAll"));
            all.Children.Add(Button(StringKeys.AiChangeRejectAll, () => Decide(set.Pending, false), "RejectAll"));
            stack.Children.Add(all);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(Spacing.Normal),
            BorderThickness = new Thickness(1),
            Child = stack
        };

        card.Themed(Border.BorderBrushProperty, "DescriptionForegroundBrush");

        Content = card;
    }

    private void Decide(IReadOnlyList<AiFileChange> files, bool accept) =>
        _decide(new AiChangeDecision(_set, files, accept));

    private Control BuildFile(AiFileChange file)
    {
        var stack = new StackPanel { Spacing = 2 };

        var header = Row();
        header.Children.Add(new TextBlock
        {
            Text = file.IsNewFile ? $"{file.RelativePath} ({Localizer.Get(StringKeys.AiChangeNewFile)})" : file.RelativePath,
            FontWeight = FontWeight.SemiBold,
            FontSize = PanelMetrics.SecondaryFontSize,
            VerticalAlignment = VerticalAlignment.Center
        });

        switch (file.State)
        {
            case AiChangeState.Pending:
                var busy = _set.Files.Any(f => f.State == AiChangeState.Applying);

                foreach (var button in new[]
                {
                    Button(StringKeys.AiChangeAccept, () => Decide([file], true), "Accept"),
                    Button(StringKeys.AiChangeReject, () => Decide([file], false), "Reject")
                })
                {
                    // While a file is being applied nothing else can be decided.
                    button.IsEnabled = !busy;
                    header.Children.Add(button);
                }

                break;

            case AiChangeState.Applying:
                header.Children.Add(Label(Localizer.Get(StringKeys.AiChangeApplying)));
                break;

            case AiChangeState.Failed:
                header.Children.Add(Label(Localizer.Get(StringKeys.AiChangeNotApplied, file.Problem ?? "")));
                break;

            default:
                header.Children.Add(Label(Localizer.Get(file.State == AiChangeState.Accepted
                    ? StringKeys.AiChangeAccepted
                    : StringKeys.AiChangeRejected)));
                break;
        }

        stack.Children.Add(header);

        // The diff is shown for a file still to be decided and for one that
        // failed, so the user can see what was not applied.
        if (file.State is AiChangeState.Pending or AiChangeState.Failed)
        {
            var diff = new DiffView();
            diff.Show(file.UnifiedDiff);

            stack.Children.Add(new ScrollViewer
            {
                Content = diff,
                MaxHeight = 260,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            });
        }

        return stack;
    }

    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = Spacing.Normal
    };

    private static TextBlock Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = PanelMetrics.SecondaryFontSize,
            VerticalAlignment = VerticalAlignment.Center
        };

        return label.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");
    }

    private static Button Button(string key, Action click, string name)
    {
        var button = new Button { Content = Localizer.Get(key), FontSize = PanelMetrics.FontSize, Name = name };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The buttons of the card, for the tests.</summary>
    internal IEnumerable<Button> Buttons =>
        this.GetLogicalDescendants().OfType<Button>();
}
