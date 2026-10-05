using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Basalt.Tests;

public sealed class DesignerFloatingDockTests
{
    [AvaloniaFact]
    public async Task FloatingPropertiesDockKeepsDesignerSessionAndAllowsEditingAfterRedocking()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-floating-dock-").FullName;
        try
        {
            var formsPath = Path.Combine(folder, "MainForm.Designer.vb");
            await File.WriteAllTextAsync(formsPath, WinFormsDesignerDocumentTests.Source);

            using var window = new TestWindow();
            var model = Assert.IsType<MainWindowViewModel>(window.Window.DataContext);
            await model.OpenFileAsync(formsPath, inDesigner: true);
            await window.SettleAsync();

            var factory = PrivateField<IdeDockFactory>(window.Window, "_factory");
            var propertiesHost = PrivateField<ContentControl>(window.Window, "_designerPropertiesDock");
            var propertiesPanel = Assert.IsType<WinFormsPropertyPanel>(propertiesHost.Content);
            var host = Assert.Single(window.Window.GetVisualDescendants().OfType<DesignerHost>());
            var designer = Assert.Single(host.GetVisualDescendants().OfType<WinFormsDesignerView>());
            var surface = Assert.Single(designer.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var selected = surface.SelectedControl;
            var rootForm = surface.RootForm;
            var session = PrivateField<WinFormsDesignerSession>(designer, "_session");
            var originalDock = Assert.IsAssignableFrom<IDock>(factory.Properties.Owner);
            var mainRoot = Assert.IsAssignableFrom<IRootDock>(factory.FindRoot(factory.Properties));
            var originalWindowCount = mainRoot.Windows?.Count ?? 0;

            // Exercise Dock's factory operation, rather than manually reparenting a panel.
            factory.FloatDockable(factory.Properties);
            await window.SettleAsync();

            var floatingWindow = Assert.Single(mainRoot.Windows!.Skip(originalWindowCount));
            Assert.NotNull(floatingWindow.Host);
            Assert.NotSame(originalDock, factory.Properties.Owner);
            Assert.Same(mainRoot, floatingWindow.Owner);
            Assert.Same(factory.FindRoot(factory.Properties), floatingWindow.Layout);
            Assert.Same(propertiesPanel, propertiesHost.Content);
            Assert.Same(session, PrivateField<WinFormsDesignerSession>(designer, "_session"));
            Assert.Same(rootForm, surface.RootForm);
            Assert.Same(selected, surface.SelectedControl);

            var row = Assert.Single(propertiesPanel.GetVisualDescendants().OfType<Border>(), border =>
                border.Name == "PropertyGridRow" &&
                border.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Text"));
            var editor = Assert.Single(row.GetVisualDescendants().OfType<TextBox>());
            editor.Text = "Edited while floating";
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
            await WaitUntilAsync(() => model.ActiveDocument?.Text.Contains(
                "Me.Button1.Text = \"Edited while floating\"", StringComparison.Ordinal) == true);
            Assert.Equal("Edited while floating", surface.SelectedControl!.Text);

            var floatingDock = Assert.IsAssignableFrom<IDock>(factory.Properties.Owner);
            factory.MoveDockable(floatingDock, originalDock, factory.Properties, factory.Events);
            await window.SettleAsync();

            Assert.Same(originalDock, factory.Properties.Owner);
            Assert.Equal(originalWindowCount, mainRoot.Windows?.Count ?? 0);
            Assert.Same(propertiesPanel, propertiesHost.Content);
            Assert.Same(session, PrivateField<WinFormsDesignerSession>(designer, "_session"));
            Assert.Equal("Button1", surface.SelectedControl!.Name);
            Assert.Equal("Edited while floating", surface.SelectedControl.Text);
            model.UndoCommand.Execute(null);
            await window.SettleAsync();
            Assert.Equal("Before", surface.SelectedControl!.Text);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static T PrivateField<T>(object owner, string name)
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field.GetValue(owner));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The floating property edit was not saved.");
    }
}
