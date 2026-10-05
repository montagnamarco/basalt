using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class WinFormsDesignerDefaultEventTests
{
    [AvaloniaFact]
    public async Task DefaultEventCreationIsUndoableAndReopeningItDoesNotDuplicateOrRewriteCode()
    {
        const string original = "Partial Class MainForm\nEnd Class\n";
        var code = new EditorDocumentViewModel("MainForm.vb", original);
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var view = new WinFormsDesignerView(designer, _ => Task.FromResult(code));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var canvas = Assert.IsType<Canvas>(typeof(WinFormsDesignSurface)
                .GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(surface));
            var point = canvas.TranslatePoint(new Point(350, 230), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await WaitUntilAsync(() => code.Text.Contains("Handles Me.Load", StringComparison.Ordinal));
            await WaitUntilAsync(() => view.IsEnabled);
            var created = code.Text;
            Assert.Contains("MainForm_Load", created);
            Assert.Equal(WinFormsDesignerDocumentTests.Source, designer.Text);
            var nextPoint = canvas.TranslatePoint(new Point(390, 270), window)!.Value;
            window.MouseDown(nextPoint, MouseButton.Left);
            window.MouseUp(nextPoint, MouseButton.Left);
            window.MouseDown(nextPoint, MouseButton.Left);
            window.MouseUp(nextPoint, MouseButton.Left);
            await WaitUntilAsync(() => view.IsEnabled);
            Assert.Equal(created, code.Text);
            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), item =>
                Equals(item.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => code.Text == original);
            var redo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), item =>
                Equals(item.Content, Localizer.Get(StringKeys.MenuEditRedo)));
            redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => code.Text == created);
            Assert.Equal(WinFormsDesignerDocumentTests.Source, designer.Text);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 150 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }
}
