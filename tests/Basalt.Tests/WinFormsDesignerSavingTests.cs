using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

public class WinFormsDesignerSavingTests
{
    [AvaloniaFact]
    public Task SaveFromDesignerPersistsBothPartsOfTheForm() => SaveBothPartsAsync(saveFromDesigner: true);

    [AvaloniaFact]
    public Task SaveFromCodePersistsBothPartsOfTheForm() => SaveBothPartsAsync(saveFromDesigner: false);

    [AvaloniaTheory]
    [InlineData("MainForm.vb")]
    [InlineData("MainForm.Designer.vb")]
    [InlineData("MainForm.resx")]
    public async Task SaveFromAnyCompanionPersistsAllThreeModifiedBuffers(string initiatingFile)
    {
        var folder = Directory.CreateTempSubdirectory("basalt-form-resource-save-").FullName;
        try
        {
            var sources = new Dictionary<string, string>
            {
                ["MainForm.vb"] = "Partial Class MainForm\nEnd Class",
                ["MainForm.Designer.vb"] = WinFormsDesignerDocumentTests.Source,
                ["MainForm.resx"] = "<root><data name='Button1.Text'><value>Before</value></data></root>",
            };
            foreach (var (name, source) in sources)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, name), source);
            }
            using var host = new TestWindow();
            var vm = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
            vm.FormatOnSave = false;
            foreach (var name in sources.Keys)
            {
                await vm.OpenFileAsync(Path.Combine(folder, name));
            }
            var documents = vm.OpenDocuments.Where(document => Path.GetDirectoryName(document.FilePath) == folder).ToArray();
            Assert.Equal(3, documents.Length);
            foreach (var document in documents)
            {
                document.Text = document.FilePath.EndsWith(".resx", StringComparison.Ordinal)
                    ? new WinFormsResourceDocument(document.Text).SetString("Button1.Text", "After")
                    : document.Text + "\n' Saved together";
                Assert.True(document.IsModified);
            }
            vm.ActivateFromTabSelection(documents.Single(document => Path.GetFileName(document.FilePath) == initiatingFile));
            await vm.SaveCommand.ExecuteAsync(null);
            foreach (var document in documents)
            {
                Assert.Equal(document.Text, await File.ReadAllTextAsync(document.FilePath));
                Assert.False(document.IsModified);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task StandaloneResourceSaveDoesNotSaveAnUnrelatedVisualBasicBuffer()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-standalone-resource-save-").FullName;
        try
        {
            var codePath = Path.Combine(folder, "Strings.vb");
            var resourcePath = Path.Combine(folder, "Strings.resx");
            const string codeSource = "Module Strings\nEnd Module";
            await File.WriteAllTextAsync(codePath, codeSource);
            await File.WriteAllTextAsync(resourcePath, "<root />");
            using var host = new TestWindow();
            var vm = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
            vm.FormatOnSave = false;
            await vm.OpenFileAsync(codePath);
            await vm.OpenFileAsync(resourcePath);
            var code = vm.OpenDocuments.Single(document => document.FilePath == codePath);
            var resource = vm.OpenDocuments.Single(document => document.FilePath == resourcePath);
            code.Text += "\n' Keep this unsaved";
            resource.Text = "<root><data name='Key'><value>Saved</value></data></root>";
            vm.ActivateFromTabSelection(resource);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.True(code.IsModified);
            Assert.Equal(codeSource, await File.ReadAllTextAsync(codePath));
            Assert.False(resource.IsModified);
            Assert.Equal(resource.Text, await File.ReadAllTextAsync(resourcePath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task SaveFromCleanDesignerPersistsOnlyDirtyResourceCompanion()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-clean-designer-resource-save-").FullName;
        try
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var codePath = Path.Combine(folder, "MainForm.vb");
            var resourcePath = Path.Combine(folder, "MainForm.resx");
            const string codeSource = "Partial Class MainForm\nEnd Class";
            const string resourceSource = "<root><data name='Button1.Text'><value>Before</value></data></root>";
            await File.WriteAllTextAsync(designerPath, WinFormsDesignerDocumentTests.Source);
            await File.WriteAllTextAsync(codePath, codeSource);
            await File.WriteAllTextAsync(resourcePath, resourceSource);
            using var host = new TestWindow();
            var vm = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
            vm.FormatOnSave = false;
            await vm.OpenFileAsync(codePath);
            await vm.OpenFileAsync(resourcePath);
            await vm.OpenFileAsync(designerPath, inDesigner: true);
            var designer = vm.OpenDocuments.Single(document => document.FilePath == designerPath);
            var code = vm.OpenDocuments.Single(document => document.FilePath == codePath);
            var resource = vm.OpenDocuments.Single(document => document.FilePath == resourcePath);
            resource.Text = new WinFormsResourceDocument(resource.Text).SetString("Button1.Text", "After");
            Assert.False(designer.IsModified);
            Assert.False(code.IsModified);
            Assert.True(resource.IsModified);
            vm.ActivateFromTabSelection(designer);
            Assert.Same(designer, vm.ActiveDocument);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal(resource.Text, await File.ReadAllTextAsync(resourcePath));
            Assert.False(resource.IsModified);
            Assert.False(designer.IsModified);
            Assert.False(code.IsModified);
            Assert.Equal(WinFormsDesignerDocumentTests.Source, designer.Text);
            Assert.Equal(WinFormsDesignerDocumentTests.Source, await File.ReadAllTextAsync(designerPath));
            Assert.Equal(codeSource, await File.ReadAllTextAsync(codePath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompanionSaveResolvesBuffersReplacedOrClosedDuringEarlierSave(bool reopenResource)
    {
        var folder = Directory.CreateTempSubdirectory("basalt-replaced-resource-save-").FullName;
        try
        {
            var codePath = Path.Combine(folder, "MainForm.vb");
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var resourcePath = Path.Combine(folder, "MainForm.resx");
            const string originalResource = "<root><data name='Text'><value>Original</value></data></root>";
            await File.WriteAllTextAsync(codePath, "Partial Class MainForm\nEnd Class");
            await File.WriteAllTextAsync(designerPath, WinFormsDesignerDocumentTests.Source);
            await File.WriteAllTextAsync(resourcePath, originalResource);
            using var host = new TestWindow();
            var vm = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
            vm.FormatOnSave = false;
            await vm.OpenFileAsync(codePath);
            await vm.OpenFileAsync(resourcePath);
            await vm.OpenFileAsync(designerPath, inDesigner: true);
            var code = vm.OpenDocuments.Single(document => document.FilePath == codePath);
            var oldResource = vm.OpenDocuments.Single(document => document.FilePath == resourcePath);
            var designer = vm.OpenDocuments.Single(document => document.FilePath == designerPath);
            code.Text += "\n' Trigger companion save";
            oldResource.Text = new WinFormsResourceDocument(originalResource).SetString("Text", "Obsolete buffer");
            EditorDocumentViewModel? replacement = null;
            var bufferChanged = false;
            code.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(EditorDocumentViewModel.IsModified) || code.IsModified)
                {
                    return;
                }
                // Replace the tab at the real save completion boundary, before the
                // command continues to the next companion file.
                void ReplaceBuffer()
                {
                    vm.OpenDocuments.Remove(oldResource);
                    if (reopenResource)
                    {
                        replacement = new EditorDocumentViewModel(resourcePath, originalResource);
                        replacement.Text = new WinFormsResourceDocument(originalResource).SetString("Text", "Current buffer");
                        vm.OpenDocuments.Add(replacement);
                    }
                    bufferChanged = true;
                }
                if (Dispatcher.UIThread.CheckAccess())
                {
                    ReplaceBuffer();
                }
                else
                {
                    Dispatcher.UIThread.InvokeAsync(ReplaceBuffer).GetAwaiter().GetResult();
                }
            };
            vm.ActivateFromTabSelection(designer);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.True(bufferChanged);
            Assert.True(oldResource.IsModified);
            Assert.Equal(reopenResource ? replacement!.Text : originalResource,
                await File.ReadAllTextAsync(resourcePath));
            if (replacement is not null)
            {
                Assert.False(replacement.IsModified);
            }
            Assert.False(code.IsModified);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task SaveBothPartsAsync(bool saveFromDesigner)
    {
        var folder = Path.Combine(Path.GetTempPath(), "basalt-forms-save", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var designerPath = Path.Combine(folder, "MainForm.Designer.vb");
            var codePath = Path.Combine(folder, "MainForm.vb");
            const string codeSource = """
                Partial Class MainForm
                    Private Sub Clicked(sender As Object, e As System.EventArgs) Handles Button1.Click
                        System.Console.WriteLine("Preserve user code")
                    End Sub
                End Class
                """;
            await File.WriteAllTextAsync(designerPath, WinFormsDesignerDocumentTests.Source);
            await File.WriteAllTextAsync(codePath, codeSource);
            using var host = new TestWindow();
            var vm = Assert.IsType<MainWindowViewModel>(host.Window.DataContext);
            // This test verifies persistence; formatting has its own command tests.
            vm.FormatOnSave = false;
            await vm.OpenFileAsync(codePath);
            await vm.OpenFileAsync(designerPath, inDesigner: true);
            var code = Assert.Single(vm.OpenDocuments, document => document.FilePath == codePath);
            var designer = Assert.Single(vm.OpenDocuments, document => document.FilePath == designerPath);

            // Deleting a WithEvents control changes declarations and its companion Handles clause.
            designer.Text = new WinFormsDesignerDocument(designer.Text).RemoveControl("Button1");
            code.Text = WinFormsEventHandlers.RemoveControls(code.Text, "MainForm",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Button1" });
            Assert.True(designer.IsModified);
            Assert.True(code.IsModified);
            Assert.Contains("Friend WithEvents Button1", await File.ReadAllTextAsync(designerPath));
            Assert.Contains("Handles Button1.Click", await File.ReadAllTextAsync(codePath));

            vm.ActivateFromTabSelection(saveFromDesigner ? designer : code);
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.Equal(designer.Text, await File.ReadAllTextAsync(designerPath));
            Assert.Equal(code.Text, await File.ReadAllTextAsync(codePath));
            Assert.False(designer.IsModified);
            Assert.False(code.IsModified);
            Assert.DoesNotContain("Friend WithEvents Button1", await File.ReadAllTextAsync(designerPath));
            Assert.DoesNotContain("Handles Button1.Click", await File.ReadAllTextAsync(codePath));
            Assert.Contains("Preserve user code", await File.ReadAllTextAsync(codePath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
