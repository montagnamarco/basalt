using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Basalt.Workspace.Projects;
using Basalt.Core.Localization;

namespace Basalt.Shell;

/// <summary>
/// Editing a project's settings without opening the project file by hand.
///
/// Pages are built in code rather than declared per category: each one is a
/// list of labelled editors bound to a property, and the shape repeats.
/// </summary>
public partial class ProjectPropertiesDialog : Window
{
    private readonly ProjectProperties? _properties;
    private readonly LaunchSettings? _launch;

    /// <summary>Parameterless constructor for the XAML previewer and tests.</summary>
    public ProjectPropertiesDialog() : this(null, null) { }

    public ProjectPropertiesDialog(ProjectProperties? properties, LaunchSettings? launch)
    {
        InitializeComponent();

        _properties = properties;
        _launch = launch;

        if (properties is not null)
        {
            ProjectNameLabel.Text = Path.GetFileNameWithoutExtension(properties.Path);
            ProjectPathLabel.Text = properties.Path;
        }

        ShowPage(0);

        // Focused when the dialog opens: the category list is where a user
        // starts, and without this the first keystroke goes nowhere.
        Opened += (_, _) => CategoryList.Focus();
    }

    public static ProjectPropertiesDialog For(string projectPath) =>
        new(ProjectProperties.Load(projectPath), LaunchSettings.Load(projectPath));

    /// <summary>
    /// Switches page when a category is picked.
    ///
    /// The index comes from the sender rather than the named field: the list's
    /// initial selection raises this before InitializeComponent has finished
    /// assigning the fields, so the field is still null at that point.
    /// </summary>
    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list) ShowPage(list.SelectedIndex);
    }

    private void ShowPage(int index)
    {
        if (_properties is null || _launch is null) return;

        PageHost.Content = index switch
        {
            1 => BuildCompilePage(),
            2 => BuildAdvancedPage(),
            3 => BuildReferencesPage(),
            4 => BuildDebugPage(),
            5 => BuildPublishPage(),
            _ => BuildApplicationPage()
        };
    }

    // Pages

    private Control BuildApplicationPage()
    {
        var properties = _properties!;

        return Page(
            Text(Localizer.Get(StringKeys.ProjectPropertiesAssemblyName), properties.AssemblyName, v => properties.AssemblyName = v),
            Text(Localizer.Get(StringKeys.ProjectPropertiesRootNamespace), properties.RootNamespace, v => properties.RootNamespace = v,
                 Localizer.Get(StringKeys.ProjectPropertiesRootNamespaceHelp)),
            Choice(Localizer.Get(StringKeys.ProjectPropertiesOutputType),
                   Enum.GetNames<ProjectOutputType>(),
                   properties.OutputType.ToString(),
                   v => properties.OutputType = Enum.Parse<ProjectOutputType>(v)),
            Text(Localizer.Get(StringKeys.ProjectPropertiesStartupObject), properties.StartupObject ?? "",
                 v => properties.StartupObject = v,
                 Localizer.Get(StringKeys.ProjectPropertiesStartupObjectHelp)));
    }

    private Control BuildCompilePage()
    {
        var properties = _properties!;

        return Page(
            Text(Localizer.Get(StringKeys.ProjectPropertiesTargetFramework), properties.TargetFramework,
                 v => properties.TargetFramework = v),
            Choice(Localizer.Get(StringKeys.ProjectPropertiesOptionStrict),
                   Enum.GetNames<OptionStrict>(),
                   properties.OptionStrict.ToString(),
                   v => properties.OptionStrict = Enum.Parse<OptionStrict>(v),
                   Localizer.Get(StringKeys.ProjectPropertiesOptionStrictHelp)),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesOptionExplicit), properties.OptionExplicit,
                   v => properties.OptionExplicit = v,
                   Localizer.Get(StringKeys.ProjectPropertiesOptionExplicitHelp)),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesOptionInfer), properties.OptionInfer,
                   v => properties.OptionInfer = v,
                   Localizer.Get(StringKeys.ProjectPropertiesOptionInferHelp)),
            Choice(Localizer.Get(StringKeys.ProjectPropertiesOptionCompare),
                   Enum.GetNames<OptionCompare>(),
                   properties.OptionCompare.ToString(),
                   v => properties.OptionCompare = Enum.Parse<OptionCompare>(v),
                   Localizer.Get(StringKeys.ProjectPropertiesOptionCompareHelp)));
    }

    private Control BuildAdvancedPage()
    {
        var properties = _properties!;

        return Page(
            Text(Localizer.Get(StringKeys.ProjectPropertiesConditionalConstants), properties.DefineConstants ?? "",
                 v => properties.DefineConstants = v,
                 Localizer.Get(StringKeys.ProjectPropertiesConditionalConstantsHelp)),
            Text(Localizer.Get(StringKeys.ProjectPropertiesSuppressedWarnings), properties.NoWarn ?? "",
                 v => properties.NoWarn = v,
                 Localizer.Get(StringKeys.ProjectPropertiesSuppressedWarningsHelp)),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesTreatWarningsAsErrors), properties.TreatWarningsAsErrors,
                   v => properties.TreatWarningsAsErrors = v),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesGenerateXmlDocumentation), properties.GenerateDocumentationFile,
                   v => properties.GenerateDocumentationFile = v),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesNullable), properties.Nullable,
                   v => properties.Nullable = v));
    }

    private Control BuildReferencesPage()
    {
        var properties = _properties!;
        var panel = new StackPanel { Spacing = 16 };

        panel.Children.Add(Heading(Localizer.Get(StringKeys.ProjectPropertiesPackages)));

        var packages = new ListBox
        {
            Height = 150,
            ItemsSource = properties.PackageReferences
                .Select(p => $"{p.Id}  {p.Version}")
                .ToList()
        };
        panel.Children.Add(packages);

        var packageRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,Auto,Auto") };
        var packageId = new TextBox { PlaceholderText = Localizer.Get(StringKeys.ProjectPropertiesPackageId) };
        var packageVersion = new TextBox { PlaceholderText = Localizer.Get(StringKeys.ProjectPropertiesVersion), Margin = new Avalonia.Thickness(8, 0, 8, 0) };
        var add = new Button { Content = Localizer.Get(StringKeys.ProjectPropertiesAddPackage) };

        // Under central package management the version lives in Directory.Packages.props,
        // so a blank version is the normal case and is not asked for.
        var centrally = ReferencesDialog.DetectsCentralPackageManagement(properties.Path, properties.ToXml());
        var packageMessage = new TextBlock { IsVisible = false, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var remove = new Button { Content = Localizer.Get(StringKeys.ProjectPropertiesRemovePackage), Margin = new Avalonia.Thickness(8, 0, 0, 0) };

        Grid.SetColumn(packageVersion, 1);
        Grid.SetColumn(add, 2);
        Grid.SetColumn(remove, 3);
        packageRow.Children.Add(packageId);
        packageRow.Children.Add(packageVersion);
        packageRow.Children.Add(add);
        packageRow.Children.Add(remove);

        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(packageId.Text)) return;

            // A package needs a version to be pinned; a blank one is not written
            // as a floating "*", and the user is told why.
            if (!centrally && string.IsNullOrWhiteSpace(packageVersion.Text))
            {
                packageMessage.Text = Localizer.Get(StringKeys.PackageVersionRequired);
                packageMessage.IsVisible = true;
                return;
            }

            packageMessage.IsVisible = false;
            properties.AddPackage(packageId.Text!.Trim(), (packageVersion.Text ?? "").Trim(), centrally);

            packages.ItemsSource = properties.PackageReferences
                .Select(p => $"{p.Id}  {p.Version}").ToList();
            packageId.Text = packageVersion.Text = "";
        };

        remove.Click += (_, _) =>
        {
            if (packages.SelectedItem is not string selected) return;

            properties.RemovePackage(selected.Split("  ")[0]);
            packages.ItemsSource = properties.PackageReferences
                .Select(p => $"{p.Id}  {p.Version}").ToList();
        };

        panel.Children.Add(packageRow);
        panel.Children.Add(packageMessage);

        panel.Children.Add(Heading(Localizer.Get(StringKeys.ProjectPropertiesProjects)));
        panel.Children.Add(new ListBox
        {
            Height = 120,
            ItemsSource = properties.ProjectReferences.ToList()
        });

        return panel;
    }

    private Control BuildDebugPage()
    {
        var launch = _launch!;

        return Page(
            Text(Localizer.Get(StringKeys.ProjectPropertiesCommandLineArguments), launch.CommandLineArguments ?? "",
                 v => launch.CommandLineArguments = v),
            Text(Localizer.Get(StringKeys.ProjectPropertiesWorkingDirectory), launch.WorkingDirectory ?? "",
                 v => launch.WorkingDirectory = v),
            Text(Localizer.Get(StringKeys.ProjectPropertiesApplicationUrl), launch.ApplicationUrl ?? "",
                 v => launch.ApplicationUrl = v,
                 Localizer.Get(StringKeys.ProjectPropertiesApplicationUrlHelp)),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesLaunchBrowser), launch.LaunchBrowser, v => launch.LaunchBrowser = v),
            Text(Localizer.Get(StringKeys.ProjectPropertiesEnvironmentVariables),
                 string.Join("\n", launch.EnvironmentVariables.Select(p => $"{p.Key}={p.Value}")),
                 ApplyEnvironmentVariables,
                 Localizer.Get(StringKeys.ProjectPropertiesEnvironmentVariablesHelp),
                 multiline: true));
    }

    private void ApplyEnvironmentVariables(string text)
    {
        var launch = _launch!;

        // Rewritten wholesale: the box shows the complete set, so anything no
        // longer listed has been deleted by the user.
        foreach (var name in launch.EnvironmentVariables.Keys.ToList())
            launch.SetEnvironmentVariable(name, null);

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            launch.SetEnvironmentVariable(
                line[..separator].Trim(), line[(separator + 1)..].Trim());
        }
    }

    private Control BuildPublishPage()
    {
        var properties = _properties!;

        return Page(
            Text(Localizer.Get(StringKeys.ProjectPropertiesRuntimeIdentifier), properties.RuntimeIdentifier ?? "",
                 v => properties.RuntimeIdentifier = v,
                 Localizer.Get(StringKeys.ProjectPropertiesRuntimeIdentifierHelp)),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesSelfContained), properties.SelfContained,
                   v => properties.SelfContained = v,
                   Localizer.Get(StringKeys.ProjectPropertiesSelfContainedHelp)),
            Switch(Localizer.Get(StringKeys.ProjectPropertiesTrimUnusedCode), properties.PublishTrimmed,
                   v => properties.PublishTrimmed = v));
    }

    // Editors

    private static Control Page(params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 16 };
        foreach (var row in rows) panel.Children.Add(row);
        return panel;
    }

    private static Control Heading(string text) => new TextBlock
    {
        Text = text,
        FontWeight = Avalonia.Media.FontWeight.SemiBold,
        Margin = new Avalonia.Thickness(0, 6, 0, 0)
    };

    private static Control Text(
        string label, string value, Action<string> apply,
        string? hint = null, bool multiline = false)
    {
        var box = new TextBox
        {
            Text = value,
            AcceptsReturn = multiline,
            Height = multiline ? 110 : double.NaN,
            TextWrapping = multiline
                ? Avalonia.Media.TextWrapping.Wrap
                : Avalonia.Media.TextWrapping.NoWrap
        };

        // Applied when the box loses focus, so a half-typed value is never
        // written to the project file.
        Action commit = () => apply(box.Text ?? "");
        box.LostFocus += (_, _) => commit();

        // Save commits every box too, whether or not it still holds the focus
        // (Enter on the default button leaves the focus where it was).
        box.Tag = commit;

        return Labelled(label, box, hint);
    }

    private static Control Choice(
        string label, IReadOnlyList<string> options, string value, Action<string> apply,
        string? hint = null)
    {
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = value,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string chosen) apply(chosen);
        };

        return Labelled(label, combo, hint);
    }

    private static Control Switch(
        string label, bool value, Action<bool> apply, string? hint = null)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        box.IsCheckedChanged += (_, _) => apply(box.IsChecked == true);

        if (hint is null) return box;

        return new StackPanel
        {
            Spacing = 2,
            Children = { box, Hint(hint) }
        };
    }

    private static Control Labelled(string label, Control editor, string? hint)
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(editor);

        if (hint is not null) panel.Children.Add(Hint(hint));

        return panel;
    }

    private static Control Hint(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.7,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_properties is null || _launch is null)
        {
            Close(false);
            return;
        }

        try
        {
            // Focus is moved first so an editor still holding a change applies
            // it before the file is written.
            SaveFocusedEditor();
            CommitEditors();

            await _properties.SaveAsync();
            await _launch.SaveAsync();

            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    /// <summary>
    /// Commits whatever the focused editor holds.
    ///
    /// Editors apply on losing focus, and clicking Save does not move focus
    /// away from them.
    /// </summary>
    private void SaveFocusedEditor()
    {
        if (FocusManager?.GetFocusedElement() is Control focused)
            focused.Focus();

        // PageHost is a ContentControl and cannot take focus; the category list can,
        // and moving focus there is what raises LostFocus on the box.
        this.FindControl<ListBox>("CategoryList")?.Focus();
    }

    private void CommitEditors()
    {
        foreach (var box in PageHost.GetVisualDescendants().OfType<TextBox>())
            (box.Tag as Action)?.Invoke();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
