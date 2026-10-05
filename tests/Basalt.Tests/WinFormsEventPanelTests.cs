using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using System.ComponentModel;

namespace Basalt.Tests;

public class WinFormsEventPanelTests
{
    [AvaloniaFact]
    public void HandlerEditsAndRemovalAreSentToTheDocumentOwner()
    {
        var panel = new WinFormsEventPanel();
        var requests = new List<(string Event, string? Handler)>();
        panel.Show(new WinFormsEventInspectorTests.TestComponent(), _ => "ExistingHandler",
            (name, handler) => requests.Add((name, handler)));
        var focusTarget = new Button();
        var window = new Window
        {
            Width = 420,
            Height = 400,
            Content = new StackPanel { Children = { panel, focusTarget } }
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var editor = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("ExistingHandler", editor.Text);
            editor.Focus();
            editor.Text = "NewHandler";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            focusTarget.Focus();
            Assert.Equal(("Changed", "NewHandler"), Assert.Single(requests));
            requests.Clear();
            editor.Focus();
            editor.Text = "DiscardedHandler";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal("NewHandler", editor.Text);
            Assert.Empty(requests);
            editor.Text = "";
            focusTarget.Focus();
            Assert.Equal(("Changed", (string?)null), Assert.Single(requests));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DoubleClickRequestsSuggestedHandlerOrNavigatesToExistingHandler()
    {
        var panel = new WinFormsEventPanel();
        var requests = new List<(string Event, string? Handler)>();
        var component = new WinFormsEventInspectorTests.TestComponent();
        panel.Show(component, null, (name, handler) => requests.Add((name, handler)), name => "Control1_" + name);
        var window = new Window { Content = panel, Width = 420, Height = 400 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var editor = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            editor.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
            Assert.Equal(("Changed", "Control1_Changed"), Assert.Single(requests));
            requests.Clear();
            panel.Show(component, _ => "ExistingHandler", (name, handler) => requests.Add((name, handler)));
            Dispatcher.UIThread.RunJobs();
            editor = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            editor.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
            Assert.Equal(("Changed", "ExistingHandler"), Assert.Single(requests));
            panel.Show(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(panel.GetVisualDescendants().OfType<TextBox>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void VisualStudioStyleGroupsEventsAndShowsSelectionHelpWithoutChangingHandlerSemantics()
    {
        var panel = new WinFormsEventPanel { VisualStudioStyle = true };
        var requests = new List<(string Event, string? Handler)>();
        panel.Show(new CategorizedEvents(), null, (name, handler) => requests.Add((name, handler)));
        var window = new Window { Content = panel, Width = 420, Height = 360, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var category = panel.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "EventCategory_Action");
            Assert.Equal(2, panel.GetVisualDescendants().OfType<TextBox>().Count());
            AssertCategoryPixel(window, category, 248);

            var changed = panel.GetVisualDescendants().OfType<TextBox>().First();
            changed.Focus();
            var description = panel.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "EventDescription");
            Assert.Equal(["Changed", "Raised after a change"],
                description.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
            changed.Text = "OnChanged";
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(changed, panel.GetVisualDescendants().OfType<TextBox>().First());
            Assert.Equal("OnChanged", changed.Text);
            AssertCategoryPixel(window, category, 24);
            changed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Contains(("Changed", "OnChanged"), requests);
            category.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            category = panel.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "EventCategory_Action");
            category.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, panel.GetVisualDescendants().OfType<TextBox>().Count());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RetiredEventEditorCannotCommitIntoTheNewSelection()
    {
        var panel = new WinFormsEventPanel { VisualStudioStyle = true };
        var firstRequests = new List<(string Event, string? Handler)>();
        var secondRequests = new List<(string Event, string? Handler)>();
        panel.Show(new WinFormsEventInspectorTests.TestComponent(),
            handlerRequested: (name, handler) => firstRequests.Add((name, handler)));
        var window = new Window { Content = panel, Width = 420, Height = 360 };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var retired = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            retired.Focus();
            retired.Text = "DraftForFirst";

            panel.Show(new WinFormsEventInspectorTests.TestComponent(),
                handlerRequested: (name, handler) => secondRequests.Add((name, handler)));
            retired.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.Empty(secondRequests);
            Assert.Empty(firstRequests);

            var active = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            active.Text = "HandlerForSecond";
            active.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(("Changed", "HandlerForSecond"), Assert.Single(secondRequests));
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertCategoryPixel(Window window, Button category, byte expected)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(420, 360));
        bitmap.Render(window);
        var pixels = new byte[420 * 360 * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 420, 360),
                handle.AddrOfPinnedObject(), pixels.Length, 420 * 4);
        }
        finally
        {
            handle.Free();
        }

        var origin = category.TranslatePoint(new Point(), window)!.Value;
        var pixel = ((int)(origin.Y + 10) * 420 + (int)(origin.X + 180)) * 4;
        Assert.InRange(pixels[pixel], expected - 3, expected + 3);
        Assert.InRange(pixels[pixel + 1], expected - 3, expected + 3);
        Assert.InRange(pixels[pixel + 2], expected - 3, expected + 3);
    }

    private sealed class CategorizedEvents
    {
        [Category("Action"), Description("Raised after a change")]
        public event EventHandler? Changed;

        [Category("Other")]
        public event EventHandler? Opened;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);
    }
}
