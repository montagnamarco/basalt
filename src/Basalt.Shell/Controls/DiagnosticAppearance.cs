using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Core.Model;

namespace Basalt.Shell.Controls;

/// <summary>
/// How a severity looks in the Problems panel: an icon, a thin accent bar
/// down the row's left edge and, at most, a faint tint behind the row.
///
/// Every colour is the theme's own error, warning or accent token, so the
/// marks stay readable in all seven themes instead of being a fixed red that
/// is correct on white and muddy on a dark surface.
/// </summary>
internal static class DiagnosticAppearance
{
    /// <summary>How much of the token's colour tints a row. Faint: the bar and icon carry the meaning.</summary>
    internal const double TintOpacity = 0.12;

    /// <summary>The colour token (not brush) for a severity.</summary>
    internal static string ColorKey(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "ErrorForeground",
        DiagnosticSeverity.Warning => "WarningForeground",
        _ => "FocusBorder"
    };

    /// <summary>The brush resource for a severity.</summary>
    internal static string BrushKey(DiagnosticSeverity severity) => ColorKey(severity) + "Brush";

    internal static IconKind IconFor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => IconKind.Error,
        DiagnosticSeverity.Warning => IconKind.Warning,
        _ => IconKind.Information
    };

    /// <summary>
    /// Tints a row with the severity's token at <see cref="TintOpacity"/>.
    ///
    /// Bound to the resource rather than computed once, so the tint follows
    /// the theme when it is switched while the panel is open. A solid light
    /// colour here read as a near-white smear on a dark row.
    /// </summary>
    internal static void Tint(Control row, DiagnosticSeverity severity)
    {
        if (severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
        {
            row.ClearValue(TemplatedControl.BackgroundProperty);
            return;
        }

        row.Bind(TemplatedControl.BackgroundProperty, row.GetResourceObservable(
            ColorKey(severity),
            value => value is Color color ? new SolidColorBrush(color, TintOpacity) : (IBrush?)null));
    }

    /// <summary>The severity cell: an accent bar against the left edge and the icon beside it.</summary>
    internal static Control CreateCell(DiagnosticSeverity severity)
    {
        var bar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left }
            .Themed(Border.BackgroundProperty, BrushKey(severity));

        var icon = new IconView { Kind = IconFor(severity), IconSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .Themed(IconView.ForegroundProperty, BrushKey(severity));

        var cell = new Grid { Children = { bar, icon } };
        ToolTip.SetTip(cell, Localizer.Get(StringKeys.ColumnSeverity) + ": " + LocalizedSeverity(severity));

        return cell;
    }

    /// <summary>The severity's own name, localized rather than its raw enum spelling.</summary>
    internal static string LocalizedSeverity(DiagnosticSeverity severity) => Localizer.Get(severity switch
    {
        DiagnosticSeverity.Error => StringKeys.SeverityError,
        DiagnosticSeverity.Warning => StringKeys.SeverityWarning,
        DiagnosticSeverity.Info => StringKeys.SeverityInfo,
        _ => StringKeys.SeverityHidden
    });
}
