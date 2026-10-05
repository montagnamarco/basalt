namespace Basalt.Shell.Controls;

/// <summary>
/// The typography and row sizes the panels share.
///
/// A survey found panel text at 10, 11, 12 and 13 pixels, toolbar buttons at
/// 11 in one panel and 12 in the next, and rows of 22, 24 and 14. Two sizes
/// and one row height replace that: <see cref="FontSize"/> for what a panel
/// is about, <see cref="SecondaryFontSize"/> for what describes it. The same
/// numbers are published as <c>PanelFontSize</c>, <c>PanelSecondaryFontSize</c>
/// and <c>PanelRowHeight</c> in Theme.axaml for markup; a test keeps them equal.
/// </summary>
public static class PanelMetrics
{
    /// <summary>Rows, labels, buttons and inputs inside a panel.</summary>
    public const double FontSize = 12;

    /// <summary>Summaries, captions and hints beneath or beside the main text.</summary>
    public const double SecondaryFontSize = 11;

    /// <summary>How tall a list or tree row is.</summary>
    public const double RowHeight = 22;
}
