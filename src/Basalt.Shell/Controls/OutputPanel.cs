using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>What a line of output is about, which decides its colour.</summary>
public enum OutputLineKind
{
    Normal,
    Error,
    Warning,
    Success
}

/// <summary>Textual output of builds and program runs.</summary>
public sealed partial class OutputPanel : UserControl
{
    // The diagnostic keyword is followed by its code ("error BC30451:") or by
    // a colon; the summary line "0 Error(s)" has neither and must stay plain.
    //
    // The Italian spellings (errore, avviso, Compilazione ...) stay for the
    // one input that still produces them: DOTNET_CLI_UI_LANGUAGE set to Italian
    // by the user (DotnetProcess keeps a value the user chose), or a build
    // started outside the IDE whose output is shown here. The IDE's own
    // processes run in English. The patterns are source generated, so they are
    // compiled once at build time, not per line.
    [GeneratedRegex(@"(\[error\]|\b(?:error|errore)\s+[A-Za-z]{1,8}\d{1,6}\s*:|(?:^|[\s:])(?:error|errore)\s*:|\bBuild FAILED\b|\bCompilazione NON riuscita\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorPattern { get; }

    [GeneratedRegex(@"(\[warning\]|\b(?:warning|avviso)\s+[A-Za-z]{1,8}\d{1,6}\s*:|(?:^|[\s:])(?:warning|avviso)\s*:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WarningPattern { get; }

    [GeneratedRegex(@"\b(Build succeeded|Compilazione completata)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuccessPattern { get; }

    /// <summary>Whether a line reports an error, a warning, success, or nothing in particular.</summary>
    public static OutputLineKind Classify(string line)
    {
        if (ErrorPattern.IsMatch(line)) return OutputLineKind.Error;
        if (WarningPattern.IsMatch(line)) return OutputLineKind.Warning;
        if (SuccessPattern.IsMatch(line)) return OutputLineKind.Success;

        return OutputLineKind.Normal;
    }

    private readonly MainWindowViewModel _viewModel;
    private readonly ItemsControl _list;
    private readonly ScrollViewer _output;
    private readonly EmptyState _empty;
    private readonly ContentControl _body = new();
    private readonly ToggleButton _wrap;
    private readonly ToggleButton _scrollToEnd;
    private FontFamily _font;
    private bool _scrollPending;

    public OutputPanel(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;

        // Read once: the settings file is on disk, and attaching the panel
        // (every time it is docked, floated or tabbed back) used to read it
        // again on the interface thread. Later changes arrive in ApplySettings.
        _font = new FontFamily(viewModel.LoadSettings?.Invoke().Editor.FontFamily ?? new EditorSettings().FontFamily);

        _output = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };

        // A virtualizing panel inside the scroll viewer: only the lines in view
        // exist as controls, however many of the five thousand are kept.
        _list = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(MainWindowViewModel.BuildOutput)),
            // Inherited by every line, so a change of font reaches the ones
            // already drawn without touching them.
            FontFamily = _font,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<string>((row, _) => BuildLine(row)),
            Template = new FuncControlTemplate<ItemsControl>((_, scope) =>
            {
                var presenter = new ItemsPresenter
                {
                    Name = "PART_ItemsPresenter",
                    Margin = new Thickness(Spacing.Normal, Spacing.Tight)
                }.RegisterInNameScope(scope);
                presenter[!ItemsPresenter.ItemsPanelProperty] = new TemplateBinding(ItemsControl.ItemsPanelProperty);
                _output.Content = presenter;

                return _output;
            })
        };

        // One style on the list rather than a binding per line: five thousand
        // lines would otherwise carry five thousand subscriptions.
        _list.Styles.Add(new Style(x => x.OfType<ItemsControl>().Class("wrap").Descendant().OfType<SelectableTextBlock>())
        {
            Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) }
        });

        // Before the first build there is nothing to show, and an empty panel
        // gives no hint whether the build ran and said nothing or never ran.
        _empty = new EmptyState(Localizer.Get(StringKeys.OutputEmpty), IconKind.Output);

        var clear = PanelToolbar.IconButton(IconKind.Close, Localizer.Get(StringKeys.OutputClear),
            () => viewModel.BuildOutput.Clear());

        _wrap = PanelToolbar.TextToggle("↵", Localizer.Get(StringKeys.OutputWordWrap));
        _wrap.IsCheckedChanged += (_, _) => ApplyWrap();

        _scrollToEnd = PanelToolbar.TextToggle("↓", Localizer.Get(StringKeys.OutputScrollToEnd), isChecked: true);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Hairline * 2,
            Children = { clear, _wrap, _scrollToEnd }
        };

        DataContext = viewModel;
        Content = new DockPanel { Children = { PanelToolbar.Row(tools), _body } };

        viewModel.BuildOutput.CollectionChanged += (_, _) =>
        {
            ShowWhicheverFits();
            ScrollToEndWhenLocked();
        };

        ShowWhicheverFits();
    }

    /// <summary>
    /// Follows the editor's font when it changes in Settings. Lines already
    /// drawn inherit it from the list, so nothing is rebuilt.
    /// </summary>
    public void ApplySettings(IdeSettings settings)
    {
        _font = new FontFamily(settings.Editor.FontFamily);
        _list.FontFamily = _font;
    }

    private static Control BuildLine(string? row)
    {
        var text = row ?? "";

        var line = new SelectableTextBlock { Text = text };
        line.Classes.Add("outputLine");

        switch (Classify(text))
        {
            case OutputLineKind.Error: line.Classes.Add("error"); break;
            case OutputLineKind.Warning: line.Classes.Add("warning"); break;
            case OutputLineKind.Success: line.Classes.Add("success"); break;
        }

        return line;
    }

    private void ApplyWrap()
    {
        var wrap = _wrap.IsChecked == true;

        _list.Classes.Set("wrap", wrap);
        _output.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    private void ScrollToEndWhenLocked()
    {
        if (_scrollToEnd.IsChecked != true || _scrollPending) return;

        // One scroll per frame however many lines arrived in it, after layout
        // has made room for them.
        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            _output.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    /// <summary>The output when there is some, the message when not.</summary>
    private void ShowWhicheverFits() =>
        _body.Content = _viewModel.BuildOutput.Count == 0 ? _empty : _list;

    /// <summary>What the panel is showing, for the tests.</summary>
    internal bool IsShowingEmptyState => ReferenceEquals(_body.Content, _empty);

    internal EmptyState EmptyState => _empty;

    internal ItemsControl Lines => _list;
    internal ScrollViewer Scroller => _output;
    internal ToggleButton WrapToggle => _wrap;
    internal ToggleButton ScrollToEndToggle => _scrollToEnd;
    internal FontFamily Font => _font;
}
