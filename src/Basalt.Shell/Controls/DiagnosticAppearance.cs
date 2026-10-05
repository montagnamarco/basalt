using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Core.Model;

namespace Basalt.Shell.Controls;

internal static class DiagnosticAppearance
{
    // Partly transparent rather than a fixed light colour: a solid "#FFF1F0"
    // or "#FFFBE5" reads as a pale tint on a white row and as a near-white
    // smear on a dark one, since a dark theme's row behind it never shows
    // through. Blending with whatever the row already is keeps the tint
    // readable on every theme without this class having to know which one
    // is active.
    internal static IBrush Background(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => new SolidColorBrush(Color.FromArgb(70, 232, 17, 35)),
        DiagnosticSeverity.Warning => new SolidColorBrush(Color.FromArgb(70, 255, 185, 0)),
        _ => Brushes.Transparent
    };

    internal static Control CreateIcon(DiagnosticSeverity severity)
    {
        var warning = severity == DiagnosticSeverity.Warning;
        var error = severity == DiagnosticSeverity.Error;
        var icon = new Grid { Width = 16, Height = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        icon.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(warning ? "M8,1 L15,14 L1,14 Z" : "M8,1 A7,7 0 1 1 7.99,1 Z"),
            Fill = error ? Brushes.Firebrick : warning ? Brushes.Goldenrod : Brushes.SteelBlue,
            Stretch = Stretch.Uniform
        });
        icon.Children.Add(new TextBlock
        {
            Text = error ? "×" : warning ? "!" : "i",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        ToolTip.SetTip(icon, Localizer.Get(StringKeys.ColumnSeverity) + ": " + LocalizedSeverity(severity));
        return icon;
    }

    /// <summary>The severity's own name, localized rather than its raw enum spelling.</summary>
    private static string LocalizedSeverity(DiagnosticSeverity severity) => Localizer.Get(severity switch
    {
        DiagnosticSeverity.Error => StringKeys.SeverityError,
        DiagnosticSeverity.Warning => StringKeys.SeverityWarning,
        DiagnosticSeverity.Info => StringKeys.SeverityInfo,
        _ => StringKeys.SeverityHidden
    });
}
