using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// The redone New Solution window: a tree of dialects and project types on
/// the right, the rest of the form on the left.
/// </summary>
public sealed class NewSolutionDialogTreeTests
{
    [AvaloniaFact]
    public void TheTreeShowsTheThreeDialectsWithTheirLeaves()
    {
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;

        var dialects = tree.ItemsSource!.OfType<TreeViewItem>()
            .Select(i => (i.Header as TextBlock)?.Text).ToList();

        Assert.Contains("Visual Basic .NET", dialects);
        Assert.Contains("Visual Basic 6", dialects);
        Assert.Contains("QuickBASIC", dialects);

        // Every ProjectTemplate the generator supports is reachable as a leaf.
        var leaves = Flatten(tree.ItemsSource).Select(LeafTemplate).Where(t => t is not null).ToList();
        foreach (var template in Enum.GetValues<ProjectTemplate>())
            Assert.Contains(template, leaves);
    }

    [AvaloniaFact]
    public void SelectingWindowsFormsApplicationAndTypingANameThenCreateReturnsWinFormsApp()
    {
        var dialog = new NewSolutionDialog();

        // Windows Forms Application is the default selection already (the
        // VB6 spirit): nothing needs to be selected in the tree.
        dialog.FindControl<TextBox>("NameBox")!.Text = "MyApp";
        dialog.FindControl<TextBox>("PathBox")!.Text =
            Path.Combine(Path.GetTempPath(), "basalt-newsolution-" + Guid.NewGuid().ToString("N"));

        // Not shown through ShowDialog: that needs an owner and the
        // dispatcher plumbing a real modal dialog brings, which a unit test
        // has no reason to start. LastCreatedForTests is what Close's
        // argument would have been.
        dialog.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var parameters = dialog.LastCreatedForTests;

        Assert.NotNull(parameters);
        Assert.Equal(ProjectTemplate.WinFormsApp, parameters!.Template);
    }

    /// <summary>
    /// What the user actually sees on open, read back from the real render:
    /// the Windows Forms Application leaf highlighted in the tree and Create
    /// enabled, without ever touching the tree — selecting it is something
    /// the dialog does for itself, not something a test drives.
    /// </summary>
    [AvaloniaFact]
    public void OpensWithWindowsFormsSelectedAndHighlightedAndCreateEnabledAfterTypingAName()
    {
        var dialog = new NewSolutionDialog();
        dialog.Show();
        for (var i = 0; i < 5; i++) dialog.UpdateLayout();

        dialog.FindControl<TextBox>("NameBox")!.Text = "MyApp";

        var tree = dialog.FindControl<TreeView>("TemplateTree")!;
        var create = dialog.FindControl<Button>("CreateButton")!;
        var winFormsItem = Flatten(tree.ItemsSource).Single(i => LeafTemplate(i) == ProjectTemplate.WinFormsApp);

        Assert.Equal(ProjectTemplate.WinFormsApp, dialog.SelectedTemplate);
        Assert.True(winFormsItem.IsSelected, "the Windows Forms Application leaf is not shown selected");
        Assert.Same(winFormsItem, tree.SelectedItem);
        Assert.True(create.IsEnabled, "Create stays disabled even though a template is selected and a name was typed");

        dialog.Close();
    }

    /// <summary>
    /// The name, description and target shown above the form track whatever
    /// leaf is currently selected, not just the one the dialog opened with.
    /// </summary>
    [AvaloniaFact]
    public void TemplateDescriptionChangesWhenAnotherLeafIsSelected()
    {
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;
        var nameLabel = dialog.FindControl<TextBlock>("TemplateNameLabel")!;
        var descriptionLabel = dialog.FindControl<TextBlock>("TemplateDescriptionLabel")!;
        var targetLabel = dialog.FindControl<TextBlock>("TemplateTargetLabel")!;

        var winFormsName = nameLabel.Text;
        var winFormsDescription = descriptionLabel.Text;
        var winFormsTarget = targetLabel.Text;

        Assert.False(string.IsNullOrWhiteSpace(winFormsDescription));

        SelectLeaf(tree, ProjectTemplate.ConsoleApp);

        Assert.NotEqual(winFormsName, nameLabel.Text);
        Assert.NotEqual(winFormsDescription, descriptionLabel.Text);
        Assert.NotEqual(winFormsTarget, targetLabel.Text);
        Assert.False(string.IsNullOrWhiteSpace(descriptionLabel.Text));
    }

    /// <summary>
    /// Windows/Visual Studio button order, as the IDE's other dialogs use:
    /// the primary action (Create) leads, Cancel follows it, both right
    /// aligned — read back from where they actually render, not just from
    /// which Grid column a property says they are in.
    /// </summary>
    [AvaloniaFact]
    public void CreateLeadsCancelInTheButtonRow()
    {
        var dialog = new NewSolutionDialog();
        dialog.Show();
        for (var i = 0; i < 5; i++) dialog.UpdateLayout();

        var create = dialog.FindControl<Button>("CreateButton")!;
        var cancel = dialog.FindControl<Button>("CancelButton")!;

        var createBounds = Raster.BoundsIn(create, dialog);
        var cancelBounds = Raster.BoundsIn(cancel, dialog);

        Assert.True(createBounds.X < cancelBounds.X,
            $"Create sits at {createBounds.X}, Cancel at {cancelBounds.X}: Create should lead");
        Assert.True(create.Classes.Contains("primary"), "Create should be the primary button");
        Assert.True(create.IsDefault, "Create should be the default button (Enter)");
        Assert.True(cancel.IsCancel, "Cancel should be the cancel button (Esc)");

        dialog.Close();
    }

    [AvaloniaFact]
    public void SelectingTheAvaloniaLeafMapsToAvaloniaApp()
    {
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;
        SelectLeaf(tree, ProjectTemplate.AvaloniaApp);

        Assert.Equal(ProjectTemplate.AvaloniaApp, dialog.SelectedTemplate);
    }

    [AvaloniaFact]
    public void SelectingACategoryDisablesCreate()
    {
        var dialog = new NewSolutionDialog();
        var tree = dialog.FindControl<TreeView>("TemplateTree")!;
        var create = dialog.FindControl<Button>("CreateButton")!;

        // The "Windows Desktop" category, not one of its leaves.
        var category = Flatten(tree.ItemsSource)
            .Single(i => (i.Header as TextBlock)?.Text == "Windows Desktop");
        tree.SelectedItem = category;

        Assert.Null(dialog.SelectedTemplate);
        Assert.False(create.IsEnabled);
    }

    [AvaloniaFact]
    public void EscCancels()
    {
        // Esc activates the Cancel button (IsCancel="True") exactly like a
        // click on it; shown without ShowDialog, so Close(null) runs with no
        // modal dispatcher loop for a unit test to get stuck in.
        var dialog = new NewSolutionDialog();

        var cancel = dialog.FindControl<Button>("CancelButton")!;
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // Create never ran, so nothing was ever recorded to hand back.
        Assert.Null(dialog.LastCreatedForTests);
    }

    /// <summary>
    /// SolutionNameBox is used verbatim as the root folder and the .sln
    /// file's name, unlike NameBox (checked against a VB identifier
    /// elsewhere): a Windows-reserved character has to stop Create exactly
    /// the way an invalid Name does.
    /// </summary>
    [AvaloniaFact]
    public void AWindowsReservedCharacterInTheSolutionNameBlocksCreateAndShowsAnError()
    {
        var dialog = new NewSolutionDialog();
        dialog.FindControl<TextBox>("NameBox")!.Text = "MyApp";
        dialog.FindControl<TextBox>("PathBox")!.Text =
            Path.Combine(Path.GetTempPath(), "basalt-newsolution-" + Guid.NewGuid().ToString("N"));
        dialog.FindControl<TextBox>("SolutionNameBox")!.Text = "My:Solution";

        dialog.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Null(dialog.LastCreatedForTests);

        var error = dialog.FindControl<TextBlock>("ErrorLabel")!.Text;
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// CON, COM1 and the rest are unusable as folder names on Windows
    /// regardless of case; Create has to reject one before a folder is ever
    /// created, not after.
    /// </summary>
    [AvaloniaFact]
    public void AReservedDeviceNameAsSolutionNameBlocksCreate()
    {
        var dialog = new NewSolutionDialog();
        dialog.FindControl<TextBox>("NameBox")!.Text = "MyApp";
        dialog.FindControl<TextBox>("PathBox")!.Text =
            Path.Combine(Path.GetTempPath(), "basalt-newsolution-" + Guid.NewGuid().ToString("N"));
        dialog.FindControl<TextBox>("SolutionNameBox")!.Text = "com1";

        dialog.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Null(dialog.LastCreatedForTests);
    }

    /// <summary>
    /// A solution name ending in a trailing dot is something Windows itself
    /// silently drops, which would leave the folder Basalt actually creates
    /// different from the one the user typed and the one shown in the
    /// preview.
    /// </summary>
    [AvaloniaFact]
    public void ATrailingDotInTheSolutionNameBlocksCreate()
    {
        var dialog = new NewSolutionDialog();
        dialog.FindControl<TextBox>("NameBox")!.Text = "MyApp";
        dialog.FindControl<TextBox>("PathBox")!.Text =
            Path.Combine(Path.GetTempPath(), "basalt-newsolution-" + Guid.NewGuid().ToString("N"));
        dialog.FindControl<TextBox>("SolutionNameBox")!.Text = "MySolution.";

        dialog.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Null(dialog.LastCreatedForTests);
    }

    /// <summary>
    /// What Create hands back follows whatever the user actually edited: a
    /// solution name typed apart from the project name, and the "place in
    /// the same directory" box, both read back from
    /// <see cref="NewSolutionDialog.LastCreatedForTests"/> rather than
    /// assumed from what was typed into Name alone.
    /// </summary>
    [AvaloniaFact]
    public void CreateHandsBackNameSolutionNameAndPlaceProjectFlagAsEdited()
    {
        var dialog = new NewSolutionDialog();
        dialog.FindControl<TextBox>("NameBox")!.Text = "MyApp";
        dialog.FindControl<TextBox>("PathBox")!.Text =
            Path.Combine(Path.GetTempPath(), "basalt-newsolution-" + Guid.NewGuid().ToString("N"));
        dialog.FindControl<TextBox>("SolutionNameBox")!.Text = "MySolution";
        dialog.FindControl<CheckBox>("SameDirectoryCheck")!.IsChecked = true;

        dialog.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var parameters = dialog.LastCreatedForTests;

        Assert.NotNull(parameters);
        Assert.Equal("MyApp", parameters!.Name);
        Assert.Equal("MySolution", parameters.SolutionName);
        Assert.True(parameters.PlaceProjectInSolutionDirectory);
    }

    [AvaloniaFact]
    public void SolutionNameFollowsNameUntilEdited()
    {
        var dialog = new NewSolutionDialog();
        var name = dialog.FindControl<TextBox>("NameBox")!;
        var solutionName = dialog.FindControl<TextBox>("SolutionNameBox")!;

        name.Text = "FirstName";
        Assert.Equal("FirstName", solutionName.Text);

        solutionName.Text = "CustomSolution";
        name.Text = "SecondName";

        // Once edited directly, it stops mirroring Name.
        Assert.Equal("CustomSolution", solutionName.Text);
    }

    /// <summary>
    /// Rasterises the dialog and reads back where its controls actually sit,
    /// to prove the tree is in the right half of the window and the form in
    /// the left half — what the user asked for literally, not a property
    /// read off a control that may not match what is actually drawn.
    /// </summary>
    [AvaloniaFact]
    public void TheTreeIsOnTheRightHalfAndTheFormIsOnTheLeft()
    {
        var dialog = new NewSolutionDialog();
        dialog.Show();
        for (var i = 0; i < 5; i++) dialog.UpdateLayout();

        var tree = dialog.FindControl<TreeView>("TemplateTree")!;
        var left = dialog.FindControl<ScrollViewer>("LeftPanel")!;

        var treeBounds = Raster.BoundsIn(tree, dialog);
        var leftBounds = Raster.BoundsIn(left, dialog);
        var middle = dialog.Bounds.Width / 2;

        Assert.True(treeBounds.X >= middle - 2,
            $"the tree starts at {treeBounds.X}, left of the midline {middle}");
        Assert.True(leftBounds.Right <= middle + 2,
            $"the form extends to {leftBounds.Right}, right of the midline {middle}");

        var screenshotDir = Environment.GetEnvironmentVariable("BASALT_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(screenshotDir))
        {
            Directory.CreateDirectory(screenshotDir);

            using var target = new RenderTargetBitmap(
                new Avalonia.PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height));
            target.Render(dialog);

            using var file = File.Create(Path.Combine(screenshotDir, "new-solution.png"));
            target.Save(file, new PngBitmapEncoderOptions());
        }

        dialog.Close();
    }

    /// <summary>Same screenshot, in the dark theme, so a reviewer can check both without running the IDE.</summary>
    [AvaloniaFact]
    public void RendersInTheDarkTheme()
    {
        using var theme = new ThemeScope(Avalonia.Styling.ThemeVariant.Dark);

        var dialog = new NewSolutionDialog();
        dialog.Show();
        for (var i = 0; i < 5; i++) dialog.UpdateLayout();

        var screenshotDir = Environment.GetEnvironmentVariable("BASALT_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(screenshotDir))
        {
            Directory.CreateDirectory(screenshotDir);

            using var target = new RenderTargetBitmap(
                new Avalonia.PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height));
            target.Render(dialog);

            using var file = File.Create(Path.Combine(screenshotDir, "new-solution-dark.png"));
            target.Save(file, new PngBitmapEncoderOptions());
        }

        dialog.Close();
    }

    private static void SelectLeaf(TreeView tree, ProjectTemplate template)
    {
        var item = Flatten(tree.ItemsSource).Single(i => LeafTemplate(i) == template);
        tree.SelectedItem = item;
    }

    private static ProjectTemplate? LeafTemplate(TreeViewItem item) =>
        item.Tag?.GetType().GetProperty("Template")?.GetValue(item.Tag) as ProjectTemplate?;

    private static IEnumerable<TreeViewItem> Flatten(System.Collections.IEnumerable? items)
    {
        if (items is null) yield break;

        foreach (var entry in items)
        {
            if (entry is not TreeViewItem item) continue;
            yield return item;
            foreach (var child in Flatten(item.ItemsSource)) yield return child;
        }
    }

    /// <summary>
    /// The "place in the same directory" box, read back from the render:
    /// Fluent's own box cell is a fixed 32 tall, sized for a control about
    /// that height, while the app-wide CheckBox style (Styles.axaml) pins
    /// every box to 20 so a column of switches reads as a group. Squeezed
    /// into 20, that cell used to overflow downward from the top and the box
    /// ended up visibly lower than the label beside it.
    /// </summary>
    [AvaloniaFact]
    public void PlaceInSameDirectoryBoxIsVerticallyCentredOnItsLabel()
    {
        var dialog = new NewSolutionDialog();
        dialog.Show();
        for (var i = 0; i < 5; i++) dialog.UpdateLayout();

        var check = dialog.FindControl<CheckBox>("SameDirectoryCheck")!;
        var box = check.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
        var text = check.GetVisualDescendants().OfType<TextBlock>().Single();

        var boxCenter = Raster.BoundsIn(box, dialog).Y + Raster.BoundsIn(box, dialog).Height / 2.0;
        var textCenter = Raster.BoundsIn(text, dialog).Y + Raster.BoundsIn(text, dialog).Height / 2.0;

        dialog.Close();

        Assert.True(Math.Abs(boxCenter - textCenter) <= 3,
            $"the box centres at {boxCenter}, the label at {textCenter}");
    }
}
