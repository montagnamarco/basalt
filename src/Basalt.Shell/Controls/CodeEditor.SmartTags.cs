using Avalonia.Controls;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed record EditorSmartTagAction(EditorDocumentViewModel Document, string Source,
    int Caret, long Generation, QuickAction Action);

internal sealed record EditorSmartTagCommand(EditorDocumentViewModel Document, string Source,
    int Caret, long Generation, EditorCommand Command);

public sealed partial class CodeEditor
{
    private readonly EditorSmartTagMargin _smartTagMargin = new();
    private CancellationTokenSource? _smartTagCancellation;
    private long _smartTagGeneration;
    private bool _smartTagsAttached;
    private string? _smartTagSource;
    private int _smartTagCaret;
    private int _smartTagSelectionStart;
    private int _smartTagSelectionLength;
    private IReadOnlyList<QuickAction> _smartTagActions = [];
    private ContextMenu? _smartTagMenu;

    internal EditorSmartTagMargin SmartTagMargin => _smartTagMargin;
    internal event EventHandler<EditorSmartTagAction>? SmartTagActionChosen;
    internal event EventHandler<EditorSmartTagCommand>? SmartTagCommandChosen;
    internal Func<string, string, int, CancellationToken, Task<IReadOnlyList<QuickAction>>>? SmartTagProvider { get; set; }
    internal Func<string, int, string, Task<Basalt.Extensibility.SymbolDescription?>>? DescribeSymbolProvider { get; set; }

    private void InstallSmartTags()
    {
        if (_document.Language != SourceLanguage.VisualBasic)
        {
            return;
        }

        _smartTagMargin.TextView = _editor.TextArea.TextView;
        _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => _smartTagMargin.UpdatePosition();
        _editor.TextArea.TextView.VisualLinesChanged += (_, _) => _smartTagMargin.UpdatePosition();
        _editor.LayoutUpdated += (_, _) => _smartTagMargin.UpdatePosition();
        _smartTagMargin.Requested += async (_, _) =>
        {
            try
            {
                await ShowSmartTagsAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _shell.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsFailed, exception.Message));
            }
        };
        _editor.TextArea.Caret.PositionChanged += (_, _) => QueueSmartTags();
        _editor.TextChanged += (_, _) => QueueSmartTags();
        _editor.TextArea.SelectionChanged += (_, _) => QueueSmartTags();
        AttachedToVisualTree += (_, _) =>
        {
            _smartTagsAttached = true;
            QueueSmartTags();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _smartTagsAttached = false;
            InvalidateSmartTags();
        };
    }

    private void InvalidateSmartTags()
    {
        _smartTagGeneration++;
        _smartTagCancellation?.Cancel();
        _smartTagCancellation?.Dispose();
        _smartTagCancellation = null;
        _smartTagSource = null;
        _smartTagActions = [];
        _smartTagMargin.Show(0);
        _smartTagMenu?.Close();
    }

    private void QueueSmartTags()
    {
        if (!_smartTagsAttached)
        {
            return;
        }

        InvalidateSmartTags();
        _smartTagCancellation = new CancellationTokenSource();
        _ = DiscoverSmartTagsAsync(_smartTagGeneration, immediate: false, _smartTagCancellation.Token);
    }

    private async Task DiscoverSmartTagsAsync(long generation, bool immediate, CancellationToken cancellationToken)
    {
        var source = _editor.Text;
        var caret = _editor.CaretOffset;
        _smartTagSelectionStart = _editor.SelectionStart;
        _smartTagSelectionLength = _editor.SelectionLength;
        try
        {
            if (!immediate)
            {
                await Task.Delay(250, cancellationToken);
            }

            Func<string, string, int, CancellationToken, Task<IReadOnlyList<QuickAction>>> provider =
                SmartTagProvider ?? _shell.GetQuickActionsAsync;
            var actions = await provider(_document.FilePath, source, caret, cancellationToken);
            if (!IsSmartTagRequestCurrent(source, caret, generation) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _smartTagSource = source;
            _smartTagCaret = caret;
            _smartTagActions = actions;
            var anchor = actions.FirstOrDefault()?.ApplicableSpan?.Start ?? caret;
            _smartTagMargin.Show(actions.Count > 0 ? _editor.Document.GetLineByOffset(anchor).LineNumber : 0, anchor);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (IsSmartTagRequestCurrent(source, caret, generation))
            {
                _shell.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsFailed, exception.Message));
            }
        }
    }

    internal bool IsSmartTagRequestCurrent(string source, int caret, long generation) =>
        _smartTagsAttached && generation == _smartTagGeneration && _document.Text == source
        && _editor.Text == source && _editor.CaretOffset == caret
        && _editor.SelectionStart == _smartTagSelectionStart
        && _editor.SelectionLength == _smartTagSelectionLength;

    internal async Task ShowSmartTagsAsync()
    {
        if (!_smartTagsAttached || _document.Language != SourceLanguage.VisualBasic)
        {
            // Ctrl+. on a document with no quick actions at all (not Visual
            // Basic) answers like one with none to offer, not with silence
            // that reads as the key not having been received.
            _shell.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsNone));
            return;
        }

        // An empty answer is asked again, not reused: it can be one given before the solution loaded, and Ctrl+. must not
        // report "none" for the rest of the text's life. Rename and Extract are still offered alongside it.
        if (_smartTagSource != _editor.Text || _smartTagCaret != _editor.CaretOffset || _smartTagActions.Count == 0)
        {
            InvalidateSmartTags();
            _smartTagCancellation = new CancellationTokenSource();
            var discoveryGeneration = _smartTagGeneration;
            await DiscoverSmartTagsAsync(discoveryGeneration, immediate: true, _smartTagCancellation.Token);
            if (discoveryGeneration != _smartTagGeneration || !_smartTagsAttached)
            {
                return;
            }
        }

        if (_smartTagSource is null)
        {
            _shell.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsNone));
            return;
        }

        var items = _smartTagActions.Select(action =>
        {
            var request = new EditorSmartTagAction(_document, _smartTagSource, _smartTagCaret, _smartTagGeneration, action);
            var item = new MenuItem { Header = action.Title };
            item.Click += (_, _) =>
            {
                if (IsSmartTagRequestCurrent(request.Source, request.Caret, request.Generation))
                {
                    SmartTagActionChosen?.Invoke(this, request);
                }
            };
            return item;
        }).Cast<Control>().ToList();

        var source = _smartTagSource;
        var caret = _smartTagCaret;
        var generation = _smartTagGeneration;
        Func<string, int, string, Task<Basalt.Extensibility.SymbolDescription?>> describeSymbol =
            DescribeSymbolProvider ?? _shell.DescribeSymbolAsync;
        var symbol = await Task.Run(() => describeSymbol(_document.FilePath, caret, source));
        if (!IsSmartTagRequestCurrent(source, caret, generation))
        {
            return;
        }

        if (symbol is not null)
        {
            AddCommand(EditorCommand.Rename, StringKeys.EditorSmartTagRename);
        }

        if (_editor.SelectionLength > 0)
        {
            AddCommand(EditorCommand.ExtractVariable, StringKeys.EditorSmartTagExtractVariable);
            AddCommand(EditorCommand.ExtractMethod, StringKeys.EditorSmartTagExtractMethod);
        }

        if (items.Count == 0)
        {
            _shell.WriteOutput(Localizer.Get(StringKeys.EditorSmartTagsNone));
            return;
        }

        void AddCommand(EditorCommand command, string key)
        {
            var request = new EditorSmartTagCommand(_document, source, caret, generation, command);
            var item = new MenuItem { Header = Localizer.Get(key) };
            item.Click += (_, _) =>
            {
                if (IsSmartTagRequestCurrent(source, caret, generation))
                {
                    SmartTagCommandChosen?.Invoke(this, request);
                }
            };
            items.Add(item);
        }

        _smartTagMenu = new ContextMenu { ItemsSource = items };
        _smartTagMargin.ContextMenu = _smartTagMenu;
        _smartTagMenu.Open(_smartTagMargin);
    }
}
