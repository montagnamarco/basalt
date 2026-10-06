using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Workspace.Publishing;

namespace Basalt.Shell;

/// <summary>What the user asked "Make executable" to produce.</summary>
public sealed record MakeExecutableChoiceResult(IReadOnlyList<PublishTarget> Targets, string OutputRoot);

/// <summary>
/// Asks for the platforms and the folder of "File > Make executable".
///
/// A plain list of check boxes rather than a combo: the same project is often
/// made for two or three platforms in one go, and Release, self-contained and
/// single-file are not questions, only a line saying what will happen.
/// </summary>
public sealed class MakeExecutableDialog : Window
{
    private readonly List<(PublishTarget Target, CheckBox Box)> _targets = [];
    private readonly TextBox _folder;
    private readonly TextBlock _error;

    public MakeExecutableDialog(string projectName, IReadOnlyCollection<string> selectedRids, string outputRoot)
    {
        Title = Localizer.Get(StringKeys.MakeExeTitle, projectName);
        Width = 480;
        MinWidth = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var list = new StackPanel { Spacing = Spacing.Tight };
        var current = PublishTarget.Current();
        var chosen = selectedRids.Count > 0 ? selectedRids : [current.Rid];

        foreach (var target in PublishTarget.All)
        {
            var box = new CheckBox
            {
                Content = TargetName(target),
                IsChecked = chosen.Contains(target.Rid, StringComparer.OrdinalIgnoreCase)
            };
            _targets.Add((target, box));
            list.Children.Add(box);
        }

        _folder = new TextBox { Text = outputRoot };
        var browse = new Button { Content = Localizer.Get(StringKeys.NewSolutionBrowse) };
        browse.Click += async (_, _) => await BrowseAsync();

        var folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Spacing.Normal };
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(_folder);
        folderRow.Children.Add(browse);

        _error = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed, IsVisible = false };

        var make = new Button { Content = Localizer.Get(StringKeys.MakeExeMake), IsDefault = true };
        var cancel = new Button { Content = Localizer.Get(StringKeys.MakeExeCancel), IsCancel = true };
        make.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Normal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, make }
        };

        Content = new StackPanel
        {
            Margin = Spacing.DialogPadding,
            Spacing = Spacing.Normal,
            Children =
            {
                new TextBlock { Text = Localizer.Get(StringKeys.MakeExeTargets), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                list,
                new TextBlock { Text = Localizer.Get(StringKeys.MakeExeOutputFolder), FontWeight = Avalonia.Media.FontWeight.SemiBold },
                folderRow,
                new TextBlock { Text = Localizer.Get(StringKeys.MakeExeOutputHint), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    .Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush"),
                new TextBlock { Text = Localizer.Get(StringKeys.MakeExeConfiguration), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    .Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush"),
                _error,
                buttons
            }
        };
    }

    /// <summary>The platform's display name, from the resources.</summary>
    internal static string TargetName(PublishTarget target) => Localizer.Get(target.Rid switch
    {
        "win-x64" => StringKeys.MakeExeTargetWinX64,
        "win-arm64" => StringKeys.MakeExeTargetWinArm64,
        "osx-arm64" => StringKeys.MakeExeTargetMacArm64,
        "osx-x64" => StringKeys.MakeExeTargetMacX64,
        "linux-x64" => StringKeys.MakeExeTargetLinuxX64,
        _ => StringKeys.MakeExeTargetLinuxArm64
    });

    private async Task BrowseAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _folder.Text = path;
    }

    private void Accept()
    {
        var targets = _targets.Where(t => t.Box.IsChecked == true).Select(t => t.Target).ToList();
        var folder = _folder.Text?.Trim() ?? "";

        if (targets.Count == 0 || folder.Length == 0)
        {
            _error.Text = Localizer.Get(StringKeys.MakeExeNeedTarget);
            _error.IsVisible = true;
            return;
        }

        Close(new MakeExecutableChoiceResult(targets, folder));
    }
}
