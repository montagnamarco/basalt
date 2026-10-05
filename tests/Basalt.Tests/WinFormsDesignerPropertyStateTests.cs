using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class WinFormsDesignerPropertyStateTests
{
    [AvaloniaFact]
    public async Task ExplicitDefaultIsNotModifiedButUnresolvedSourceRemainsResettable()
    {
        var source = WinFormsDesignerDocumentTests.Source.Replace(
            "Me.Button1.Text = \"Before\"",
            "Me.Button1.Text = \"Changed\"\n        Me.Button1.Enabled = True\n        Me.Button1.Tag = UnknownValue()",
            StringComparison.Ordinal);
        Assert.Contains("UnknownValue()", source);
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            for (var attempt = 0; surface.RootForm is null && attempt < 200; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            Assert.NotNull(surface.RootForm);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var enabled = Assert.Single(grid.Properties, property => property.Name == "Enabled");
            Assert.False(enabled.IsSet);
            Assert.False(enabled.CanReset);
            var text = Assert.Single(grid.Properties, property => property.Name == "Text");
            Assert.True(text.IsSet);
            Assert.True(text.CanReset);
            var tag = Assert.Single(grid.Properties, property => property.Name == "Tag");
            Assert.True(tag.IsSet);
            Assert.True(tag.CanReset);
            Assert.Equal(source, document.Text);
            // Rows keep their property in an internal entry, so locate the visible
            // row by its label rather than relying on that implementation detail.
            var label = grid.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => block.Text == "Tag");
            var row = label.GetVisualAncestors().OfType<Control>()
                .First(control => control.ContextMenu is not null);
            var reset = Assert.Single(row.ContextMenu!.Items.OfType<MenuItem>());
            Assert.True(reset.IsEnabled);
            reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (var attempt = 0; document.Text == source && attempt < 200; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Assert.DoesNotContain("Me.Button1.Tag =", document.Text);
            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, Basalt.Core.Localization.Localizer.Get(
                    Basalt.Core.Localization.StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; document.Text != source && attempt < 200; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Assert.Equal(source, document.Text);

        }
        finally
        {
            window.Close();
        }
    }
}
