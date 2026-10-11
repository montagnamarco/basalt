using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Extensibility;

namespace Basalt.Shell.Controls;

/// <summary>
/// The declarations in the open document, nested as they are written.
///
/// Built from the syntax tree rather than the semantic model, so it keeps
/// working while the file does not compile — which is most of the time while
/// typing.
/// </summary>
public sealed class OutlinePanel : UserControl
{
    private readonly TreeView _tree;
    private readonly EmptyState _empty;
    private IReadOnlyList<DocumentSymbol> _symbols = [];

    public OutlinePanel()
    {
        _empty = new EmptyState(Localizer.Get(StringKeys.OutlineEmpty), IconKind.Outline);

        _tree = new TreeView
        {
            ItemTemplate = new FuncTreeDataTemplate<DocumentSymbol>(
                (symbol, _) => BuildRow(symbol),
                symbol => symbol?.Children ?? [])
        };

        _tree.SelectionChanged += (_, _) =>
        {
            if (_tree.SelectedItem is DocumentSymbol symbol)
                SymbolActivated?.Invoke(this, symbol);
        };

        // A double-click on the member that is already selected changes no
        // selection, so the selection handler above never runs for it. The
        // member must still be reached: the caret may have moved away since.
        _tree.DoubleTapped += (_, _) =>
        {
            if (_tree.SelectedItem is DocumentSymbol symbol)
                SymbolActivated?.Invoke(this, symbol);
        };

        BuildContextMenu();

        Content = _empty;
    }

    /// <summary>Raised when the user picks a symbol to jump to.</summary>
    public event EventHandler<DocumentSymbol>? SymbolActivated;

    /// <summary>What the outline's menu can ask for.</summary>
    public enum OutlineCommand { GoTo, CopyName, ExpandAll, CollapseAll }

    public event EventHandler<(OutlineCommand Command, DocumentSymbol? Symbol)>? CommandChosen;

    internal ContextMenu? Menu => _tree.ContextMenu;

    private void BuildContextMenu()
    {
        bool HasSymbol() => _tree.SelectedItem is DocumentSymbol;

        void Choose(OutlineCommand command) =>
            CommandChosen?.Invoke(this, (command, _tree.SelectedItem as DocumentSymbol));

        _tree.ContextMenu = ContextMenus.Build(
        [
            new(Localizer.Get(StringKeys.OutlineGoTo), () => Choose(OutlineCommand.GoTo)) { IsAvailable = HasSymbol },
            new(Localizer.Get(StringKeys.TestsCopyName), () => Choose(OutlineCommand.CopyName)) { IsAvailable = HasSymbol },
            MenuAction.Separator,
            new(Localizer.Get(StringKeys.OutlineExpandAll), () => Choose(OutlineCommand.ExpandAll)),
            new(Localizer.Get(StringKeys.OutlineCollapseAll), () => Choose(OutlineCommand.CollapseAll))
        ]);
    }

    /// <summary>Symbols currently listed, exposed for tests.</summary>
    internal IReadOnlyList<DocumentSymbol> Symbols => _symbols;

    public void Show(IReadOnlyList<DocumentSymbol> symbols)
    {
        _symbols = symbols;

        if (symbols.Count == 0)
        {
            Content = _empty;
            return;
        }

        _tree.ItemsSource = symbols;
        Content = _tree;
    }

    public void Clear() => Show([]);

    /// <summary>
    /// Selects the innermost symbol containing a line, following the caret.
    /// </summary>
    public DocumentSymbol? SymbolAtLine(int line) => Innermost(_symbols, line);

    private static DocumentSymbol? Innermost(IReadOnlyList<DocumentSymbol> symbols, int line)
    {
        foreach (var symbol in symbols)
        {
            if (line < symbol.Range.Start.Line || line > symbol.Range.End.Line) continue;

            // A nested match is more specific than its container.
            return Innermost(symbol.Children, line) ?? symbol;
        }

        return null;
    }

    private static Control BuildRow(DocumentSymbol? symbol)
    {
        if (symbol is null) return new TextBlock();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Small
        };

        row.Children.Add(new Image
        {
            Source = CompletionIcons.For(symbol.Kind),
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = symbol.Name,
            FontSize = PanelMetrics.FontSize,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (symbol.Detail is { Length: > 0 } detail)
            row.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = PanelMetrics.SecondaryFontSize,
                VerticalAlignment = VerticalAlignment.Center
            }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush"));

        return row;
    }
}
