using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The file icons on a selected row of a tree, read from the pixels.
///
/// A focused selection is filled with the active selection colour, and its
/// icon kept the file-type accent: the .vb icon measured 1.49:1 on the Light
/// selection and the .cs icon 1.19:1 on the Dark one. Visual Studio draws the
/// icon in the selection's foreground there, so the icon takes it. Unselected
/// icons keep their accent hue, the files nested under a selected folder keep
/// theirs, and the accents the tree draws reach 3:1 on the list surface of
/// every palette.
/// </summary>
public sealed class TreeIconSelectionTests : IDisposable
{
    /// <summary>How far an icon's hue may drift from its accent, in degrees: a blend with a neutral surface keeps it.</summary>
    private const double HueTolerance = 4;

    private readonly string _root = Directory.CreateTempSubdirectory("basalt-treeicon-").FullName;

    public TreeIconSelectionTests()
    {
        File.WriteAllText(Path.Combine(_root, "Probe.vbproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module M\nEnd Module\n");
        File.WriteAllText(Path.Combine(_root, "Helper.cs"), "class Helper { }\n");

        // Three levels down: a file in a folder in the project, as deep as Solution > Project > file.
        Directory.CreateDirectory(Path.Combine(_root, "Sub"));
        File.WriteAllText(Path.Combine(_root, "Sub", "Deep.vb"), "Module D\nEnd Module\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // The Solution Explorer, rasterised.
    // ------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("Light", "Program.vb")]
    [InlineData("Light", "Helper.cs")]
    [InlineData("Dark", "Program.vb")]
    [InlineData("Dark", "Helper.cs")]
    [InlineData("Light", "Deep.vb")]
    [InlineData("Dark", "Deep.vb")]
    public async Task TheIconOfAFocusedSelectedRowReadsAgainstItsSelection(string theme, string fileName)
    {
        var variant = Variant(theme);
        using var scope = new ThemeScope(variant);
        var shown = Open(variant);

        try
        {
            var node = FileNode(shown.Vm, fileName);
            shown.Vm.Explorer.SelectedNode = node;
            await SettleAsync(shown.Window);

            var row = RowFor(shown.Panel.TreeForTests, node);
            await FocusAsync(row, shown.Window);

            var raster = Raster.Of(shown.Surface);
            var selection = raster.At(Raster.BoundsIn(LayoutRootOf(row), shown.Surface).Right - 2, RowCentreY(row, shown.Surface));
            var expected = Token(variant, "ListActiveSelectionBackground");

            Assert.True(Raster.Near(selection, expected),
                $"{fileName} in {theme}: the focused selected row is painted {selection}, not the selection {expected}.");

            var contrast = raster.BestContrastAgainst(Raster.BoundsIn(IconOf(row), shown.Surface), selection);

            Assert.True(contrast >= 3.0,
                $"{fileName} in {theme}: its icon reaches only {contrast:0.00}:1 on the focused selection {selection}.");
        }
        finally { shown.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task UnselectedIconsKeepTheirAccentHue(string theme)
    {
        var variant = Variant(theme);
        using var scope = new ThemeScope(variant);
        var shown = Open(variant);

        try
        {
            // Nothing selected: every icon is on a plain row. The focus is on the project's row, which is not selected either.
            await SettleAsync(shown.Window);
            await FocusAsync(RowFor(shown.Panel.TreeForTests, shown.Vm.Explorer.Roots[0]), shown.Window);

            AssertAccentHue(shown, theme, FileNode(shown.Vm, "Program.vb"), IconKind.VisualBasicFile);
            AssertAccentHue(shown, theme, FileNode(shown.Vm, "Helper.cs"), IconKind.CSharpFile);
            AssertAccentHue(shown, theme, shown.Vm.Explorer.Roots[0], IconKind.Project);
        }
        finally { shown.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task TheFilesUnderAFocusedSelectedFolderKeepTheirAccents(string theme)
    {
        // The project is selected, so the files nested under it must not be. A rule that whitens a selected
        // row's icon reaches down into the rows below it, and these are the rows it would reach.
        var variant = Variant(theme);
        using var scope = new ThemeScope(variant);
        var shown = Open(variant);

        try
        {
            var project = shown.Vm.Explorer.Roots[0];
            shown.Vm.Explorer.SelectedNode = project;
            await SettleAsync(shown.Window);
            await FocusAsync(RowFor(shown.Panel.TreeForTests, project), shown.Window);

            AssertAccentHue(shown, theme, FileNode(shown.Vm, "Program.vb"), IconKind.VisualBasicFile);
            AssertAccentHue(shown, theme, FileNode(shown.Vm, "Helper.cs"), IconKind.CSharpFile);
        }
        finally { shown.Window.Close(); }
    }

    // ------------------------------------------------------------------
    // The same rule for every tree and list that draws an icon in a row.
    // ------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATreeOrListRowWithAnIconTakesTheSelectionForegroundWhileFocused(bool list)
    {
        using var scope = new ThemeScope(ThemeVariant.Dark);

        var icon = new IconView { Kind = IconKind.CSharpFile, IconSize = 14, Classes = { "rowIcon" }, [RowIcon.FollowsSelectionProperty] = true, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { icon, new TextBlock { Text = "Helper.cs" } }
        };

        Control host;
        Control row;

        if (list)
        {
            var listBox = new ListBox { Background = Brushes.Transparent, BorderThickness = default };
            var item = new ListBoxItem { Content = content };
            listBox.Items.Add(item);
            listBox.SelectedItem = item;
            host = listBox;
            row = item;
        }
        else
        {
            var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = default };
            var item = new TreeViewItem { Header = content };
            tree.Items.Add(item);
            tree.SelectedItem = item;
            host = tree;
            row = item;
        }

        var surface = new Border { Width = 240, Height = 120, Background = new SolidColorBrush(Token(ThemeVariant.Dark, "SideBarBackground")), Child = host };
        var window = new Window { Content = surface, Width = 240, Height = 120 };

        try
        {
            window.Show();
            await SettleAsync(window);

            // The row takes the focus, as a click on it does; the tree and list themselves are not focusable.
            await FocusAsync(row, window);

            var raster = Raster.Of(surface);

            // The list draws a row's fill on its content presenter, the tree on its layout root.
            var iconBox = Raster.BoundsIn(icon, surface);
            var rowName = list ? "PART_ContentPresenter" : "PART_LayoutRoot";
            var rowRoot = row.GetVisualDescendants().OfType<Control>().First(c => c.Name == rowName);
            var rowBox = Raster.BoundsIn(rowRoot, surface);
            var selection = raster.At(rowBox.Right - 2, (int)rowBox.Center.Y);

            Assert.True(Raster.Near(selection, Token(ThemeVariant.Dark, "ListActiveSelectionBackground")),
                $"{(list ? "list" : "tree")} row: the focused selected row is painted {selection}.");

            var contrast = raster.BestContrastAgainst(iconBox, selection);

            Assert.True(contrast >= 3.0,
                $"{(list ? "list" : "tree")} row: its icon reaches only {contrast:0.00}:1 on the focused selection {selection}.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task AnUnfocusedSelectedRowKeepsItsAccent(string theme)
    {
        // Selected but not focused: the row is filled with the quieter inactive selection, and its icon is not whitened.
        var variant = Variant(theme);
        using var scope = new ThemeScope(variant);
        var shown = Open(variant);

        try
        {
            var node = FileNode(shown.Vm, "Helper.cs");
            shown.Vm.Explorer.SelectedNode = node;
            await SettleAsync(shown.Window);

            var row = RowFor(shown.Panel.TreeForTests, node);
            var raster = Raster.Of(shown.Surface);
            var selection = raster.At(Raster.BoundsIn(LayoutRootOf(row), shown.Surface).Right - 2, RowCentreY(row, shown.Surface));

            Assert.True(Raster.Near(selection, Token(variant, "ListInactiveSelectionBackground")),
                $"Helper.cs in {theme}: the unfocused selected row is painted {selection}, not the inactive selection.");

            AssertAccentHue(shown, theme, node, IconKind.CSharpFile, selection);
        }
        finally { shown.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task AButtonInAFocusedSelectedRowKeepsItsColour(string theme)
    {
        // A row's own icon turns white on the selection; a button's icon in the same row must keep its colour.
        var variant = Variant(theme);
        using var scope = new ThemeScope(variant);

        var rowIcon = new IconView { Kind = IconKind.CSharpFile, IconSize = 14, Classes = { "rowIcon" }, [RowIcon.FollowsSelectionProperty] = true, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button
        {
            Content = new IconView { Kind = IconKind.Stop, IconSize = 14, VerticalAlignment = VerticalAlignment.Center },
            Background = new SolidColorBrush(Colors.Gray),
            BorderThickness = default,
            Padding = new Thickness(4, 0),
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { rowIcon, button, new TextBlock { Text = "Helper.cs" } }
        };
        var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = default };
        var item = new TreeViewItem { Header = content };
        tree.Items.Add(item);
        tree.SelectedItem = item;

        var surface = new Border { Width = 240, Height = 120, Background = new SolidColorBrush(Token(variant, "SideBarBackground")), Child = tree };
        var window = new Window { Content = surface, Width = 240, Height = 120 };

        try
        {
            window.Show();
            await SettleAsync(window);
            await FocusAsync(item, window);

            var raster = Raster.Of(surface);
            var layoutRoot = LayoutRootOf(item);
            var selection = raster.At(Raster.BoundsIn(layoutRoot, surface).Right - 2, (int)Raster.BoundsIn(layoutRoot, surface).Center.Y);

            var rowIconContrast = raster.BestContrastAgainst(Raster.BoundsIn(rowIcon, surface), selection);
            Assert.True(rowIconContrast >= 3.0, $"the row's own icon in {theme} reaches only {rowIconContrast:0.00}:1 on the selection.");

            var buttonBox = Raster.BoundsIn(button, surface);
            var buttonBackground = raster.At(buttonBox.X + 2, (int)buttonBox.Center.Y);
            var ink = Ink(raster, Raster.BoundsIn((Control)button.Content!, surface), buttonBackground);
            var accent = IdeIcons.AccentFor(IconKind.Stop)!.Value;
            var drift = HueDifference(Hue(ink), Hue(accent));
            var range = Math.Max(ink.R, Math.Max(ink.G, ink.B)) - Math.Min(ink.R, Math.Min(ink.G, ink.B));

            Assert.True(drift <= HueTolerance && range >= 60,
                $"the button's icon in {theme} is {ink}, {drift:0.0} degrees from its accent {accent}: it lost its colour on the focused row.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TheRowIconFollowsTheSelectionOnceAfterReattachAndToggling()
    {
        // Each step must change the icon's class exactly once: a handler left behind by a detach, or a second one
        // added by toggling the property, would show as an extra change.
        using var scope = new ThemeScope(ThemeVariant.Light);

        var icon = new IconView { Kind = IconKind.CSharpFile, IconSize = 14, Classes = { "rowIcon" }, [RowIcon.FollowsSelectionProperty] = true };
        var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = default };
        var item = new TreeViewItem { Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon } } };
        tree.Items.Add(item);
        tree.SelectedItem = item;

        var host = new Border { Width = 240, Height = 120, Child = tree };
        var window = new Window { Content = host, Width = 240, Height = 120 };

        try
        {
            window.Show();
            await SettleAsync(window);
            await FocusAsync(item, window);

            var changes = 0;
            icon.Classes.CollectionChanged += (_, e) =>
            {
                if ((e.NewItems?.Contains(RowIcon.SelectedClass) ?? false) || (e.OldItems?.Contains(RowIcon.SelectedClass) ?? false))
                    changes++;
            };

            Assert.True(icon.Classes.Contains(RowIcon.SelectedClass), "the focused selected row's icon has no selected class");

            // Detach and re-attach the tree, then focus the row again.
            host.Child = null;
            await SettleAsync(window);
            Assert.False(icon.Classes.Contains(RowIcon.SelectedClass), "a detached icon kept the selected class");
            Assert.Equal(1, changes);

            host.Child = tree;
            await SettleAsync(window);
            await FocusAsync(item, window);
            Assert.True(icon.Classes.Contains(RowIcon.SelectedClass), "a re-attached icon did not follow the selection");
            Assert.Equal(2, changes);

            // Toggle the property off and on: the second link must not stack a second set of handlers.
            RowIcon.SetFollowsSelection(icon, false);
            Assert.Equal(3, changes);
            RowIcon.SetFollowsSelection(icon, true);
            Assert.True(icon.Classes.Contains(RowIcon.SelectedClass), "a toggled icon did not follow the selection");
            Assert.Equal(4, changes);

            // Deselecting the row clears the class exactly once.
            tree.SelectedItem = null;
            await SettleAsync(window);
            Assert.False(icon.Classes.Contains(RowIcon.SelectedClass), "a deselected row's icon kept the selected class");
            Assert.Equal(5, changes);
        }
        finally { window.Close(); }
    }

    // ------------------------------------------------------------------
    // The accents, against every palette's list surfaces.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public void TheTreesFileAndProjectAccentsReachThreeToOneOnEveryPalette()
    {
        var variants = new[]
        {
            ThemeVariant.Light, ThemeVariant.Dark,
            IdeThemes.VisualStudioBlue, IdeThemes.HighContrastLight, IdeThemes.HighContrastDark,
            IdeThemes.SolarizedLight, IdeThemes.SolarizedDark,
        };

        IconKind[] kinds = [IconKind.CSharpFile, IconKind.Solution, IconKind.Project];

        foreach (var variant in variants)
            foreach (var key in new[] { "SideBarBackground", "EditorBackground" })
            {
                var surface = Token(variant, key);

                foreach (var kind in kinds)
                {
                    var accent = IdeIcons.AccentFor(kind)!.Value;
                    var contrast = Raster.Contrast(accent, surface);

                    Assert.True(contrast >= 3.0,
                        $"{kind} is {accent}: {contrast:0.00}:1 on {key} of {variant.Key}.");
                }
            }
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static ThemeVariant Variant(string name) =>
        name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    /// <summary>A colour of the palette the variant names, as the application resolves it.</summary>
    private static Color Token(ThemeVariant variant, string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return (Color)value!;
    }

    private (Window Window, Border Surface, SolutionExplorerPanel Panel, MainWindowViewModel Vm) Open(ThemeVariant variant)
    {
        var vm = new MainWindowViewModel();
        vm.Explorer.Load(Path.Combine(_root, "Probe.vbproj"));

        var panel = new SolutionExplorerPanel(vm);

        // On the side bar, as the dock places it: the rows read against that surface.
        var surface = new Border
        {
            Width = 240,
            Height = 200,
            Background = new SolidColorBrush(Token(variant, "SideBarBackground")),
            Child = panel
        };
        var window = new Window { Content = surface, Width = 240, Height = 200 };

        window.Show();
        window.UpdateLayout();

        return (window, surface, panel, vm);
    }

    /// <summary>
    /// Checks that a row's icon, drawn without a selection, is still its accent: the ink that stands furthest
    /// from the side bar has the accent's hue.
    /// </summary>
    private static void AssertAccentHue(
        (Window Window, Border Surface, SolutionExplorerPanel Panel, MainWindowViewModel Vm) shown,
        string theme, SolutionTreeNode node, IconKind kind, Color? surface = null)
    {
        var raster = Raster.Of(shown.Surface);
        var background = surface ?? Token(Variant(theme), "SideBarBackground");

        var area = Raster.BoundsIn(IconOf(RowFor(shown.Panel.TreeForTests, node)), shown.Surface);
        var ink = Ink(raster, area, background);
        var accent = IdeIcons.AccentFor(kind)!.Value;

        var drift = HueDifference(Hue(ink), Hue(accent));

        Assert.True(drift <= HueTolerance,
            $"{kind} for {node.Name} in {theme}: the icon's ink is {ink}, {drift:0.0} degrees from its accent {accent}.");
    }

    /// <summary>
    /// Gives a row the keyboard, as a click on it does: the tree itself is not focusable, its rows are. The headless
    /// window takes its activation on a later dispatcher tick, so focus is asked for only once it has settled.
    /// </summary>
    private static async Task FocusAsync(Control row, Window window)
    {
        window.Activate();
        await SettleAsync(window);

        row.Focus();
        await SettleAsync(window);

        Assert.True(row.IsKeyboardFocusWithin, "the row did not take the focus");
    }

    private static async Task SettleAsync(Window window, int passes = 4)
    {
        for (var i = 0; i < passes; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(40);
        }

        window.UpdateLayout();
    }

    /// <summary>A file anywhere under the project; its folders are opened so that its row is realised.</summary>
    private static SolutionTreeNode FileNode(MainWindowViewModel vm, string name) =>
        Find(vm.Explorer.Roots[0], name) ?? throw new InvalidOperationException($"no node named {name}");

    private static SolutionTreeNode? Find(SolutionTreeNode parent, string name)
    {
        foreach (var child in parent.Children)
        {
            if (child.Name == name) return child;

            if (Find(child, name) is { } deeper)
            {
                child.IsExpanded = true;
                return deeper;
            }
        }

        return null;
    }

    private static TreeViewItem RowFor(TreeView tree, SolutionTreeNode node) =>
        tree.GetVisualDescendants().OfType<TreeViewItem>().First(item => ReferenceEquals(item.DataContext, node));

    /// <summary>The icon in a row's own header: not the icons of the rows nested under it.</summary>
    private static IconView IconOf(TreeViewItem row) =>
        row.GetVisualDescendants().OfType<IconView>()
            .First(icon => ReferenceEquals(icon.FindAncestorOfType<TreeViewItem>(), row));

    private static Control LayoutRootOf(TreeViewItem row) =>
        row.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_LayoutRoot");

    private static int RowCentreY(TreeViewItem row, Visual surface) =>
        (int)Raster.BoundsIn(LayoutRootOf(row), surface).Center.Y;

    /// <summary>The pixel in the area that stands furthest from the surface: the icon's ink.</summary>
    private static Color Ink(Raster raster, PixelRect area, Color surface)
    {
        var best = surface;
        var bestContrast = 1.0;

        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
            {
                var colour = raster.At(x, y);
                var contrast = Raster.Contrast(colour, surface);
                if (contrast > bestContrast) (best, bestContrast) = (colour, contrast);
            }

        return best;
    }

    /// <summary>The hue in degrees, 0 to 360, as HSV reads it.</summary>
    private static double Hue(Color c)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta == 0) return 0;

        var hue = max == r ? (g - b) / delta % 6
                : max == g ? (b - r) / delta + 2
                : (r - g) / delta + 4;

        return (hue * 60 + 360) % 360;
    }

    private static double HueDifference(double a, double b)
    {
        var difference = Math.Abs(a - b) % 360;
        return Math.Min(difference, 360 - difference);
    }
}
