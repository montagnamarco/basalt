using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Basalt.Shell.Controls;

/// <summary>
/// A row of tabs that gives each only as much as it needs, and shares the
/// width out when there is not enough.
///
/// Dock lays tabs out in a horizontal stack inside a scroll viewer, which
/// measures each at its natural width: in the narrow left column the third
/// tab was simply cut off at the edge ("Outli", "oIbox"), glyph by glyph,
/// with nothing to say there was more. Here the tabs shrink together, down
/// to <see cref="MinTabWidth"/>, and each tab's label trims with an ellipsis
/// and carries a tooltip. Below that width the row is clipped at the strip's
/// edge: Dock offers no overflow list for tool tabs (the title bar's menu is
/// the panel's command menu, not a list of its tabs), so the left column has
/// to stay wide enough for its tabs at <see cref="MinTabWidth"/> each.
/// </summary>
public sealed class ShrinkingTabPanel : Panel
{
    /// <summary>The narrowest a tab is made before the row overflows.</summary>
    public const double MinTabWidth = 44;

    protected override Size MeasureOverride(Size availableSize)
    {
        var natural = new List<double>(Children.Count);
        var chrome = new List<double>(Children.Count);
        var height = 0.0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));

            // What the tab would ask for with nothing capping its label,
            // worked out from the text rather than by uncapping and measuring:
            // that changed the label on every pass, and each change asked for
            // another pass, so the final state depended on which pass was last.
            var label = FindLabel(child);

            // Read before TextWidth: it may re-measure the label, and the
            // tab's own DesiredSize still holds the width it had when this
            // was taken, so the two have to describe the same moment.
            var have = label?.DesiredSize.Width ?? 0;
            var wanted = label is null ? 0 : TextWidth(label);

            natural.Add(child.DesiredSize.Width - have + wanted);
            chrome.Add(child.DesiredSize.Width - have);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var widths = Share(natural, availableSize.Width);

        for (var i = 0; i < Children.Count; i++)
        {
            if (FindLabel(Children[i]) is { } label)
            {
                if (natural[i] <= widths[i])
                {
                    if (label.IsSet(Layoutable.MaxWidthProperty)) label.ClearValue(Layoutable.MaxWidthProperty);
                }
                else
                {
                    var cap = Math.Max(1, widths[i] - chrome[i]);
                    if (label.MaxWidth != cap) label.MaxWidth = cap;
                }
            }

            Children[i].Measure(new Size(widths[i], availableSize.Height));
        }

        return new Size(Children.Sum(c => c.DesiredSize.Width), height);
    }

    private sealed record NaturalWidth(string? Text, double FontSize, double Width);

    private static readonly ConditionalWeakTable<TextBlock, NaturalWidth> Naturals = new();

    /// <summary>
    /// The width a label asks for when nothing caps it.
    ///
    /// Remembered per text and size: a capped label measures as the cap, and
    /// uncapping it to find out changed it on every pass, and each change
    /// asked for another pass, so the final state depended on which pass was
    /// last.
    /// </summary>
    private static double TextWidth(TextBlock label)
    {
        if (!label.IsSet(Layoutable.MaxWidthProperty))
        {
            var natural = label.DesiredSize.Width;
            Naturals.AddOrUpdate(label, new NaturalWidth(label.Text, label.FontSize, natural));
            return natural;
        }

        if (Naturals.TryGetValue(label, out var known)
            && known.Text == label.Text && known.FontSize == label.FontSize)
            return known.Width;

        label.ClearValue(Layoutable.MaxWidthProperty);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return label.DesiredSize.Width;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;

        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return new Size(x, finalSize.Height);
    }

    private static TextBlock? FindLabel(Control tab) =>
        tab.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.GetVisualAncestors().OfType<ContentPresenter>()
                .Any(p => p.Name == "PART_HeaderPresenter"));

    /// <summary>
    /// The width each tab gets: its own when everything fits, otherwise the
    /// wide ones give way first so short labels are not trimmed for nothing.
    /// </summary>
    internal static IReadOnlyList<double> Share(IReadOnlyList<double> natural, double available)
    {
        if (double.IsInfinity(available) || natural.Sum() <= available)
            return natural;

        var widths = natural.Select(w => Math.Max(w, MinTabWidth)).ToArray();
        var floor = natural.Select(w => Math.Min(w, MinTabWidth)).ToArray();

        // Find the cap that makes the row fit: tabs wider than it are cut to
        // it, narrower ones keep their width.
        double low = MinTabWidth, high = natural.Max();

        for (var i = 0; i < 24; i++)
        {
            var cap = (low + high) / 2;
            var total = natural.Sum(w => Math.Min(w, cap));
            if (total > available) high = cap; else low = cap;
        }

        for (var i = 0; i < widths.Length; i++)
            widths[i] = Math.Min(natural[i], Math.Max(low, floor[i]));

        return widths;
    }
}
