using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Regression coverage for <see cref="WinFormsPropertyPanel"/>'s view-state restoration:
/// after a modal property editor commits a value, the property grid must keep its scroll
/// offset and focused row, even when the owner window is re-activated afterwards (the
/// sequence a window manager produces once a modal dialog closes).
/// </summary>
public sealed class WinFormsPropertyPanelViewStateTests
{
    [AvaloniaFact]
    public async Task CommittingAColorEditorKeepsScrollOffsetAndFocusAcrossOwnerReactivation()
    {
        var document = new EditorDocumentViewModel(
            "MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var view = new WinFormsDesignerView(document);
        var owner = new Window { Content = view, Width = 950, Height = 440 };
        owner.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0]);

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var scroll = Assert.Single(grid.GetVisualDescendants().OfType<ScrollViewer>());
            await WaitUntilAsync(() => scroll.Extent.Height > scroll.Viewport.Height + 70);
            owner.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var row = FindRow(grid, "BackColor");
            row.RaiseEvent(new TappedEventArgs(Control.TappedEvent, null!));
            Dispatcher.UIThread.RunJobs();
            row.Focus();

            // Scroll the focused row out of view: re-focusing it with automatic
            // bring-into-view would move the viewport away from the user's position.
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
            Dispatcher.UIThread.RunJobs();
            var before = scroll.Offset.Y;
            Assert.True(before > 20);
            Assert.True(FindRow(grid, "BackColor").IsFocused);

            // A window manager deactivates the owner while the modal editor owns
            // activation, so the panel must lift its focus-scroll suppression on the
            // owner's later Activated event, not on an "already active" shortcut.
            Deactivate(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.False(owner.IsActive);

            var suppressedFocusScroll = false;
            using var watch = scroll.GetObservable(ScrollViewer.BringIntoViewOnFocusChangeProperty)
                .Subscribe(new ActionObserver(enabled => suppressedFocusScroll |= !enabled));

            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => owner.OwnedWindows.OfType<WinFormsColorEditor>().Any());
            var colorEditor = owner.OwnedWindows.OfType<WinFormsColorEditor>().Single();
            Find<ComboBox>(colorEditor, "ColorKind").SelectedIndex = 2;
            Find<TextBox>(colorEditor, "ColorHex").Text = "#7F123456";
            Click(colorEditor, "ColorOk");
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.Panel1.BackColor = System.Drawing.Color.FromArgb(127, 18, 52, 86)",
                StringComparison.Ordinal));
            await WaitUntilAsync(() => !owner.OwnedWindows.Any());
            await WaitUntilAsync(() => FindRow(grid, "BackColor").IsFocused);
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(scroll.Offset.Y, before - 1, before + 1);
            Assert.True(FindRow(grid, "BackColor").IsFocused);

            // Focus-driven scrolling was suppressed while the focus was given back,
            // otherwise the window manager's refocus would drag the viewport to the row.
            Assert.True(suppressedFocusScroll);

            // The owner is given back activation after the commit finished.
            owner.Activate();
            await WaitUntilAsync(() => scroll.BringIntoViewOnFocusChange);
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(scroll.Offset.Y, before - 1, before + 1);
            Assert.True(FindRow(grid, "BackColor").IsFocused);
            Assert.True(scroll.BringIntoViewOnFocusChange);
        }
        finally
        {
            owner.Close();
        }
    }

    private sealed class ActionObserver(Action<bool> onNext) : IObserver<bool>
    {
        public void OnNext(bool value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    private static void Deactivate(WindowBase window)
    {
        var method = typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WindowBase.HandleDeactivated was not found.");
        method.Invoke(window, null);
    }

    private static T Find<T>(Window dialog, string name) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Border FindRow(PropertyGrid grid, string name) =>
        Assert.Single(grid.Rows.OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static void Click(Window dialog, string name) =>
        Find<Button>(dialog, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The modal property edit did not complete.");
    }
}
