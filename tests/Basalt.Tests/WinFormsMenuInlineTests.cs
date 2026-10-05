using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
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

public sealed class WinFormsMenuInlineTests
{
    private const string Source = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.MenuStrip1 = New Global.System.Windows.Forms.MenuStrip()
                Me.MenuStrip1.Name = "MenuStrip1"
                Me.Controls.Add(Me.MenuStrip1)
                ' Keep application initialization.
            End Sub
            Friend WithEvents MenuStrip1 As Global.System.Windows.Forms.MenuStrip
        End Class
        """;

    [AvaloniaFact]
    public void DoubleTapOnBuiltInItemRequestsItsDefaultEvent()
    {
        var editor = new WinFormsMenuDesignEditor();
        var item = new WinFormsMenuItem("FileMenu", "MenuStrip1", "&File");
        editor.Show("MenuStrip1", [item], 320);
        string? requested = null;
        editor.DefaultEventRequested += name => requested = name;

        var button = Find<Button>(editor, "MenuInlineItem_FileMenu");
        button.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        Assert.Equal("FileMenu", requested);
        Assert.Equal("FileMenu", editor.SelectedItemName);
    }

    [AvaloniaFact]
    public async Task InlineTypeHereEditsNestedMenuRendersDropdownAndUndoesEachAction()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.MenuStrip>()));

            var editor = Editor(view);
            var topPlaceholder = Find<TextBox>(editor, "MenuInlineTopPlaceholder");
            Assert.Equal(Localizer.Get("MenuInline_TypeHere"), topPlaceholder.PlaceholderText);
            topPlaceholder.Text = "&File";
            Enter(topPlaceholder);
            await WaitUntilAsync(() => Read(document).Count == 1 && view.IsEnabled);
            var file = Assert.Single(Read(document));
            Assert.Equal("&File", file.Text);
            Assert.Equal("MenuStrip1", file.Parent);
            Assert.Equal("&File", Assert.IsType<Forms.ToolStripMenuItem>(
                Assert.Single(Assert.IsType<Forms.MenuStrip>(surface.SelectedControl).Items
                    .Cast<Forms.ToolStripItem>())).Text);
            var afterFile = document.Text;

            editor = Editor(view);
            var fileButton = Find<Button>(editor, "MenuInlineItem_" + file.Name);
            fileButton.BringIntoView();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var point = fileButton.TranslatePoint(new Point(fileButton.Bounds.Width / 2, fileButton.Bounds.Height / 2),
                window)!.Value;
            var designerScroll = Assert.Single(surface.GetVisualAncestors().OfType<ScrollViewer>());
            var scrollOrigin = designerScroll.TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.InRange(point.X, scrollOrigin.X, scrollOrigin.X + designerScroll.Bounds.Width);
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("MenuStrip1", surface.SelectedControl?.Name);
            Assert.Equal(file.Name, editor.SelectedItemName);
            var dropdown = Find<Border>(editor, "MenuInlineDropdown");
            Assert.True(dropdown.IsVisible, "Clicking the top-level item must open its inline dropdown.");
            var borderPoint = dropdown.TranslatePoint(new Point(0, 12), window)!.Value;
            var border = ReadPixel(window, borderPoint);
            Assert.True(border.Blue > border.Red + 30,
                $"The real dropdown border should paint blue at {borderPoint}, dropdown={dropdown.Bounds}; RGB={border.Red},{border.Green},{border.Blue}.");
            var propertyGrid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => propertyGrid.Properties.Any(property => property.Name == "Checked") &&
                propertyGrid.Properties.Any(property => property.Name == "Text" && property.CurrentValue == "&File"));

            var childPlaceholder = Find<TextBox>(editor, "MenuInlineChildPlaceholder");
            childPlaceholder.Text = "&Open";
            Enter(childPlaceholder);
            await WaitUntilAsync(() => Read(document).Count == 2 && view.IsEnabled);
            var open = Read(document).Single(item => item.Parent == file.Name);
            Assert.Equal("&Open", open.Text);
            var afterOpen = document.Text;

            editor = Editor(view);
            Click(Find<Button>(editor, "MenuInlineOpen_" + open.Name));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(editor.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "File › Open");
            Assert.NotNull(Find<TextBox>(editor, "MenuInlineChildPlaceholder"));
            Click(Find<Button>(editor, "MenuInlineBack"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(afterOpen, document.Text);

            Click(Find<Button>(editor, "MenuInlineSeparator"));
            await WaitUntilAsync(() => Read(document).Count == 3 && view.IsEnabled);
            Assert.True(Read(document).Last().IsSeparator);
            Assert.Equal(file.Name, Read(document).Last().Parent);
            var afterSeparator = document.Text;

            editor = Editor(view);
            Assert.True(Find<Border>(editor, "MenuInlineDropdown").IsVisible,
                "The dropdown must remain open while its separator edit is committed.");
            await WaitUntilAsync(() =>
            {
                window.UpdateLayout();
                return editor.GetVisualDescendants().OfType<Button>()
                    .Any(button => button.Name == "MenuInlineItem_" + open.Name);
            });
            Click(Find<Button>(editor, "MenuInlineItem_" + open.Name));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(open.Name, editor.SelectedItemName);
            Assert.True(Find<Border>(editor, "MenuInlineDropdown").IsVisible,
                "Selecting the nested item must keep its parent dropdown open.");
            var caption = Find<TextBox>(editor, "MenuInlineCaption");
            caption.Text = "Open &Recent";
            Enter(caption);
            await WaitUntilAsync(() => Read(document).Single(item => item.Name == open.Name).Text == "Open &Recent"
                && view.IsEnabled);

            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterSeparator && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterOpen && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == afterFile && view.IsEnabled);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == Source && view.IsEnabled);
            Assert.Contains("' Keep application initialization.", document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InlineEditorHidesForOtherControlsAndDoesNotCaptureDesignerMovement()
    {
        var source = new WinFormsDesignerDocument(Source).AddControl(
            "Panel1", "Global.System.Windows.Forms.Panel");
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.MenuStrip>()));
            Assert.NotNull(Editor(view));

            surface.Select(Assert.Single(surface.RootForm.Controls.OfType<Forms.Panel>()));
            Assert.Empty(view.GetVisualDescendants().OfType<WinFormsMenuDesignEditor>());
            var before = surface.SelectedControl!.Bounds;
            surface.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Right,
            });
            await WaitUntilAsync(() => document.Text != source);
            Assert.Equal(before.Left + 1, surface.SelectedControl?.Left);
        }
        finally
        {
            window.Close();
        }
    }

    private static IReadOnlyList<WinFormsMenuItem> Read(EditorDocumentViewModel document) =>
        new WinFormsMenuDocument(document.Text).Read("MenuStrip1");

    private static WinFormsMenuDesignEditor Editor(WinFormsDesignerView view) =>
        Assert.Single(view.GetVisualDescendants().OfType<WinFormsMenuDesignEditor>());

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
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The inline menu edit did not complete.");
    }
}
