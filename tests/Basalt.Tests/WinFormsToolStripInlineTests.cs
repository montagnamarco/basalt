using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
/// Proves the ToolStrip/StatusStrip "Type Here" inline editor end to end, the same way
/// <see cref="WinFormsMenuInlineTests"/> proves the MenuStrip one: a real form opens in the
/// designer, the strip is selected, items are added through both the text box and the type
/// drop-down, each add is one undo step, the newly added item becomes the Properties selection,
/// and the overlay is rasterised to prove it actually paints over the strip.
/// </summary>
public sealed class WinFormsToolStripInlineTests
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

    private const string StatusStripSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.StatusStrip1 = New Global.System.Windows.Forms.StatusStrip()
                Me.StatusStrip1.Name = "StatusStrip1"
                Me.Controls.Add(Me.StatusStrip1)
                ' Keep application initialization.
            End Sub
            Friend WithEvents StatusStrip1 As Global.System.Windows.Forms.StatusStrip
        End Class
        """;

    [AvaloniaFact]
    public async Task InlineTypeHereAndDropdownAddToolStripItemsRenderPaintedEditorAndUndoEachStep()
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
            var placeholder = Find<TextBox>(editor, "ToolStripInlineTypeHere");
            Assert.Equal(Localizer.Get(StringKeys.MenuInlineTypeHere), placeholder.PlaceholderText);

            // (e) Rasterise before any item exists: the bar that hosts "Type Here" must already
            // paint its DodgerBlue border, proving the overlay itself is really drawn, not just
            // logically present in the tree.
            var bar = Find<Border>(editor, "ToolStripInlineBar");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var barPoint = bar.TranslatePoint(new Point(0, bar.Bounds.Height / 2), window)!.Value;
            var barBorder = ReadPixel(window, barPoint);
            Assert.True(barBorder.Blue > barBorder.Red + 30,
                $"The Type Here bar border should paint DodgerBlue at {barPoint}; RGB={barBorder.Red},{barBorder.Green},{barBorder.Blue}.");

            // (a) Typing text in "Type Here" and pressing Enter adds a ToolStripButton.
            placeholder.Text = "&Save";
            Enter(placeholder);
            await WaitUntilAsync(() => Read(document, "ToolStrip1").Count == 1 && view.IsEnabled);
            var button = Assert.Single(Read(document, "ToolStrip1"));
            Assert.Equal("ToolStripButton1", button.Name);
            Assert.Equal("ToolStripButton", button.TypeName);
            Assert.Equal("&Save", button.Text);
            Assert.Contains("Friend WithEvents ToolStripButton1 As Global.System.Windows.Forms.ToolStripButton",
                document.Text);
            var realButton = Assert.IsType<Forms.ToolStripButton>(
                Assert.Single(Assert.IsType<Forms.ToolStrip>(surface.SelectedControl).Items.Cast<Forms.ToolStripItem>()));
            Assert.Equal("&Save", realButton.Text);
            var afterButton = document.Text;

            // (d) The added item becomes the editor's and the Properties panel's selection.
            editor = Editor(view);
            Assert.Equal("ToolStripButton1", editor.SelectedItemName);
            var propertyGrid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() =>
                propertyGrid.Properties.Any(property => property.Name == "Name" && property.DefaultValue == "ToolStripButton1") &&
                propertyGrid.Properties.Any(property => property.Name == "Text" && property.CurrentValue == "&Save"));

            // (b) The arrow opens a drop-down listing every ToolStrip item kind.
            var arrow = Find<Button>(editor, "ToolStripInlineTypeArrow");
            Click(arrow);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var dropdown = Find<Border>(editor, "ToolStripInlineDropdown");
            Assert.True(dropdown.IsVisible, "Clicking the arrow must open the type drop-down.");
            foreach (var typeName in new[]
                     {
                         "ToolStripButton", "ToolStripLabel", "ToolStripSplitButton", "ToolStripDropDownButton",
                         "ToolStripSeparator", "ToolStripComboBox", "ToolStripTextBox", "ToolStripProgressBar",
                     })
            {
                Assert.NotNull(Find<Button>(editor, "ToolStripInlineType_" + typeName));
            }

            var dropdownPoint = dropdown.TranslatePoint(new Point(0, 10), window)!.Value;
            var dropdownBorder = ReadPixel(window, dropdownPoint);
            Assert.True(dropdownBorder.Blue > dropdownBorder.Red + 30,
                $"The real drop-down border should paint DodgerBlue at {dropdownPoint}; RGB={dropdownBorder.Red},{dropdownBorder.Green},{dropdownBorder.Blue}.");

            // Choosing Separator from the drop-down adds one, with no Text assignment.
            Click(Find<Button>(editor, "ToolStripInlineType_ToolStripSeparator"));
            await WaitUntilAsync(() => Read(document, "ToolStrip1").Count == 2 && view.IsEnabled);
            var separator = Read(document, "ToolStrip1")[1];
            Assert.Equal("ToolStripSeparator", separator.TypeName);
            Assert.DoesNotContain(separator.Name + ".Text", document.Text);
            var afterSeparator = document.Text;

            editor = Editor(view);
            Assert.Equal(separator.Name, editor.SelectedItemName);

            // Choosing ComboBox from the drop-down adds one, also with no Text assignment.
            Click(Find<Button>(editor, "ToolStripInlineTypeArrow"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(editor, "ToolStripInlineType_ToolStripComboBox"));
            await WaitUntilAsync(() => Read(document, "ToolStrip1").Count == 3 && view.IsEnabled);
            var comboBox = Read(document, "ToolStrip1")[2];
            Assert.Equal("ToolStripComboBox", comboBox.TypeName);
            Assert.DoesNotContain(comboBox.Name + ".Text", document.Text);
            var afterComboBox = document.Text;

            editor = Editor(view);
            Assert.Equal(comboBox.Name, editor.SelectedItemName);

            // (c) Each add is its own undo step.
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterSeparator && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterButton && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == ToolStripSource && view.IsEnabled);
            Assert.Contains("' Keep application initialization.", document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InlineTypeHereAndDropdownAddStatusStripItemsShowStatusStripKinds()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", StatusStripSource, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.StatusStrip>()));

            var editor = Editor(view);
            var placeholder = Find<TextBox>(editor, "ToolStripInlineTypeHere");

            // (e) The Type Here bar for a StatusStrip paints the same way a ToolStrip's does.
            var bar = Find<Border>(editor, "ToolStripInlineBar");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var barPoint = bar.TranslatePoint(new Point(0, bar.Bounds.Height / 2), window)!.Value;
            var barBorder = ReadPixel(window, barPoint);
            Assert.True(barBorder.Blue > barBorder.Red + 30,
                $"The Type Here bar border should paint DodgerBlue at {barPoint}; RGB={barBorder.Red},{barBorder.Green},{barBorder.Blue}.");

            // (a) Typing into "Type Here" on a StatusStrip creates a ToolStripStatusLabel.
            placeholder.Text = "Ready";
            Enter(placeholder);
            await WaitUntilAsync(() => Read(document, "StatusStrip1").Count == 1 && view.IsEnabled);
            var label = Assert.Single(Read(document, "StatusStrip1"));
            Assert.Equal("ToolStripStatusLabel1", label.Name);
            Assert.Equal("ToolStripStatusLabel", label.TypeName);
            Assert.Equal("Ready", label.Text);
            var afterLabel = document.Text;

            // (d) Selection.
            editor = Editor(view);
            Assert.Equal("ToolStripStatusLabel1", editor.SelectedItemName);
            var propertyGrid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() =>
                propertyGrid.Properties.Any(property => property.Name == "Text" && property.CurrentValue == "Ready"));

            // (b) The drop-down for a StatusStrip lists its own kinds, not the ToolStrip's.
            Click(Find<Button>(editor, "ToolStripInlineTypeArrow"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var dropdown = Find<Border>(editor, "ToolStripInlineDropdown");
            Assert.True(dropdown.IsVisible);
            foreach (var typeName in new[]
                     {
                         "ToolStripStatusLabel", "ToolStripProgressBar", "ToolStripDropDownButton", "ToolStripSplitButton",
                     })
            {
                Assert.NotNull(Find<Button>(editor, "ToolStripInlineType_" + typeName));
            }
            Assert.Null(FindOrNull<Button>(editor, "ToolStripInlineType_ToolStripComboBox"));

            Click(Find<Button>(editor, "ToolStripInlineType_ToolStripProgressBar"));
            await WaitUntilAsync(() => Read(document, "StatusStrip1").Count == 2 && view.IsEnabled);
            var progressBar = Read(document, "StatusStrip1")[1];
            Assert.Equal("ToolStripProgressBar", progressBar.TypeName);

            // (c) Undo removes the most recent add first, then the first.
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterLabel && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == StatusStripSource && view.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private static IReadOnlyList<WinFormsToolStripItem> Read(EditorDocumentViewModel document, string strip) =>
        new WinFormsToolStripDocument(document.Text).Read(strip);

    private static WinFormsToolStripDesignEditor Editor(WinFormsDesignerView view) =>
        Assert.Single(view.GetVisualDescendants().OfType<WinFormsToolStripDesignEditor>());

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static T? FindOrNull<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.Name == name);

    private static void Enter(Control control) => control.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = Key.Enter,
    });

    private static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

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
        // 20s: this shares WinFormsMenuInlineTests' rationale for the same deadline (every
        // AvaloniaFact test shares one dispatcher thread under a full-suite run, so a slow
        // neighbour test can starve this one's RunEditAsync of CPU for several seconds).
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The inline ToolStrip/StatusStrip edit did not complete.");
    }
}
