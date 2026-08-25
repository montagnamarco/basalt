using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly TextBlock _empty;
    private IReadOnlyList<DocumentSymbol> _symbols = [];

    public OutlinePanel()
    {
        _empty = new TextBlock
        {
            Text = "No symbols.",
            Opacity = 0.6,
            FontSize = 12,
            Margin = new Thickness(12, 8, 8, 8)
        };

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
            new("Go to Symbol", () => Choose(OutlineCommand.GoTo)) { IsAvailable = HasSymbol },
            new("Copy Name", () => Choose(OutlineCommand.CopyName)) { IsAvailable = HasSymbol },
            MenuAction.Separator,
            new("Expand All", () => Choose(OutlineCommand.ExpandAll)),
            new("Collapse All", () => Choose(OutlineCommand.CollapseAll))
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
            Spacing = 6
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
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (symbol.Detail is { Length: > 0 } detail)
            row.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 11,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center
            });

        return row;
    }
}
