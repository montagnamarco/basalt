using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Workspace.ObjectBrowser;

namespace Basalt.Shell.Controls;

/// <summary>
/// A namespace in the type list; opening it lists its types. A record, so a
/// toggled namespace is a new row and the list redraws its chevron.
/// </summary>
public sealed record BrowserNamespaceRow(string Name, bool IsExpanded = false);

/// <summary>A type in the list: indented under its namespace, or flat in a search.</summary>
public sealed record BrowserTypeRow(BrowserSymbol Symbol, bool UnderNamespace);

/// <summary>A member in the right-hand list.</summary>
public sealed record BrowserMemberRow(BrowserSymbol Symbol);

/// <summary>How the browser draws its rows. Kept apart so the panel is only about behaviour.</summary>
internal static class ObjectBrowserRows
{
    private const double IconSize = 14;

    internal static Control Build(object? row) => row switch
    {
        BrowserNamespaceRow ns => Namespace(ns),
        BrowserTypeRow type => Symbol(type.Symbol, type.UnderNamespace ? Spacing.Loose : 0,
            secondary: type.UnderNamespace ? null : NamespaceOf(type.Symbol)),
        BrowserMemberRow member => Symbol(member.Symbol, 0,
            secondary: member.Symbol.IsInherited ? member.Symbol.DeclaringType : null),
        _ => new TextBlock()
    };

    private static string? NamespaceOf(BrowserSymbol symbol) =>
        symbol.Namespace.Length == 0 ? null : symbol.Namespace;

    private static Control Namespace(BrowserNamespaceRow ns)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing.Tight };

        row.Children.Add(new IconView
        {
            Kind = ns.IsExpanded ? IconKind.ChevronDown : IconKind.ChevronRight,
            IconSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        }.Themed(IconView.ForegroundProperty, "ChromeForegroundBrush"));

        row.Children.Add(Image(Basalt.Extensibility.SymbolKind.Namespace));

        row.Children.Add(new TextBlock
        {
            Text = ns.Name,
            FontSize = PanelMetrics.FontSize,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        return row;
    }

    private static Control Symbol(BrowserSymbol symbol, double indent, string? secondary)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Tight,
            Margin = new Thickness(indent, 0, 0, 0)
        };

        row.Children.Add(Image(symbol.Kind));

        row.Children.Add(new TextBlock
        {
            Text = symbol.Name,
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (secondary is { Length: > 0 })
            row.Children.Add(new TextBlock
            {
                Text = secondary,
                FontSize = PanelMetrics.SecondaryFontSize,
                VerticalAlignment = VerticalAlignment.Center
            }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush"));

        ToolTip.SetTip(row, symbol.Signature);

        return row;
    }

    private static Image Image(Basalt.Extensibility.SymbolKind kind) => new()
    {
        Source = CompletionIcons.For(kind),
        Width = IconSize,
        Height = IconSize,
        VerticalAlignment = VerticalAlignment.Center
    };
}
