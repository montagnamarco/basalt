using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsDesignerResourceEditingTests
{
    private const string Resources = """
        <root>
          <!-- retain this comment -->
          <data name="Button1.Text"><value>Resource text</value><comment>Translator note</comment></data>
          <data name="Button1.Location" type="System.Drawing.Point, System.Drawing"><value>40, 50</value></data>
          <data name="Button1.Size" type="System.Drawing.Size, System.Drawing"><value>120, 35</value></data>
          <data name="Unrelated" type="Custom.Type, Custom.Assembly"><value>Opaque content</value></data>
        </root>
        """;

    internal static string Source => WinFormsDesignerDocumentTests.Source
        .Replace("Private Sub InitializeComponent()",
            "Private Sub InitializeComponent()\n        Dim resources As New System.ComponentModel.ComponentResourceManager(GetType(MainForm))",
            StringComparison.Ordinal)
        .Replace("Me.Button1.Text = \"Before\"", "resources.ApplyResources(Me.Button1, \"Button1\")",
            StringComparison.Ordinal);

    [AvaloniaFact]
    public async Task PropertyGridEditsAndResetsTheResourceWithLinkedUndoAcrossReopenedBuffers()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", Source, true);
        var resourceBuffer = new EditorDocumentViewModel("MainForm.resx", Resources);
        using var view = new WinFormsDesignerView(designer,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? resourceBuffer : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var editor = Assert.Single(FindRow(grid, "Text").GetVisualDescendants().OfType<TextBox>(),
                box => box.Name == "PropertyGridValue");
            editor.Text = "Edited resource";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => surface.SelectedControl?.Text == "Edited resource" && view.IsEnabled);
            var edited = resourceBuffer.Text;
            Assert.Equal("Edited resource", new WinFormsResourceDocument(edited).TryGet("Button1.Text")!.Value);
            Assert.Contains("Translator note", edited);
            Assert.Contains("<data name=\"Unrelated\" type=\"Custom.Type, Custom.Assembly\"><value>Opaque content</value></data>", edited);
            Assert.Equal(Source, designer.Text);
            Assert.False(designer.IsModified);
            Assert.True(resourceBuffer.IsModified);

            var closedBuffer = resourceBuffer;
            resourceBuffer = new EditorDocumentViewModel("MainForm.resx", edited);
            ClickHistory(view, StringKeys.MenuEditUndo);
            await WaitUntilAsync(() => resourceBuffer.Text == Resources && surface.SelectedControl?.Text == "Resource text");
            Assert.Equal(edited, closedBuffer.Text);
            ClickHistory(view, StringKeys.MenuEditRedo);
            await WaitUntilAsync(() => resourceBuffer.Text == edited && surface.SelectedControl?.Text == "Edited resource");

            var reset = Assert.Single(FindRow(grid, "Text").ContextMenu!.Items.OfType<MenuItem>());
            Assert.True(reset.IsEnabled);
            reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => surface.SelectedControl?.Text == "" && view.IsEnabled);
            Assert.Equal("", new WinFormsResourceDocument(resourceBuffer.Text).TryGet("Button1.Text")!.Value);
            Assert.Equal(Source, designer.Text);
            Assert.False(Assert.Single(grid.Properties, property => property.Name == "Text").IsSet);
            ClickHistory(view, StringKeys.MenuEditUndo);
            await WaitUntilAsync(() => resourceBuffer.Text == edited && surface.SelectedControl?.Text == "Edited resource");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ResizingWritesBothResourceEntriesInOneUndoStep()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(Source),
            new WinFormsResourceDocument(Resources));
        await session.SetBoundsAsync(session.FindControl("Button1")!, new System.Drawing.Rectangle(80, 90, 140, 45));
        Assert.Equal(Source, session.Document.Source);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal("80, 90", resources.TryGet("Button1.Location")!.Value);
        Assert.Equal("140, 45", resources.TryGet("Button1.Size")!.Value);
        await session.UndoAsync();
        Assert.Equal(Resources, session.ResourceSource);
        Assert.Equal(new System.Drawing.Point(40, 50), session.FindControl("Button1")!.Location);
        await session.RedoAsync();
        Assert.Equal(new System.Drawing.Size(140, 45), session.FindControl("Button1")!.Size);
    }

    [AvaloniaFact]
    public async Task LaterExplicitAssignmentKeepsItsVbStorageAndEarlierResourcesUnchanged()
    {
        var source = Source.Replace("resources.ApplyResources(Me.Button1, \"Button1\")",
            "resources.ApplyResources(Me.Button1, \"Button1\")\n        Me.Button1.Text = resources.GetString(\"Button1.Text\")\n        Me.Button1.Text = \"Explicit\"", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source),
            new WinFormsResourceDocument(Resources));
        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Updated VB");
        Assert.Equal(Resources, session.ResourceSource);
        Assert.Contains("Me.Button1.Text = \"Updated VB\"", session.Document.Source);
        Assert.Contains("Me.Button1.Text = resources.GetString(\"Button1.Text\")", session.Document.Source);
        Assert.Equal("Updated VB", session.FindControl("Button1")!.Text);
        await session.UndoAsync();
        Assert.Equal(source, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task ExternalResourceEditBlocksUndoWithoutOverwritingEitherDocument()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(Source),
            new WinFormsResourceDocument(Resources));
        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Designer edit");
        var edited = session.ResourceSource;
        var external = new WinFormsResourceDocument(Resources).SetString("Button1.Text", "External edit");
        session.UpdateResources(new WinFormsResourceDocument(external));
        await Assert.ThrowsAsync<InvalidOperationException>(session.UndoAsync);
        Assert.Equal(external, session.ResourceSource);
        Assert.Equal(Source, session.Document.Source);
        session.UpdateResources(new WinFormsResourceDocument(edited!));
        await session.UndoAsync();
        Assert.Equal(Resources, session.ResourceSource);
    }

    [AvaloniaFact]
    public async Task ClosedResourceEditorsAreLoadedAgainForEditsAndUndo()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", Source, true);
        var resources = new EditorDocumentViewModel("MainForm.resx", Resources);
        var originalBuffer = resources;
        var storedText = Resources;
        var closed = false;
        var opened = 0;
        using var view = new WinFormsDesignerView(designer,
            findCode: path => !closed && path.EndsWith(".resx", StringComparison.Ordinal) ? resources : null,
            getCode: path =>
            {
                Assert.EndsWith(".resx", path);
                ++opened;
                closed = false;
                resources = new EditorDocumentViewModel(path, storedText);
                return Task.FromResult(resources);
            });
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            closed = true;
            EditText(view, "Reopened edit");
            await WaitUntilAsync(() => surface.SelectedControl?.Text == "Reopened edit" && view.IsEnabled);
            Assert.Equal(1, opened);
            Assert.Equal(Resources, originalBuffer.Text);
            storedText = resources.Text;
            var editedBuffer = resources;
            closed = true;
            ClickHistory(view, StringKeys.MenuEditUndo);
            await WaitUntilAsync(() => resources.Text == Resources && surface.SelectedControl?.Text == "Resource text");
            Assert.Equal(2, opened);
            Assert.Equal(storedText, editedBuffer.Text);
            Assert.Equal(Source, designer.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ThrowingBufferListenerDoesNotSplitTheResourceAndDesignerTransaction()
    {
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", Source, true);
        var resources = new EditorDocumentViewModel("MainForm.resx", Resources);
        using var view = new WinFormsDesignerView(designer,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? resources : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var notified = false;
            resources.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == "Text" && resources.Text != Resources)
                {
                    notified = true;
                    throw new InvalidOperationException("Listener failure");
                }
            };
            EditText(view, "Rejected edit");
            await WaitUntilAsync(() => notified && view.IsEnabled);
            Assert.Equal(Resources, resources.Text);
            Assert.Equal("Resource text", surface.SelectedControl!.Text);
            Assert.Equal(Source, designer.Text);
            ClickHistory(view, StringKeys.MenuEditUndo);
            await WaitUntilAsync(() => view.IsEnabled);
            Assert.Equal(Resources, resources.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MixedVbAndResourceGroupChangesShareOneUndoTransaction()
    {
        var source = Source.Replace("Me.Panel1.Controls.Add(Me.Button1)",
                "Me.Button2 = New System.Windows.Forms.Button()\n        Me.Button2.Text = \"VB text\"\n"
                + "        Me.Panel1.Controls.Add(Me.Button2)\n        Me.Panel1.Controls.Add(Me.Button1)", StringComparison.Ordinal)
            .Replace("Friend WithEvents Button1 As", "Friend WithEvents Button2 As System.Windows.Forms.Button\n    Friend WithEvents Button1 As", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source),
            new WinFormsResourceDocument(Resources));
        await session.SetGroupPropertyAsync([session.FindControl("Button1")!, session.FindControl("Button2")!], "Text", "Group edit");
        Assert.Equal("Group edit", session.FindControl("Button1")!.Text);
        Assert.Equal("Group edit", session.FindControl("Button2")!.Text);
        Assert.Contains("Me.Button2.Text = \"Group edit\"", session.Document.Source);
        Assert.Equal("Group edit", new WinFormsResourceDocument(session.ResourceSource!).TryGet("Button1.Text")!.Value);
        await session.UndoAsync();
        Assert.Equal(source, session.Document.Source);
        Assert.Equal(Resources, session.ResourceSource);
        await session.RedoAsync();
        Assert.Equal("Group edit", session.FindControl("Button1")!.Text);
        Assert.Equal("Group edit", session.FindControl("Button2")!.Text);
    }

    private static void EditText(WinFormsDesignerView view, string text)
    {
        var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
        var editor = Assert.Single(FindRow(grid, "Text").GetVisualDescendants().OfType<TextBox>(),
            box => box.Name == "PropertyGridValue");
        editor.Text = text;
        editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    }

    private static Border FindRow(PropertyGrid grid, string name) => Assert.Single(grid.Rows.OfType<Border>(),
        row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static void ClickHistory(WinFormsDesignerView view, string key) =>
        Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, Localizer.Get(key)))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }
}
