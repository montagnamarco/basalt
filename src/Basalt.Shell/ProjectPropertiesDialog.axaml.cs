using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Basalt.Shell.ViewModels;
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

    // The startup object: the list the compiler reads (started once per dialog, shared by every page built), and
    // the choice as the list shows it, which is what Save writes.
    private Task<StartupObjects>? _startupObjects;

    // Completes when the list has been offered, or the reason it could not be read has been shown.
    private Task _startupObjectsFilled = Task.CompletedTask;

    /// <summary>Completes once the startup object list has been offered in the page, for the tests to wait on.</summary>
    internal Task StartupObjectsFilled => _startupObjectsFilled;
    private string? _startupObjectNotSet;
    private string? _startupObjectChoice;
    private bool _startupObjectFilling;

    // The RootNamespace the page opened with (its text, empty when the project sets none), and the root the compiler
    // gave the listed names, which is the project name when none is set.
    private string? _openedRoot;
    private string? _listedRoot;
    private IReadOnlyList<string> _listedNames = [];

    // The Application Framework, when the project has one: then the list offers the forms, and the choice is the
    // form the framework opens, written to its file rather than to StartupObject.
    private ApplicationFramework? _framework;

    /// <summary>A fully qualified name without the root namespace in front of it.</summary>
    private static string RelativeTo(string name, string root) =>
        root.Length > 0 && name.StartsWith(root + ".", StringComparison.Ordinal) ? name[(root.Length + 1)..] : name;

    /// <summary>The name with the root namespace in front of it, as Visual Basic declares it.</summary>
    private static string QualifyWith(string relative, string root) =>
        root.Length == 0 ? relative : root + "." + relative;

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
            StartupObjectChoice(properties));
    }

    /// <summary>
    /// The type whose Main runs at startup, chosen from the types that have one rather than typed by hand.
    /// </summary>
    /// <remarks>
    /// The list is read by the compiler on the thread pool. The page shows the value the project has at once, and
    /// the candidates when they arrive. A value the list does not hold is kept in it, so that saving the page
    /// does not drop what the project already says.
    /// </remarks>
    private Control StartupObjectChoice(ProjectProperties properties)
    {
        _startupObjectNotSet ??= Localizer.Get(StringKeys.ProjectPropertiesStartupObjectNotSet);

        _framework ??= ApplicationFramework.For(properties.Path);
        _openedRoot ??= properties.RootNamespace;

        // The choice survives the page being built again, so a pick made before switching pages is not lost. With the
        // framework the choice is the form's name as the framework's file gives it; otherwise the StartupObject.
        _startupObjectChoice ??= _framework.InUse
            ? _framework.MainForm ?? _startupObjectNotSet
            : string.IsNullOrWhiteSpace(properties.StartupObject)
                ? _startupObjectNotSet
                : properties.StartupObject;

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) =>
        {
            if (!_startupObjectFilling && combo.SelectedItem is string chosen) _startupObjectChoice = chosen;
        };

        ShowStartupObjects(combo, []);
        _startupObjectsFilled = FillStartupObjectsAsync(combo);

        return Labelled(
            Localizer.Get(StringKeys.ProjectPropertiesStartupObject), combo,
            Localizer.Get(StringKeys.ProjectPropertiesStartupObjectHelp));
    }

    /// <summary>Reads the candidates once per dialog, and offers them in the combo box when they are read.</summary>
    private async Task FillStartupObjectsAsync(ComboBox combo)
    {
        try
        {
            _startupObjects ??= StartupObjectCatalog.ReadAsync(_properties!.Path);
            var listed = await _startupObjects.ConfigureAwait(true);
            _listedRoot = listed.RootNamespace;
            _listedNames = listed.Names;

            // With the framework the forms are offered, named as the framework's file names them.
            ShowStartupObjects(combo, _framework is { InUse: true }
                ? listed.Forms.Select(form => RelativeTo(form, listed.RootNamespace)).ToList()
                : listed.Names);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The choice still shows, so the page is usable; the reason says why there is no list.
            StatusLabel.Text = ex.Message;
        }
    }

    private void ShowStartupObjects(ComboBox combo, IReadOnlyList<string> found)
    {
        var choice = _startupObjectChoice!;

        var items = new List<string> { _startupObjectNotSet! };
        items.AddRange(found);
        if (!items.Contains(choice)) items.Add(choice);

        // Replacing the items moves the selection, which is not the user's choice and must not be recorded as one.
        _startupObjectFilling = true;
        try
        {
            combo.ItemsSource = items;
            combo.SelectedItem = choice;
        }
        finally
        {
            _startupObjectFilling = false;
        }
    }

    /// <summary>Writes the chosen startup object; "(Not set)" removes the element.</summary>
    private void CommitStartupObject()
    {
        if (_properties is null || _startupObjectChoice is null) return;

        // With the Application Framework the startup object is the framework's own class, and the form goes to its
        // files, which are written after the project is saved (see SaveFrameworkFormAsync).
        if (_framework is { InUse: true }) return;

        string? value = _startupObjectChoice == _startupObjectNotSet ? null : _startupObjectChoice;

        // Only a value that is one of the listed types is renamed, and only when the root was changed in this dialog:
        // the root it was listed with is swapped for the root being saved. Anything else is written as it stands.
        if (value is not null
            && _listedRoot is { } listedRoot
            && _listedNames.Contains(value)
            && _properties.RootNamespace != _openedRoot)
        {
            value = QualifyWith(RelativeTo(value, listedRoot), _properties.RootNamespace);
        }

        _properties.StartupObject = value;
    }

    /// <summary>
    /// Writes the form the Application Framework opens, to Application.myapp and to the designer file that names it in
    /// code. Called only once the project is saved, so a project that failed to save leaves the framework as it was.
    /// </summary>
    private async Task SaveFrameworkFormAsync()
    {
        if (_framework is not { InUse: true } framework || _startupObjectChoice is null) return;

        // The designer names the form with the root namespace the list was read with. A list that failed to load was
        // reported when it failed, and the root the page opened with is then the best there is.
        if (_startupObjects is not null)
        {
            try { await _startupObjects.ConfigureAwait(true); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }

        framework.SaveMainForm(
            _startupObjectChoice == _startupObjectNotSet ? null : _startupObjectChoice,
            _listedRoot ?? _openedRoot ?? "");
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
        // Only a changed value is written: an editor the person did not touch must leave the file as it was, and a
        // value the editor shows (such as the assembly name the project defaults to) is not written back into it.
        // Compared with the last value applied, not the opening one: a change followed by a return to the opening
        // value must write that value back, or the file keeps the change.
        var applied = value;
        Action commit = () =>
        {
            var text = box.Text ?? "";
            if (text == applied) return;

            applied = text;
            apply(text);
        };
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
            CommitStartupObject();

            await _properties.SaveAsync();
            await _launch.SaveAsync();

            // The framework's files change only once the project has been saved.
            await SaveFrameworkFormAsync();

            // The window that opened the dialog is asked to bring the project up to date. The owner is taken before
            // the close, and the work runs after it, so the window is not held while the solution is read again.
            var owner = Owner as MainWindow;
            Close(true);

            if (owner is not null) RefreshAfterSave(owner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusLabel.Text = ex.Message;
        }
    }

    /// <summary>
    /// Brings the solution's projects up to date with the file just saved: the language service, the Problems
    /// panel, the projects offered to run, and the startup project list of the toolbar.
    /// </summary>
    private static void RefreshAfterSave(MainWindow owner)
    {
        if (owner.DataContext is not MainWindowViewModel viewModel) return;

        Guarded.Run(async () =>
        {
            await viewModel.ProjectPropertiesSavedAsync().ConfigureAwait(true);
            owner.RefreshToolbar();
        }, viewModel.WriteOutput, "properties");
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
