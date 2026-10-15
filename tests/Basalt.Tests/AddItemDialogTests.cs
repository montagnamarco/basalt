using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Razor.Vb;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using static Basalt.Tests.UserJourneyItemsTests;

namespace Basalt.Tests;

/// <summary>
/// Add New Item, driven the way a person drives it: the real
/// <see cref="MainWindow"/>, Solution Explorer's context menu, and the dialog
/// answered through its own controls. The catalogue's rules are in
/// <see cref="AddItemCatalogTests"/> and what the templates write in
/// <see cref="AddItemTemplateBuildTests"/>; this is the dialog and the window.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class AddItemDialogTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-additem-dialog-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-additem-dialog-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public AddItemDialogTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
        try { File.Delete(AddItemMemory.PathBeside(_settings)); } catch (IOException) { }
    }

    private const string WebProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <MyType>Empty</MyType>
          </PropertyGroup>
        </Project>
        """;

    private string WriteWebProject()
    {
        var path = Path.Combine(_root, "Site.vbproj");
        File.WriteAllText(path, WebProjectXml);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module Program\nSub Main()\nEnd Sub\nEnd Module\n");
        return path;
    }

    private static T Find<T>(Window dialog, string name) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Search(Window dialog, string text) => Find<TextBox>(dialog, "AddItemSearch").Text = text;

    private static string ErrorText(Window dialog) => Find<TextBlock>(dialog, "AddItemError").Text ?? "";

    private async Task<(TestWindow Host, MainWindowViewModel Vm, Window Dialog)> OpenAsync(
        string project, ThemeVariant? theme = null)
    {
        var host = new TestWindow(project);
        await host.SettleAsync(10);

        // The main window applies the theme its settings name when it starts, over the
        // one the test asked for; asked again once it is up, for the dialog it opens.
        if (theme is not null)
        {
            Application.Current!.RequestedThemeVariant = theme;
            await host.SettleAsync(3);
        }

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        var dialog = await OpenAddNewItemFromExplorerAsync(host, vm.Explorer.Roots.Single());
        return (host, vm, dialog);
    }

    private static async Task CancelAsync(TestWindow host, Window dialog)
    {
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.ButtonCancel)));
        await host.SettleAsync(3);
    }

    // ------------------------------------------------------------------
    // Search: the list narrows by name and by description, across every
    // category, and a search with no answer says so.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task SearchFiltersByNameAndDescriptionAcrossCategories()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        // The dialog opens on Code: its seven templates.
        Assert.Equal(7, OfferedTemplates(dialog).Count);

        // By name, from another category than the one showing.
        Search(dialog, "blazor");
        await host.SettleAsync(2);
        Assert.Equal([L("AddItem_Tpl_BlazorComponent_Name")], OfferedTemplates(dialog));

        // By description alone: "constructors" is in no template's name.
        Search(dialog, "constructors");
        await host.SettleAsync(2);
        Assert.Equal([L("AddItem_Tpl_ExceptionClass_Name")], OfferedTemplates(dialog));

        // Several words must all match, in the name or the description.
        Search(dialog, "xml schema");
        await host.SettleAsync(2);
        Assert.Contains(L("AddItem_Tpl_XmlSchema_Name"), OfferedTemplates(dialog));
        Assert.DoesNotContain(L("AddItem_Tpl_TextFile_Name"), OfferedTemplates(dialog));

        Search(dialog, "no such template");
        await host.SettleAsync(2);
        Assert.Empty(OfferedTemplates(dialog));
        Assert.True(Find<TextBlock>(dialog, "AddItemNoMatches").IsVisible);

        // Cleared, the category comes back.
        Search(dialog, "");
        await host.SettleAsync(2);
        Assert.Equal(7, OfferedTemplates(dialog).Count);
        Assert.False(Find<TextBlock>(dialog, "AddItemNoMatches").IsVisible);

        await CancelAsync(host, dialog);
    }

    // ------------------------------------------------------------------
    // Categories: the tree lists them all, and each shows its templates,
    // each row with an icon, a name and a description.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task EachCategoryListsItsTemplatesWithIconNameAndDescription()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        var tree = Find<TreeView>(dialog, "AddItemCategories");
        Assert.Equal(
            ItemTemplateCatalog.Categories.Select(c => L(ItemTemplates.CategoryKey(c))),
            tree.Items.OfType<TreeViewItem>().Select(i =>
                ((StackPanel)i.Header!).Children.OfType<TextBlock>().Single().Text));

        // Each name has an icon in front of it, so the tree has no empty indent.
        Assert.All(tree.Items.OfType<TreeViewItem>(),
            i => Assert.Single(((StackPanel)i.Header!).Children.OfType<IconView>()));

        foreach (var category in ItemTemplateCatalog.Categories)
        {
            SelectCategory(dialog, category);
            await host.SettleAsync(2);

            var expected = ItemTemplates.InCategory(category).ToList();
            Assert.Equal(expected.Select(t => t.Name), OfferedTemplates(dialog));

            foreach (var row in TemplateList(dialog).Items.OfType<ListBoxItem>())
            {
                var template = (ItemTemplateDefinition)row.Tag!;
                var texts = row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

                Assert.Contains(template.Name, texts);
                Assert.Contains(template.Description, texts);
                Assert.Single(row.GetVisualDescendants().OfType<IconView>());
            }
        }

        await CancelAsync(host, dialog);
    }

    [AvaloniaFact]
    public async Task ChoosingATemplateShowsItsDescriptionAndAPreviewOfTheFile()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Structure);
        await host.SettleAsync(2);

        Assert.Equal(L("AddItem_Tpl_Structure_Description"), Find<TextBlock>(dialog, "AddItemDescription").Text);

        var preview = Find<TextBox>(dialog, "AddItemPreview");
        Assert.True(preview.IsReadOnly);
        Assert.Contains("Public Structure Structure1", preview.Text);

        // The preview follows the name typed.
        TypeName(dialog, "Point3D");
        Assert.Contains("Public Structure Point3D", preview.Text);

        // A pair shows both of its files.
        ChooseTemplate(dialog, ItemTemplateIds.Dialog);
        TypeName(dialog, "Ask");
        Assert.Contains("Ask.Designer.vb", preview.Text);
        Assert.Contains("AcceptButton", preview.Text);

        await CancelAsync(host, dialog);
        Assert.False(File.Exists(Path.Combine(_root, "Ask.vb")));
    }

    // ------------------------------------------------------------------
    // The dialog opens where it was left.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ItRemembersTheLastCategory()
    {
        var project = WriteConsoleProject(_root, "App");

        var (host, _, dialog) = await OpenAsync(project);
        using (host)
        {
            Assert.Equal(ItemCategory.Code, SelectedCategory(dialog));

            SelectCategory(dialog, ItemCategory.Razor);
            await CancelAsync(host, dialog);

            var again = await OpenAddNewItemFromExplorerAsync(host,
                ((MainWindowViewModel)host.Window.DataContext!).Explorer.Roots.Single());

            Assert.Equal(ItemCategory.Razor, SelectedCategory(again));
            Assert.Contains(L("AddItem_Tpl_MvcView_Name"), OfferedTemplates(again));

            await CancelAsync(host, again);
        }
    }

    private static ItemCategory SelectedCategory(Window dialog) =>
        (ItemCategory)((TreeViewItem)Find<TreeView>(dialog, "AddItemCategories").SelectedItem!).Tag!;

    // ------------------------------------------------------------------
    // The name: checked as it is typed, and again on Add.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ABadIdentifierIsRefusedWithAMessageAndNothingIsWritten()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Class);

        foreach (var bad in new[] { "9Lives", "My Class", "End" })
        {
            TypeName(dialog, bad);

            // The message is there before any click.
            Assert.Equal(L(StringKeys.AddItemNameInvalid), ErrorText(dialog));

            ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
            await host.SettleAsync(3);

            Assert.Contains(dialog, host.Window.OwnedWindows.ToList());
            Assert.Empty(Directory.GetFiles(_root, "*.vb").Where(f => !f.EndsWith("Program.vb")));
        }

        TypeName(dialog, "");
        Assert.Equal(L(StringKeys.AddItemNameEmpty), ErrorText(dialog));

        TypeName(dialog, "Customer");
        Assert.Equal("", ErrorText(dialog));

        await CancelAsync(host, dialog);
    }

    [AvaloniaFact]
    public async Task AnExistingFileIsRefusedByNameAndNothingIsOverwritten()
    {
        File.WriteAllText(Path.Combine(_root, "Existing.vb"), "' mine");
        File.WriteAllText(Path.Combine(_root, "Half.Designer.vb"), "' only the designer half");

        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Class);
        TypeName(dialog, "Existing");
        Assert.Equal(Localizer.Get(StringKeys.AddItemNameExists, "Existing.vb"), ErrorText(dialog));

        // The extension typed is the same file (where the file system ignores case, the name in other case too).
        TypeName(dialog, "Existing.VB");
        Assert.Equal(Localizer.Get(StringKeys.AddItemNameExists, "Existing.vb"), ErrorText(dialog));

        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(3);
        Assert.Contains(dialog, host.Window.OwnedWindows.ToList());
        Assert.Equal("' mine", File.ReadAllText(Path.Combine(_root, "Existing.vb")));

        // A form is refused when either half of its pair is there, and the message names that half.
        ChooseTemplate(dialog, ItemTemplateIds.WindowsForm);
        TypeName(dialog, "Half");
        Assert.Equal(Localizer.Get(StringKeys.AddItemNameExists, "Half.Designer.vb"), ErrorText(dialog));
        Assert.False(File.Exists(Path.Combine(_root, "Half.vb")));

        // A file item is checked the same way, and a file name that cannot be one says so.
        ChooseTemplate(dialog, ItemTemplateIds.MvcView);
        TypeName(dialog, "a/b");
        Assert.Equal(Localizer.Get(StringKeys.AddItemNameFileInvalid, "a/b"), ErrorText(dialog));

        await CancelAsync(host, dialog);
    }

    [AvaloniaFact]
    public async Task ChoosingATemplateSuggestsAFreeNameThatCountsUp()
    {
        File.WriteAllText(Path.Combine(_root, "Class1.vb"), "");
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Class);
        Assert.Equal("Class2.vb", Find<TextBox>(dialog, "AddItemName").Text);
        Assert.Equal("", ErrorText(dialog));

        ChooseTemplate(dialog, ItemTemplateIds.Module);
        Assert.Equal("Module1.vb", Find<TextBox>(dialog, "AddItemName").Text);

        await CancelAsync(host, dialog);
    }

    // ------------------------------------------------------------------
    // Adding: a Blazor component in a web project is written, parsed by the
    // compiler's parser, and opened.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AddingABlazorComponentWritesTheFileAndOpensIt()
    {
        var (host, vm, dialog) = await OpenAsync(WriteWebProject());
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.BlazorComponent);
        TypeName(dialog, "Greeting");
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        var path = Path.Combine(_root, "Greeting.vbrazor");
        Assert.True(File.Exists(path), "the component was not written");

        var text = File.ReadAllText(path);
        Assert.Empty(VbHtmlParser.Parse(text).Diagnostics);
        Assert.Contains("<Parameter>", text);

        Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Path == path);
        Assert.Equal(path, vm.ActiveDocument?.FilePath);

        // It fits this project: no warning.
        Assert.DoesNotContain(vm.BuildOutput, line => line.Contains("Greeting.vbrazor"));
    }

    // ------------------------------------------------------------------
    // A template that does not fit the project is marked, and adding it
    // writes the file and says so plainly: it is not silently refused.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ARazorViewInAConsoleProjectIsMarkedAndAddedWithAWarning()
    {
        var (host, vm, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        SelectCategory(dialog, ItemCategory.Razor);
        await host.SettleAsync(3);

        var row = TemplateList(dialog).Items.OfType<ListBoxItem>()
            .Single(i => ((ItemTemplateDefinition)i.Tag!).Id == ItemTemplateIds.MvcView);
        Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == L(StringKeys.AddItemNotForProject));

        ChooseTemplate(dialog, ItemTemplateIds.MvcView);
        await host.SettleAsync(2);
        Assert.True(Find<TextBlock>(dialog, "AddItemMisfit").IsVisible);
        Assert.Contains(L(StringKeys.AddItemNeedsWeb), Find<TextBlock>(dialog, "AddItemMisfit").Text);

        TypeName(dialog, "Index");
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        var path = Path.Combine(_root, "Index.vbhtml");
        Assert.True(File.Exists(path), "the view was refused instead of added");
        Assert.Equal(path, vm.ActiveDocument?.FilePath);

        var warning = Localizer.Get(StringKeys.AddItemMisfitWarning, "Index.vbhtml", L(StringKeys.AddItemNeedsWeb));
        Assert.Contains(warning, vm.BuildOutput);
        Assert.Equal(warning, vm.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ATestClassIsOfferedWithANoteWhenTheProjectLacksThatFramework()
    {
        var project = Path.Combine(_root, "Tests.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module Program\nEnd Module\n");

        var (host, vm, dialog) = await OpenAsync(project);
        using var _ = host;

        SelectCategory(dialog, ItemCategory.Testing);
        await host.SettleAsync(3);

        bool Marked(string id) => TemplateList(dialog).Items.OfType<ListBoxItem>()
            .Single(i => ((ItemTemplateDefinition)i.Tag!).Id == id)
            .GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == L(StringKeys.AddItemNotForProject));

        Assert.False(Marked(ItemTemplateIds.XUnitTest));
        Assert.True(Marked(ItemTemplateIds.NUnitTest));
        Assert.True(Marked(ItemTemplateIds.MSTestTest));

        ChooseTemplate(dialog, ItemTemplateIds.NUnitTest);
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        Assert.True(File.Exists(Path.Combine(_root, "NUnitTest1.vb")));
        Assert.Contains(vm.BuildOutput, line => line.Contains("NUnitTest1.vb") && line.Contains("NUnit"));
    }

    // ------------------------------------------------------------------
    // Enter adds, a double click adds, Escape and Cancel do not.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task EnterAddsTheItem()
    {
        var (host, vm, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Structure);
        TypeName(dialog, "Point");

        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await host.SettleAsync(10);

        Assert.True(File.Exists(Path.Combine(_root, "Point.vb")), "Enter did not add the item");
        Assert.Equal(Path.Combine(_root, "Point.vb"), vm.ActiveDocument?.FilePath);
        Assert.DoesNotContain(dialog, host.Window.OwnedWindows.ToList());
    }

    [AvaloniaFact]
    public async Task EnterOnABadNameKeepsTheDialogOpenWithItsMessage()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Class);
        TypeName(dialog, "Sub");
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await host.SettleAsync(3);

        Assert.Contains(dialog, host.Window.OwnedWindows.ToList());
        Assert.Equal(L(StringKeys.AddItemNameInvalid), ErrorText(dialog));

        await CancelAsync(host, dialog);
    }

    [AvaloniaFact]
    public async Task ADoubleClickOnATemplateAddsItWithItsSuggestedName()
    {
        var (host, vm, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Interface);
        await host.SettleAsync(2);
        var row = TemplateList(dialog).Items.OfType<ListBoxItem>()
            .Single(i => ((ItemTemplateDefinition)i.Tag!).Id == ItemTemplateIds.Interface);

        row.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
        await host.SettleAsync(10);

        Assert.True(File.Exists(Path.Combine(_root, "Interface1.vb")), "the double click did not add the item");
        Assert.Equal(Path.Combine(_root, "Interface1.vb"), vm.ActiveDocument?.FilePath);
    }

    [AvaloniaFact]
    public async Task EscapeCancelsWithoutWriting()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Class);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await host.SettleAsync(5);

        Assert.DoesNotContain(dialog, host.Window.OwnedWindows.ToList());
        Assert.False(File.Exists(Path.Combine(_root, "Class1.vb")));
    }

    // ------------------------------------------------------------------
    // An Avalonia window opens in the designer, as Add Window's does.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AnAvaloniaWindowIsWrittenWithItsCodeBehindAndOpenedInTheDesigner()
    {
        var project = Path.Combine(_root, "Ui.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>WinExe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.1.1" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module Program\nEnd Module\n");

        var (host, vm, dialog) = await OpenAsync(project);
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.AvaloniaWindow);
        TypeName(dialog, "Shell");
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        Assert.True(File.Exists(Path.Combine(_root, "Shell.axaml")));
        Assert.True(File.Exists(Path.Combine(_root, "Shell.axaml.vb")));
        Assert.Equal(Path.Combine(_root, "Shell.axaml"), vm.ActiveDocument?.FilePath);
        Assert.DoesNotContain(vm.BuildOutput, line => line.Contains("Shell.axaml"));
    }

    // ------------------------------------------------------------------
    // Size: resizable, never narrower than 640, and at every width the
    // parts sit apart: the description and preview move under the list when
    // there is not room beside it.
    // ------------------------------------------------------------------
    private static Rect BoundsIn(Window dialog, Control control) =>
        new(control.TranslatePoint(default, dialog) ?? default, control.Bounds.Size);

    private static void AssertNoOverlap(Window dialog, params string[] names)
    {
        var parts = names.Select(name => (name, bounds: BoundsIn(dialog, dialog.GetVisualDescendants().OfType<Control>().First(c => c.Name == name)))).ToList();

        foreach (var part in parts)
        {
            Assert.True(part.bounds.Width > 0 && part.bounds.Height > 0, $"{part.name} has no size: {part.bounds}");
            Assert.True(part.bounds.Right <= dialog.Bounds.Width + 0.5 && part.bounds.Bottom <= dialog.Bounds.Height + 0.5,
                $"{part.name} {part.bounds} is outside the dialog {dialog.Bounds}");
        }

        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
                Assert.False(parts[i].bounds.Intersects(parts[j].bounds.Deflate(0.5)),
                    $"{parts[i].name} {parts[i].bounds} overlaps {parts[j].name} {parts[j].bounds}");
    }

    private static readonly string[] Parts =
        ["AddItemSearch", "AddItemCategories", "AddItemTemplates", "AddItemDetail", "AddItemName", "AddItemAdd", "AddItemCancel"];

    [AvaloniaFact]
    public async Task ItIsResizableWithAMinimumWidthAndNothingOverlapsAtAnyWidth()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        Assert.True(dialog.CanResize);
        Assert.True(dialog.MinWidth >= 640, "narrower than 640 px is not supported");
        Assert.True(dialog.MinHeight >= 400);

        ChooseTemplate(dialog, ItemTemplateIds.Dialog);

        // Wide: the detail is beside the list.
        dialog.Width = 1100;
        dialog.Height = 700;
        await host.SettleAsync(5);
        Assert.False(((AddItemDialog)dialog).IsNarrow);
        AssertNoOverlap(dialog, Parts);
        Assert.True(BoundsIn(dialog, Find<Grid>(dialog, "AddItemDetail")).Left >= BoundsIn(dialog, TemplateList(dialog)).Right - 0.5,
            "wide: the detail should sit to the right of the list");

        // Narrow, as small as the dialog goes: the detail is under the list.
        dialog.Width = 640;
        dialog.Height = dialog.MinHeight;
        await host.SettleAsync(5);
        Assert.True(((AddItemDialog)dialog).IsNarrow);
        AssertNoOverlap(dialog, Parts);
        Assert.True(BoundsIn(dialog, Find<Grid>(dialog, "AddItemDetail")).Top >= BoundsIn(dialog, TemplateList(dialog)).Bottom - 0.5,
            "narrow: the detail should sit under the list");
        Assert.True(TemplateList(dialog).Bounds.Height >= 100, "the list must keep a few rows at the minimum size");

        // The description and preview are still there, and readable.
        Assert.True(Find<TextBox>(dialog, "AddItemPreview").Bounds.Height > 20);

        await CancelAsync(host, dialog);
    }

    // ------------------------------------------------------------------
    // Both themes, read from the pixels the dialog draws.
    // ------------------------------------------------------------------
    private sealed class Frame(byte[] pixels, int width, int height)
    {
        public Color At(int x, int y)
        {
            var i = (y * width + x) * 4;
            // The bytes come out as red, green, blue, alpha.
            return Color.FromRgb(pixels[i], pixels[i + 1], pixels[i + 2]);
        }

        public Color At(Point point) => At((int)point.X, (int)point.Y);

        /// <summary>The colour in the rectangle that stands furthest from <paramref name="background"/>: the ink.</summary>
        public Color Ink(Rect area, Color background)
        {
            var best = background;
            var bestContrast = 1.0;

            for (var y = (int)Math.Max(0, area.Top); y < Math.Min(height, area.Bottom); y++)
                for (var x = (int)Math.Max(0, area.Left); x < Math.Min(width, area.Right); x++)
                {
                    var colour = At(x, y);
                    var contrast = Contrast(colour, background);
                    if (contrast > bestContrast) (best, bestContrast) = (colour, contrast);
                }

            return best;
        }
    }

    private static Frame Rasterise(Window window)
    {
        var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));

        // A window's own background is the platform's to paint, not part of what
        // rendering its visual tree draws: laid down first, as the screen has it.
        using (var context = bitmap.CreateDrawingContext())
            context.FillRectangle(window.Background ?? Brushes.Transparent, new Rect(0, 0, size.Width, size.Height));

        bitmap.Render(window);

        var pixels = new byte[size.Width * size.Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);

        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        return new Frame(pixels, size.Width, size.Height);
    }

    internal static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    internal static double Contrast(Color a, Color b)
    {
        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }

    private static Rect RowBackground(Window dialog, ListBoxItem row)
    {
        var bounds = BoundsIn(dialog, row);
        return new Rect(bounds.Left + 1, bounds.Top + 1, 3, Math.Max(1, bounds.Height - 2));
    }

    private async Task<(Frame Frame, Color Background)> SnapshotAsync(string variant, Action<Window>? arrange = null)
    {
        using var theme = new ThemeScope(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"), variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        using var _ = host;

        dialog.Width = 1000;
        dialog.Height = 640;
        arrange?.Invoke(dialog);
        await host.SettleAsync(5);

        // The window's own background is not part of what a visual renders; a plain
        // row of the list is the dialog's surface, so that is what is read.
        var frame = Rasterise(dialog);
        var plainRow = TemplateList(dialog).Items.OfType<ListBoxItem>().First(r => !r.IsSelected);
        var background = frame.At(RowBackground(dialog, plainRow).TopLeft + new Point(1, 1));
        await CancelAsync(host, dialog);
        return (frame, background);
    }

    [AvaloniaFact]
    public async Task TheTwoThemesDrawTheDialogInDifferentColours()
    {
        var light = await SnapshotAsync("Light");
        var dark = await SnapshotAsync("Dark");

        // Light is a light surface and Dark a dark one: not a theme in name only.
        Assert.True(Luminance(light.Background) > 0.6, $"Light draws {light.Background}");
        Assert.True(Luminance(dark.Background) < 0.1, $"Dark draws {dark.Background}");
    }

    /// <summary>
    /// The text of the selected row, with the list in focus (the selection is
    /// then the blue one), is read against the row's own background: 4.5:1 is
    /// what text needs. It was the dark text of an unselected row on the blue.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task TheSelectedRowsTextIsLightOnTheSelectionAndReadableInBothThemes(string variant)
    {
        using var theme = new ThemeScope(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"), variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        using var _ = host;

        dialog.Width = 1000;
        dialog.Height = 640;
        ChooseTemplate(dialog, ItemTemplateIds.Structure);
        TemplateList(dialog).Items.OfType<ListBoxItem>().Single(r => r.IsSelected).Focus();
        await host.SettleAsync(6);

        var rows = TemplateList(dialog).Items.OfType<ListBoxItem>().ToList();
        var selected = rows.Single(r => r.IsSelected);
        var other = rows.First(r => !r.IsSelected);
        var frame = Rasterise(dialog);

        foreach (var (row, isSelected) in new[] { (selected, true), (other, false) })
        {
            var background = frame.At(RowBackground(dialog, row).TopLeft + new Point(1, 1));
            var texts = row.GetVisualDescendants().OfType<TextBlock>().ToList();

            foreach (var text in texts)
            {
                var ink = frame.Ink(BoundsIn(dialog, text), background);
                var contrast = Contrast(ink, background);

                Assert.True(contrast >= 4.5,
                    $"{variant}, {(isSelected ? "selected" : "plain")} row, \"{text.Text}\": {ink} on {background} is {contrast:0.0}:1");
            }

            if (isSelected && variant == "Light")
            {
                // The blue selection of the light theme: its text must be the light one.
                Assert.True(Luminance(background) < 0.25, $"the selection should be dark blue here, it is {background}");
                var ink = frame.Ink(BoundsIn(dialog, texts[0]), background);
                Assert.True(Luminance(ink) > 0.7, $"the selected row's text is {ink}, not light");
            }
        }

        // The tree's selected category too.
        var tree = Find<TreeView>(dialog, "AddItemCategories");
        tree.Items.OfType<TreeViewItem>().Single(i => i.IsSelected).Focus();
        await host.SettleAsync(4);
        frame = Rasterise(dialog);

        var item = tree.Items.OfType<TreeViewItem>().Single(i => i.IsSelected);
        var label = item.GetVisualDescendants().OfType<TextBlock>().First();
        var itemBackground = frame.At(BoundsIn(dialog, item).TopLeft + new Point(BoundsIn(dialog, item).Width - 4, 2));
        Assert.True(Contrast(frame.Ink(BoundsIn(dialog, label), itemBackground), itemBackground) >= 4.5,
            $"{variant}: the selected category's text is not readable on {itemBackground}");

        await CancelAsync(host, dialog);
    }

    /// <summary>
    /// Every template's icon is drawn on the list in Dark, and at least 3:1
    /// against it: the MVC views' dark purple and the pages' dark red were not.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task EveryTemplateIconIsVisibleAgainstTheList(string variant)
    {
        using var theme = new ThemeScope(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"), variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        using var _ = host;

        dialog.Width = 1000;
        dialog.Height = 900;
        await host.SettleAsync(4);

        foreach (var category in ItemTemplateCatalog.Categories)
        {
            SelectCategory(dialog, category);
            await host.SettleAsync(3);

            var frame = Rasterise(dialog);

            foreach (var row in TemplateList(dialog).Items.OfType<ListBoxItem>())
            {
                var template = (ItemTemplateDefinition)row.Tag!;
                var icon = row.GetVisualDescendants().OfType<IconView>().Single();
                var area = BoundsIn(dialog, icon);
                var background = frame.At(RowBackground(dialog, row).TopLeft + new Point(1, 1));

                var ink = frame.Ink(area, background);
                var contrast = Contrast(ink, background);

                // Strokes are a pixel and a quarter wide, so the strongest pixel is
                // short of the colour itself: the bar is 2 here for what is really
                // drawn, and 3 for the colour (AddItemIconTests). Dark purple on
                // dark read 1.6.
                Assert.True(contrast >= 2.0,
                    $"{variant}: the icon of {template.Id} ({template.Icon}) is {ink} on {background}: {contrast:0.0}:1");
            }
        }

        await CancelAsync(host, dialog);
    }

    // ------------------------------------------------------------------
    // The name field, and what it refuses.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheSuggestedNameIsSelectedWithoutItsExtension()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        // On opening, the first template's name.
        var name = Find<TextBox>(dialog, "AddItemName");
        Assert.Equal("Class1", name.SelectedText);

        SelectCategory(dialog, ItemCategory.WindowsForms);
        ChooseTemplate(dialog, ItemTemplateIds.Component);
        Assert.Equal("Component1.vb", name.Text);
        Assert.Equal("Component1", name.SelectedText);

        // A name with no extension at all is selected whole.
        ChooseTemplate(dialog, ItemTemplateIds.AnyFile);
        Assert.Equal(name.Text, name.SelectedText);

        await CancelAsync(host, dialog);
    }

    [AvaloniaFact]
    public async Task CountingUpAControllerNameGivesAFileTheDialogAccepts()
    {
        File.WriteAllText(Path.Combine(_root, "HomeController.vb"), "");

        var (host, vm, dialog) = await OpenAsync(WriteWebProject());
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.MvcController);
        Assert.Equal("Home2Controller.vb", Find<TextBox>(dialog, "AddItemName").Text);
        Assert.Equal("", ErrorText(dialog));

        // "Home" is HomeController, which is there.
        TypeName(dialog, "Home");
        Assert.Equal(Localizer.Get(StringKeys.AddItemNameExists, "HomeController.vb"), ErrorText(dialog));

        TypeName(dialog, "Home2Controller.vb");
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        Assert.True(File.Exists(Path.Combine(_root, "Home2Controller.vb")));
        Assert.False(File.Exists(Path.Combine(_root, "Home2ControllerController.vb")));
        Assert.Equal(Path.Combine(_root, "Home2Controller.vb"), vm.ActiveDocument?.FilePath);
    }

    [AvaloniaTheory]
    [InlineData(ItemTemplateIds.MvcController, "Controller")]
    [InlineData(ItemTemplateIds.AvaloniaWindow, "Window")]
    [InlineData(ItemTemplateIds.WindowsForm, "Form")]
    public async Task ANameThatIsTheBaseClassIsRefusedWithAMessage(string id, string typed)
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, id);
        TypeName(dialog, typed);

        Assert.Equal(Localizer.Get(StringKeys.AddItemNameShadows, typed), ErrorText(dialog));

        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(3);

        Assert.Contains(dialog, host.Window.OwnedWindows.ToList());
        Assert.Empty(Directory.GetFiles(_root, "*" + typed + "*"));
    }

    [AvaloniaFact]
    public async Task ADottedNameIsKeptWhole()
    {
        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.EmptyFile);
        TypeName(dialog, "Models.Customer");
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        Assert.True(File.Exists(Path.Combine(_root, "Models.Customer.vb")));
        Assert.False(File.Exists(Path.Combine(_root, "Models.Customer")));
    }

    /// <summary>
    /// A file differing only in case is the same file where the file system
    /// ignores case (Windows, macOS), and the dialog says so before the click.
    /// Where it keeps case (Linux) they are two files, and it does not.
    /// </summary>
    [AvaloniaFact]
    public async Task ACollisionThatDiffersOnlyInCaseIsFoundWhereTheFileSystemIgnoresCase()
    {
        File.WriteAllText(Path.Combine(_root, "customer.vb"), "' mine");
        var ignoresCase = File.Exists(Path.Combine(_root, "CUSTOMER.VB"));

        var (host, _, dialog) = await OpenAsync(WriteConsoleProject(_root, "App"));
        using var _ = host;

        ChooseTemplate(dialog, ItemTemplateIds.Class);
        TypeName(dialog, "Customer");

        if (ignoresCase)
        {
            Assert.Equal(Localizer.Get(StringKeys.AddItemNameExists, "Customer.vb"), ErrorText(dialog));

            ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
            await host.SettleAsync(3);
            Assert.Contains(dialog, host.Window.OwnedWindows.ToList());
            Assert.Equal("' mine", File.ReadAllText(Path.Combine(_root, "customer.vb")));
        }
        else
        {
            Assert.Equal("", ErrorText(dialog));
        }

        await CancelAsync(host, dialog);
    }
}
