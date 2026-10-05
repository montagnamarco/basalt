using System.Drawing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class WinFormsDesignerModalPropertyViewTests
{
    [AvaloniaTheory]
    [InlineData("BackColor", "ColorCancel")]
    [InlineData("Items", "CollectionCancel")]
    public async Task CancelingAnEllipsisEditorRestoresPropertyFocusAndViewport(string propertyName, string cancelButton)
    {
        var source = propertyName == "Items"
            ? new Basalt.Workspace.WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
                .AddControl("ListBox1", "Global.System.Windows.Forms.ListBox")
            : WinFormsDesignerDocumentTests.Source;
        var document = new EditorDocumentViewModel(
            "MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var owner = new Window { Content = view, Width = 950, Height = 440 };
        owner.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(propertyName == "Items" ? surface.RootForm!.Controls.Find("ListBox1", true).Single()
                : surface.RootForm!.Controls[0]);

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var scroll = Assert.Single(grid.GetVisualDescendants().OfType<ScrollViewer>());
            await WaitUntilAsync(() => scroll.Extent.Height > scroll.Viewport.Height + 70);
            owner.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var row = FindRow(grid, propertyName);
            row.RaiseEvent(new Avalonia.Input.TappedEventArgs(Control.TappedEvent, null!));
            Dispatcher.UIThread.RunJobs();
            row.Focus();
            if (propertyName == "BackColor") scroll.Offset = new Vector(0, 60);
            Dispatcher.UIThread.RunJobs();
            var before = scroll.Offset.Y;
            Assert.True(before > 20);

            var ellipsis = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "PropertyGridEllipsis");
            ellipsis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => owner.OwnedWindows.Any());
            Click(Assert.Single(owner.OwnedWindows), cancelButton);
            await WaitUntilAsync(() => !owner.OwnedWindows.Any());
            await WaitUntilAsync(() => FindRow(grid, propertyName).IsFocused &&
                Math.Abs(scroll.Offset.Y - before) <= 1);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(source, document.Text);
            Assert.InRange(scroll.Offset.Y, before - 1, before + 1);
            Assert.True(FindRow(grid, propertyName).IsFocused);
            Assert.True(scroll.BringIntoViewOnFocusChange);

            if (propertyName == "Items")
            {
                FindRow(grid, propertyName).RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Up,
                });
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(propertyName, grid.SelectedPropertyName);
                Assert.True(scroll.Offset.Y < before - 1);
            }
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ColorAndFontEllipsesCancelOrPersistWithOneUndoEach()
    {
        var original = WinFormsDesignerDocumentTests.Source.Replace(
            "Me.Button1.Tag = DangerousCall()",
            "Me.Button1.Tag = Nothing",
            StringComparison.Ordinal);
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", original, true);
        var view = new WinFormsDesignerView(document);
        var owner = new Window
        {
            Content = view,
            Width = 1100,
            Height = 700,
        };
        owner.Show();

        try
        {
            var surface = view.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0]);
            Assert.Equal("Panel1", surface.SelectedControl!.Name);

            await OpenEditorAsync(view, "BackColor");
            await WaitUntilAsync(() => owner.OwnedWindows.OfType<WinFormsColorEditor>().Any());
            var colorEditor = owner.OwnedWindows.OfType<WinFormsColorEditor>().Single();
            Find<ComboBox>(colorEditor, "ColorKind").SelectedIndex = 2;
            Find<TextBox>(colorEditor, "ColorHex").Text = "#7F123456";
            Click(colorEditor, "ColorCancel");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(original, document.Text);

            await OpenEditorAsync(view, "BackColor");
            await WaitUntilAsync(() => owner.OwnedWindows.OfType<WinFormsColorEditor>().Any());
            colorEditor = owner.OwnedWindows.OfType<WinFormsColorEditor>().Single();
            Find<ComboBox>(colorEditor, "ColorKind").SelectedIndex = 2;
            Find<TextBox>(colorEditor, "ColorHex").Text = "#7F123456";
            Click(colorEditor, "ColorOk");
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.Panel1.BackColor = System.Drawing.Color.FromArgb(127, 18, 52, 86)",
                StringComparison.Ordinal));
            Assert.Equal(Color.FromArgb(127, 18, 52, 86), surface.SelectedControl!.BackColor);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == original);

            await OpenEditorAsync(view, "Font");
            await WaitUntilAsync(() => owner.OwnedWindows.OfType<WinFormsFontEditor>().Any());
            var fontEditor = owner.OwnedWindows.OfType<WinFormsFontEditor>().Single();
            Find<TextBox>(fontEditor, "FontSize").Text = "14.5";
            Find<CheckBox>(fontEditor, "FontBold").IsChecked = true;
            Click(fontEditor, "FontOk");
            await WaitUntilAsync(() => document.Text.Contains("Me.Panel1.Font = New System.Drawing.Font(",
                StringComparison.Ordinal));
            Assert.Equal(14.5f, surface.SelectedControl!.Font.Size);
            Assert.True(surface.SelectedControl.Font.Bold);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == original);
        }
        finally
        {
            view.Dispose();
            owner.Close();
        }
    }

    private static async Task OpenEditorAsync(WinFormsDesignerView view, string propertyName)
    {
        var grid = view.GetVisualDescendants().OfType<PropertyGrid>().Single();
        Assert.Contains(grid.Properties, property => property.Name == propertyName);
        await WaitUntilAsync(() => view.GetVisualDescendants().OfType<Border>()
            .Any(border => border.Name == "PropertyGridRow" &&
                border.GetVisualDescendants().OfType<TextBlock>()
                    .Any(label => label.Text == propertyName)));
        var row = view.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "PropertyGridRow" &&
                border.GetVisualDescendants().OfType<TextBlock>()
                    .Any(label => label.Text == propertyName));
        var button = row.GetVisualDescendants().OfType<Button>()
            .Single(candidate => candidate.Name == "PropertyGridEllipsis");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static T Find<T>(Window dialog, string name) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Border FindRow(PropertyGrid grid, string name) =>
        Assert.Single(grid.Rows.OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static void Click(Window dialog, string name) =>
        Find<Button>(dialog, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickUndo(WinFormsDesignerView view)
    {
        var undo = view.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The modal designer edit did not complete.");
    }
}
