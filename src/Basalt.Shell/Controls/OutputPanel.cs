using Avalonia.Controls;
using Basalt.Core.Localization;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>Textual output of builds and program runs.</summary>
public sealed class OutputPanel : UserControl
{
    public OutputPanel(MainWindowViewModel viewModel)
    {
        var list = new ItemsControl
        {
            [!ItemsControl.ItemsSourceProperty] = new Binding(nameof(MainWindowViewModel.BuildOutput)),
            Margin = new Avalonia.Thickness(8, 4),
            ItemTemplate = new FuncDataTemplate<string>((row, _) => new TextBlock
            {
                Text = row,
                FontFamily = new FontFamily("Menlo,Consolas,DejaVu Sans Mono,monospace"),
                FontSize = 12
            })
        };

        // Before the first build there is nothing to show, and an empty panel
        // gives no hint whether the build ran and said nothing or never ran.
        _empty = new TextBlock
        {
            Text = Localizer.Get(StringKeys.OutputEmpty),
            Opacity = 0.6,
            FontSize = 12,
            Margin = new Avalonia.Thickness(12, 8, 8, 8)
        };

        _output = new ScrollViewer { Content = list };

        DataContext = viewModel;

        viewModel.BuildOutput.CollectionChanged += (_, _) => ShowWhicheverFits();

        ShowWhicheverFits();
    }

    private readonly ScrollViewer _output;
    private readonly TextBlock _empty;

    /// <summary>The output when there is some, the message when not.</summary>
    private void ShowWhicheverFits() =>
        Content = ((MainWindowViewModel)DataContext!).BuildOutput.Count == 0
            ? _empty
            : _output;

    /// <summary>What the panel is showing, for the tests.</summary>
    internal bool IsShowingEmptyState => ReferenceEquals(Content, _empty);
}
