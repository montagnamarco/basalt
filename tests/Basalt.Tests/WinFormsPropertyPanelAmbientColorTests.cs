using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Visual Studio's Properties grid names an unset ambient colour by its
/// WinForms default ("Control", "ControlText"), not the plain RGB the
/// design surface paints with when portable visual styles stand in for a
/// real system theme (Control.PortableVisualStylesEnabled).
/// </summary>
public sealed class WinFormsPropertyPanelAmbientColorTests
{
    [AvaloniaFact]
    public async Task UnsetButtonColorsShowTheirWinFormsDeclaredDefaultByNameNotBold()
    {
        var source = WinFormsDesignerDocumentTests.Source;
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());

            AssertUnsetColor(grid, "BackColor", "Control");
            AssertUnsetColor(grid, "ForeColor", "ControlText");

            // Red-prove the root cause is still there under the fix: the live getter
            // the inspector must not fall back to still answers with the portable
            // visual styles palette's plain RGB, not a named system colour. If this
            // assertion ever fails, the getter itself changed and painting is at risk.
            var liveBackColorText = System.ComponentModel.TypeDescriptor.GetConverter(typeof(System.Drawing.Color))
                .ConvertToInvariantString(surface.SelectedControl!.BackColor);
            Assert.NotEqual("Control", liveBackColorText);

            var backColorBox = ValueBox(grid, "BackColor");
            backColorBox.Text = "Red";
            backColorBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.Button1.BackColor = System.Drawing.Color.Red", StringComparison.Ordinal));

            var setBox = ValueBox(grid, "BackColor");
            Assert.Equal("Red", setBox.Text);
            Assert.Equal(FontWeight.Bold, setBox.FontWeight);

            var reset = ResetMenuItem(grid, "BackColor");
            Assert.True(reset.IsEnabled);
            reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => !document.Text.Contains("Me.Button1.BackColor", StringComparison.Ordinal));

            AssertUnsetColor(grid, "BackColor", "Control");
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertUnsetColor(PropertyGrid grid, string propertyName, string expected)
    {
        var property = Assert.Single(grid.Properties, p => p.Name == propertyName);
        Assert.False(property.IsSet);
        var box = ValueBox(grid, propertyName);
        Assert.Equal(expected, box.Text);
        Assert.Equal(FontWeight.Normal, box.FontWeight);
    }

    private static TextBox ValueBox(PropertyGrid grid, string propertyName) =>
        Assert.Single(Row(grid, propertyName).GetVisualDescendants().OfType<TextBox>(), box => box.Name == "PropertyGridValue");

    private static MenuItem ResetMenuItem(PropertyGrid grid, string propertyName) =>
        Assert.Single(Row(grid, propertyName).ContextMenu!.Items.OfType<MenuItem>());

    private static Control Row(PropertyGrid grid, string propertyName)
    {
        var label = grid.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == propertyName);
        return label.GetVisualAncestors().OfType<Control>().First(control => control.ContextMenu is not null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The designer did not finish the ambient colour operation.");
    }
}
