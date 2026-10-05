using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class WinFormsDesignerResourcePreviewTests
{
    private const string Resources = """
        <root>
          <data name="Button1.Text"><value>Resource text</value></data>
          <data name="Button1.Location" type="System.Drawing.Point, System.Drawing"><value>40, 50</value></data>
          <data name="Button1.Size" type="System.Drawing.Size, System.Drawing"><value>120, 35</value></data>
        </root>
        """;

    [AvaloniaFact]
    public void ResourceAssignmentsRespectSourceOrderAndNeverRewriteInitialization()
    {
        var source = Source(explicitTextAfterResources: true);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source),
            new WinFormsResourceDocument(Resources));
        var button = session.FindControl("Button1")!;
        Assert.Equal("Explicit", button.Text);
        Assert.Equal(new System.Drawing.Point(40, 50), button.Location);
        Assert.Equal(new System.Drawing.Size(120, 35), button.Size);
        Assert.Equal(source, session.Document.Source);
    }

    [AvaloniaFact]
    public void TypedLookupReadsOnlyItsDeclaredResourceWithoutExecutingTheCast()
    {
        var source = Source(explicitTextAfterResources: false).Replace(
            "resources.ApplyResources(Me.Button1, \"Button1\")",
            "Me.Button1.Text = resources.GetString(\"Button1.Text\")\n"
            + "        Me.Button1.Location = CType(resources.GetObject(\"Button1.Location\"), System.Drawing.Point)",
            StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source),
            new WinFormsResourceDocument(Resources));
        Assert.Equal("Resource text", session.FindControl("Button1")!.Text);
        Assert.Equal(new System.Drawing.Point(40, 50), session.FindControl("Button1")!.Location);
        Assert.Equal(source, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task DesignerLoadsResourcesFromAnUnsavedEditorBuffer()
    {
        var source = Source(explicitTextAfterResources: false);
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        var resources = new EditorDocumentViewModel("MainForm.resx", Resources);
        using var view = new WinFormsDesignerView(document,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? resources : null);
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
            var button = surface.RootForm!.Controls[0].Controls[0];
            Assert.Equal("Resource text", button.Text);
            Assert.Equal(new System.Drawing.Point(40, 50), button.Location);
            Assert.Equal(source, document.Text);
            Assert.Equal(Resources, resources.Text);
            surface.Select(button);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var textProperty = Assert.Single(grid.Properties, property => property.Name == "Text");
            Assert.True(textProperty.IsSet);
            Assert.True(textProperty.CanReset);

            await Task.Run(() => resources.Text = Resources.Replace("Resource text", "Live text", StringComparison.Ordinal));
            await WaitUntilAsync(() => surface.SelectedControl?.Text == "Live text");
            resources.Text = "<root>";
            await WaitUntilAsync(() => surface.SelectedControl?.Text == "");
            Assert.NotNull(surface.RootForm);
            Assert.Equal("<root>", resources.Text);
            resources.Text = Resources;
            await WaitUntilAsync(() => surface.SelectedControl?.Text == "Resource text");
            Assert.Equal(source, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MalformedResourceBufferDoesNotPreventOpeningAValidForm()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb",
            Source(explicitTextAfterResources: false), true);
        var resources = new EditorDocumentViewModel("MainForm.resx", "<root>");
        using var view = new WinFormsDesignerView(document,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? resources : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            Assert.Equal("Button1", surface.RootForm!.Controls[0].Controls[0].Name);
            Assert.Equal("<root>", resources.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UnsupportedResourceValueIsModifiedAndCannotResetTheWrongFile()
    {
        var source = Source(explicitTextAfterResources: false);
        var resources = Resources.Replace("name=\"Button1.Text\"",
            "name=\"Button1.Text\" mimetype=\"application/x-custom\"", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source),
            new WinFormsResourceDocument(resources));
        var button = session.FindControl("Button1")!;
        var state = session.SourceValueState(button, "Text");
        Assert.Equal(Basalt.Designer.WinFormsSourceValueState.ResourceBackedUnresolved, state);
        var descriptor = System.ComponentModel.TypeDescriptor.GetProperties(button)["Text"]!;
        var metadata = Basalt.Designer.WinFormsPropertyResetMetadata.Describe(button, descriptor, state);
        Assert.True(metadata.IsModified);
        Assert.False(metadata.CanReset);
        Assert.Contains(session.PreviewProblems, problem => problem.StartsWith("Button1.Text:", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task QueuedEventFromAReplacedBufferDoesNotCancelTheCurrentResourceEdit()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb",
            Source(explicitTextAfterResources: false), true);
        var current = new EditorDocumentViewModel("MainForm.resx", Resources);
        var previous = new EditorDocumentViewModel("MainForm.resx",
            Resources.Replace("Resource text", "Stale text", StringComparison.Ordinal));
        using var view = new WinFormsDesignerView(document,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? current : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            current.Text = Resources.Replace("Resource text", "Current text", StringComparison.Ordinal);
            // A callback already queued before unsubscription can still arrive
            // after a new buffer's edit. Deliver that callback before dispatching
            // either parse completion, then observe the actual preview result.
            typeof(WinFormsDesignerView).GetMethod("ResourceBufferChanged",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(view, [previous, new System.ComponentModel.PropertyChangedEventArgs("Text")]);
            await WaitUntilAsync(() => surface.SelectedControl?.Text == "Current text");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    private static string Source(bool explicitTextAfterResources)
    {
        return WinFormsDesignerDocumentTests.Source.Replace("Private Sub InitializeComponent()",
                "Private Sub InitializeComponent()\n        Dim resources As New System.ComponentModel.ComponentResourceManager(GetType(MainForm))",
                StringComparison.Ordinal)
            .Replace("Me.Button1.Text = \"Before\"",
                "resources.ApplyResources(Me.Button1, \"Button1\")"
                + (explicitTextAfterResources ? "\n        Me.Button1.Text = \"Explicit\"" : ""),
                StringComparison.Ordinal);
    }
}
