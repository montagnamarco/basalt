using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Proves the cascading DropDownItems panel a ToolStripDropDownButton/SplitButton owns
/// (<see cref="WinFormsToolStripDropDownPanel"/>) is actually wired into
/// <see cref="WinFormsToolStripDesignEditor"/> and <see cref="WinFormsDesignerView"/>: selecting
/// the owner shows it under the strip, "Type Here" and the kind arrow add to its own
/// DropDownItems (not the strip's own flat Items), a MenuItem can nest further, each add is one
/// undo step, the overlay really paints, a selected nested item drives Properties, and a
/// SplitButton gets the same treatment as a DropDownButton.
/// </summary>
public sealed class WinFormsToolStripDropDownInlineTests
{
    private const string ToolStripSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ToolStrip1 = New Global.System.Windows.Forms.ToolStrip()
                Me.ToolStrip1.Name = "ToolStrip1"
                Me.Controls.Add(Me.ToolStrip1)
                ' Keep application initialization.
            End Sub
            Friend WithEvents ToolStrip1 As Global.System.Windows.Forms.ToolStrip
        End Class
        """;

    [AvaloniaFact]
    public async Task SelectingADropDownButtonShowsItsPanelAddsNestedItemsAndUndoesEachStep()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", ToolStripSource, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.ToolStrip>()));

            // Add a ToolStripDropDownButton through the flat "Type Here" arrow, the same way
            // WinFormsToolStripInlineTests adds a plain item.
            var editor = Editor(view);
            Click(Find<Button>(editor, "ToolStripInlineTypeArrow"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(editor, "ToolStripInlineType_ToolStripDropDownButton"));
            await WaitUntilAsync(() => ReadStrip(document).Count == 1 && view.IsEnabled);
            var owner = Assert.Single(ReadStrip(document));
            Assert.Equal("ToolStripDropDownButton1", owner.Name);
            var afterOwnerAdded = document.Text;

            editor = Editor(view);
            Assert.Equal(owner.Name, editor.SelectedItemName);

            // (pixel proof) Selecting the owner shows its panel, hosted in its own paintable
            // Border, directly under the strip's bar.
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var panelHost = Find<Border>(editor, "ToolStripDropDownHost");
            Assert.True(panelHost.IsVisible, "Selecting a DropDownButton must show its own DropDownItems panel.");
            var panelPoint = panelHost.TranslatePoint(new Point(0, 10), window)!.Value;
            var panelBorder = ReadPixel(window, panelPoint);
            Assert.True(panelBorder.Blue > panelBorder.Red + 30,
                $"The DropDownItems panel's own border should paint DodgerBlue at {panelPoint}; RGB={panelBorder.Red},{panelBorder.Green},{panelBorder.Blue}.");

            // Typing into the panel's own "Type Here" adds a ToolStripMenuItem to the owner's
            // DropDownItems, not to the strip's own flat Items.
            var typeHere = Find<TextBox>(panelHost, "ToolStripDropDownTypeHere");
            typeHere.Text = "Open";
            Enter(typeHere);
            await WaitUntilAsync(() => ReadDropDown(document, owner.Name).Count == 1 && view.IsEnabled);
            var open = Assert.Single(ReadDropDown(document, owner.Name));
            Assert.Equal("ToolStripMenuItem1", open.Name);
            Assert.Equal("Open", open.Text);
            Assert.Equal(owner.Name, open.Parent);
            Assert.Equal(1, ReadStrip(document).Count);
            Assert.Contains(
                "Me.ToolStripDropDownButton1.DropDownItems.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem1})",
                document.Text);
            var realOwner = Assert.IsAssignableFrom<Forms.ToolStripDropDownItem>(
                Assert.Single(Assert.IsType<Forms.ToolStrip>(surface.SelectedControl).Items.Cast<Forms.ToolStripItem>()));
            var realOpen = Assert.IsType<Forms.ToolStripMenuItem>(Assert.Single(realOwner.DropDownItems.Cast<Forms.ToolStripItem>()));
            Assert.Equal("Open", realOpen.Text);
            var afterOpenAdded = document.Text;

            // (Properties) The newly added nested item becomes both the panel's and the
            // Properties panel's selection.
            editor = Editor(view);
            panelHost = Find<Border>(editor, "ToolStripDropDownHost");
            var propertyGrid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() =>
                propertyGrid.Properties.Any(property => property.Name == "Text" && property.CurrentValue == "Open"));

            // The arrow next to the panel's own "Type Here" offers a separator too.
            Click(Find<ToggleButton>(panelHost, "ToolStripDropDownTypeArrow"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(panelHost, "ToolStripDropDownType_ToolStripSeparator"));
            await WaitUntilAsync(() => ReadDropDown(document, owner.Name).Count == 2 && view.IsEnabled);
            var separator = ReadDropDown(document, owner.Name)[1];
            Assert.True(separator.IsSeparator);
            Assert.DoesNotContain(separator.Name + ".Text", document.Text);
            var afterSeparatorAdded = document.Text;

            // A MenuItem inside the panel can itself be opened and nested further.
            editor = Editor(view);
            panelHost = Find<Border>(editor, "ToolStripDropDownHost");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(panelHost, "ToolStripDropDownOpen_" + open.Name));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var nestedTypeHere = Find<TextBox>(panelHost, "ToolStripDropDownTypeHere");
            nestedTypeHere.Text = "Recent";
            Enter(nestedTypeHere);
            await WaitUntilAsync(() => ReadDropDown(document, owner.Name).Count == 3 && view.IsEnabled);
            var nested = ReadDropDown(document, owner.Name).Single(item => item.Parent == open.Name);
            Assert.Equal("Recent", nested.Text);
            var liveOwner = Assert.IsAssignableFrom<Forms.ToolStripDropDownItem>(
                Assert.Single(Assert.IsType<Forms.ToolStrip>(surface.SelectedControl).Items.Cast<Forms.ToolStripItem>()));
            var liveOpen = Assert.IsType<Forms.ToolStripMenuItem>(liveOwner.DropDownItems[0]);
            var realNested = Assert.IsType<Forms.ToolStripMenuItem>(Assert.Single(liveOpen.DropDownItems.Cast<Forms.ToolStripItem>()));
            Assert.Equal("Recent", realNested.Text);
            var afterNestedAdded = document.Text;

            // (c) Each add is its own undo step.
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterSeparatorAdded && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterOpenAdded && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterOwnerAdded && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == ToolStripSource && view.IsEnabled);
            Assert.Contains("' Keep application initialization.", document.Text);

            // (reload round-trip) Rebuilding a fresh view from the final, fully-edited source
            // must show the exact same owner and nested tree in the live control, proving the
            // written Designer.vb is not just textually plausible but actually re-parses into
            // the same preview WinFormsToolStripDropDownPreview.Apply materializes live.
            _ = afterNestedAdded;
            var reloadedDocument = new EditorDocumentViewModel("MainForm.Designer.vb", afterNestedAdded, true);
            using var reloadedView = new WinFormsDesignerView(reloadedDocument);
            var reloadedWindow = new Window { Content = reloadedView, Width = 1100, Height = 700 };
            reloadedWindow.Show();
            try
            {
                var reloadedSurface = Assert.Single(reloadedView.GetVisualDescendants().OfType<WinFormsDesignSurface>());
                await WaitUntilAsync(() => reloadedSurface.RootForm is not null);
                var reloadedStrip = Assert.Single(reloadedSurface.RootForm!.Controls.OfType<Forms.ToolStrip>());
                var reloadedOwner = Assert.IsAssignableFrom<Forms.ToolStripDropDownItem>(
                    Assert.Single(reloadedStrip.Items.Cast<Forms.ToolStripItem>()));
                Assert.Equal(2, reloadedOwner.DropDownItems.Count);
                var reloadedOpen = Assert.IsType<Forms.ToolStripMenuItem>(reloadedOwner.DropDownItems[0]);
                Assert.Equal("Open", reloadedOpen.Text);
                Assert.IsType<Forms.ToolStripSeparator>(reloadedOwner.DropDownItems[1]);
                var reloadedNested = Assert.IsType<Forms.ToolStripMenuItem>(Assert.Single(reloadedOpen.DropDownItems.Cast<Forms.ToolStripItem>()));
                Assert.Equal("Recent", reloadedNested.Text);
            }
            finally
            {
                reloadedWindow.Close();
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SelectingASplitButtonAlsoShowsAndEditsItsOwnDropDownItems()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", ToolStripSource, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.ToolStrip>()));

            var editor = Editor(view);
            Click(Find<Button>(editor, "ToolStripInlineTypeArrow"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(editor, "ToolStripInlineType_ToolStripSplitButton"));
            await WaitUntilAsync(() => ReadStrip(document).Count == 1 && view.IsEnabled);
            var owner = Assert.Single(ReadStrip(document));
            Assert.Equal("ToolStripSplitButton1", owner.Name);

            editor = Editor(view);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var panelHost = Find<Border>(editor, "ToolStripDropDownHost");
            Assert.True(panelHost.IsVisible, "Selecting a SplitButton must show its own DropDownItems panel too.");

            var typeHere = Find<TextBox>(panelHost, "ToolStripDropDownTypeHere");
            typeHere.Text = "Save As";
            Enter(typeHere);
            await WaitUntilAsync(() => ReadDropDown(document, owner.Name).Count == 1 && view.IsEnabled);
            var saveAs = Assert.Single(ReadDropDown(document, owner.Name));
            Assert.Equal("Save As", saveAs.Text);
            Assert.Contains(
                "Me.ToolStripSplitButton1.DropDownItems.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem1})",
                document.Text);
            var realOwner = Assert.IsType<Forms.ToolStripSplitButton>(
                Assert.Single(Assert.IsType<Forms.ToolStrip>(surface.SelectedControl).Items.Cast<Forms.ToolStripItem>()));
            Assert.Equal("Save As", Assert.IsType<Forms.ToolStripMenuItem>(
                Assert.Single(realOwner.DropDownItems.Cast<Forms.ToolStripItem>())).Text);
        }
        finally
        {
            window.Close();
        }
    }

    private static IReadOnlyList<WinFormsToolStripItem> ReadStrip(EditorDocumentViewModel document) =>
        new WinFormsToolStripDocument(document.Text).Read("ToolStrip1");

    private static IReadOnlyList<WinFormsMenuItem> ReadDropDown(EditorDocumentViewModel document, string ownerName) =>
        new WinFormsMenuDocument(document.Text).Read(ownerName);

    private static WinFormsToolStripDesignEditor Editor(WinFormsDesignerView view) =>
        Assert.Single(view.GetVisualDescendants().OfType<WinFormsToolStripDesignEditor>());

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static void Enter(Control control) => control.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = Key.Enter,
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickUndo(WinFormsDesignerView view)
    {
        var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
            Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
        Click(undo);
    }

    private static (byte Red, byte Green, byte Blue) ReadPixel(Window window, Point point)
    {
        const int width = 1100;
        const int height = 700;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height),
                pin.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            pin.Free();
        }

        var offset = ((int)point.Y * width + (int)point.X) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // 20s: this shares WinFormsMenuInlineTests' and WinFormsToolStripInlineTests' rationale
        // for the same deadline (every AvaloniaFact test shares one dispatcher thread under a
        // full-suite run, so a slow neighbour test can starve this one's RunEditAsync of CPU for
        // several seconds).
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The inline DropDownItems edit did not complete.");
    }
}
