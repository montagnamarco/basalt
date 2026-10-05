using System.Drawing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

public sealed class ResourceEditorTests
{
    [AvaloniaFact]
    public void StringEditsPreserveOpaqueResourcesAndComments()
    {
        const string opaque = "<data name='Object' type='Custom.Type, User'><value>opaque</value></data>";
        var source = "<root><!--keep--><data name='Text'><value>Before</value><comment>Note</comment></data>" + opaque + "</root>";
        var document = new EditorDocumentViewModel("Form.resx", source);
        var editor = new ResourceEditor(document, () => new TextBlock());
        var window = new Window { Content = editor, Width = 880, Height = 420 };
        window.Show();
        try
        {
            var list = Find<ListBox>(editor, "ResourceEntries");
            list.SelectedItem = list.ItemsSource!.Cast<WinFormsResourceEntry>()
                .Single(entry => entry.Name == "Text");
            Find<TextBox>(editor, "ResourceValue").Text = "After & <markup>";
            Click(editor, "ResourceApply");

            Assert.Equal("After & <markup>", new WinFormsResourceDocument(document.Text).TryGet("Text")!.Value);
            Assert.Contains(opaque, document.Text);
            Assert.Contains("<!--keep-->", document.Text);
            Assert.Contains("<comment>Note</comment>", document.Text);
            Assert.True(document.IsModified);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CtrlZWithAnUncommittedDraftUndoesTheFieldNotTheLastAppliedChange()
    {
        var source = "<root><data name='Existing'><value>Old</value></data></root>";
        var document = new EditorDocumentViewModel("Form.resx", source);
        var editor = new ResourceEditor(document, () => new TextBlock());
        var window = new Window { Content = editor, Width = 880, Height = 420 };
        window.Show();
        try
        {
            // One real, applied change: this is what a buggy Ctrl+Z would
            // revert instead of the field's own draft.
            Click(editor, "ResourceAdd");
            Find<TextBox>(editor, "ResourceName").Text = "Added";
            Find<TextBox>(editor, "ResourceValue").Text = "New";
            Click(editor, "ResourceApply");
            var applied = document.Text;
            Assert.Equal("New", new WinFormsResourceDocument(applied).TryGet("Added")!.Value);

            // Select the entry and start editing its value without applying:
            // an uncommitted draft sitting in the focused text box.
            var list = Find<ListBox>(editor, "ResourceEntries");
            list.SelectedItem = list.ItemsSource!.Cast<WinFormsResourceEntry>()
                .Single(entry => entry.Name == "Added");
            var value = Find<TextBox>(editor, "ResourceValue");
            value.Focus();
            value.Text = "Draft";

            // Ctrl+Z raised on the focused field must tunnel through the
            // editor without being intercepted: the last applied resource
            // change must still be there.
            PressKey(value, Key.Z, KeyModifiers.Control);
            Assert.Equal(applied, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AddTypedScalarsThroughTheVisibleEditor()
    {
        var samples = new (Type Type, string Text, object Expected)[]
        {
            (typeof(string), "Hello", "Hello"),
            (typeof(int), "42", 42),
            (typeof(bool), "true", true),
            (typeof(Color), "#7F123456", Color.FromArgb(127, 18, 52, 86)),
            (typeof(Point), "12, 34", new Point(12, 34)),
            (typeof(Size), "80, 24", new Size(80, 24)),
        };

        foreach (var (type, text, expected) in samples)
        {
            var document = new EditorDocumentViewModel("Form.resx", "<root />");
            var editor = new ResourceEditor(document, () => new TextBlock());
            var window = new Window { Content = editor, Width = 880, Height = 420 };
            window.Show();
            try
            {
                Click(editor, "ResourceAdd");
                Find<TextBox>(editor, "ResourceName").Text = "Typed";
                var choices = Find<ComboBox>(editor, "ResourceType");
                choices.SelectedItem = choices.ItemsSource!.Cast<object>()
                    .Single(choice => choice.ToString() == type.FullName);
                Find<TextBox>(editor, "ResourceValue").Text = text;
                Find<TextBox>(editor, "ResourceComment").Text = "Translator note";
                Click(editor, "ResourceApply");

                var entry = new WinFormsResourceDocument(document.Text).TryGet("Typed")!;
                Assert.NotNull(entry);
                Assert.Equal("Translator note", entry.Comment);
                Assert.True(WinFormsDesignerResources.TryRead(entry, type, out var value));
                Assert.Equal(expected, value);
                var list = Find<ListBox>(editor, "ResourceEntries");
                Assert.Equal("Typed", Assert.IsType<WinFormsResourceEntry>(list.SelectedItem).Name);
                Assert.True(Find<TextBox>(editor, "ResourceName").IsReadOnly);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), cell =>
                    cell.Text == "Translator note");
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void AddDeleteUndoRedoAndXmlReturnWorkThroughRealButtonsAndKeys()
    {
        var source = "<root><data name='Existing'><value>Old</value></data></root>";
        var document = new EditorDocumentViewModel("Form.resx", source);
        var editor = new ResourceEditor(document, () => new TextBlock { Name = "RawXmlPreview" });
        var window = new Window { Content = editor, Width = 880, Height = 420 };
        window.Show();
        try
        {
            Click(editor, "ResourceAdd");
            Find<TextBox>(editor, "ResourceName").Text = "Added";
            Find<TextBox>(editor, "ResourceValue").Text = "New";
            Click(editor, "ResourceApply");
            var added = document.Text;
            Assert.Equal("New", new WinFormsResourceDocument(added).TryGet("Added")!.Value);

            PressKey(editor, Key.Z, KeyModifiers.Control);
            Assert.Equal(source, document.Text);
            PressKey(editor, Key.Y, KeyModifiers.Control);
            Assert.Equal(added, document.Text);

            var list = Find<ListBox>(editor, "ResourceEntries");
            list.SelectedItem = list.ItemsSource!.Cast<WinFormsResourceEntry>()
                .Single(entry => entry.Name == "Added");
            Click(editor, "ResourceDelete");
            Assert.Null(new WinFormsResourceDocument(document.Text).TryGet("Added"));
            Click(editor, "ResourceUndo");
            Assert.Equal(added, document.Text);
            Click(editor, "ResourceRedo");
            Assert.Null(new WinFormsResourceDocument(document.Text).TryGet("Added"));

            Click(editor, "ResourceXml");
            Assert.NotNull(Find<TextBlock>(editor, "RawXmlPreview"));
            Click(editor, "ResourceBack");
            Assert.NotNull(Find<ListBox>(editor, "ResourceEntries"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OpaqueValuesRemainUnreadWhileTheirCommentsCanBeEdited()
    {
        const string source = "<root><data name='Object' type='Custom.Type, User' mimetype='application/x-custom'>"
            + "<value>opaque bytes</value></data></root>";
        var document = new EditorDocumentViewModel("Form.resx", source);
        var editor = new ResourceEditor(document, () => new TextBlock());
        var window = new Window { Content = editor, Width = 880, Height = 420 };
        window.Show();
        try
        {
            var list = Find<ListBox>(editor, "ResourceEntries");
            list.SelectedItem = Assert.Single(list.ItemsSource!.Cast<WinFormsResourceEntry>());
            Assert.True(Find<TextBox>(editor, "ResourceValue").IsReadOnly);
            Assert.Equal("Custom.Type, User", Find<TextBlock>(editor, "ResourceRawType").Text);
            Find<TextBox>(editor, "ResourceComment").Text = "Safe note";
            Click(editor, "ResourceApply");

            var entry = new WinFormsResourceDocument(document.Text).TryGet("Object")!;
            Assert.Equal("Safe note", entry.Comment);
            Assert.Equal("opaque bytes", entry.Value);
            Assert.Equal("Custom.Type, User", entry.TypeName);
            Assert.Equal("application/x-custom", entry.MimeType);
            Click(editor, "ResourceUndo");
            Assert.Equal(source, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InvalidXmlClearsOldSelectionAndCannotRecreateAnEntry()
    {
        var source = "<root><data name='Old'><value>Before</value></data></root>";
        var document = new EditorDocumentViewModel("Form.resx", source);
        var editor = new ResourceEditor(document, () => new TextBlock());
        var window = new Window { Content = editor, Width = 880, Height = 420 };
        window.Show();
        try
        {
            var list = Find<ListBox>(editor, "ResourceEntries");
            list.SelectedItem = Assert.Single(list.ItemsSource!.Cast<WinFormsResourceEntry>());
            Click(editor, "ResourceAdd");
            Find<TextBox>(editor, "ResourceName").Text = "Old";
            Find<TextBox>(editor, "ResourceValue").Text = "Overwrite";
            Click(editor, "ResourceApply");
            Assert.Equal(source, document.Text);
            Assert.Contains("already exists", Find<TextBlock>(editor, "ResourceStatus").Text);

            document.Text = "<root><data";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(list.ItemsSource!.Cast<WinFormsResourceEntry>());
            Assert.Null(list.SelectedItem);
            Assert.Equal("", Find<TextBox>(editor, "ResourceName").Text);
            Assert.True(Find<TextBox>(editor, "ResourceName").IsReadOnly);
            Click(editor, "ResourceAdd");
            Click(editor, "ResourceApply");
            Assert.Equal("<root><data", document.Text);

            document.Text = "<root />";
            Click(editor, "ResourceAdd");
            Find<TextBox>(editor, "ResourceName").Text = "Fresh";
            var choices = Find<ComboBox>(editor, "ResourceType");
            Assert.Equal(6, choices.ItemsSource!.Cast<object>().Count());
            choices.SelectedItem = choices.ItemsSource!.Cast<object>()
                .Single(choice => choice.ToString() == typeof(int).FullName);
            Find<TextBox>(editor, "ResourceValue").Text = "not an integer";
            Click(editor, "ResourceApply");
            Assert.Equal("<root />", document.Text);

            Find<TextBox>(editor, "ResourceValue").Text = "3";
            Click(editor, "ResourceApply");
            Assert.Equal("3", new WinFormsResourceDocument(document.Text).TryGet("Fresh")!.Value);
            Assert.Null(new WinFormsResourceDocument(document.Text).TryGet("Old"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UndoAfterDetachedExternalEditCannotReplaceTheNewDocument()
    {
        var document = new EditorDocumentViewModel("Form.resx", "<root />");
        var editor = new ResourceEditor(document, () => new TextBlock());
        var window = new Window { Content = editor, Width = 880, Height = 420 };
        window.Show();
        try
        {
            Click(editor, "ResourceAdd");
            Find<TextBox>(editor, "ResourceName").Text = "First";
            Find<TextBox>(editor, "ResourceValue").Text = "One";
            Click(editor, "ResourceApply");
            Assert.NotNull(new WinFormsResourceDocument(document.Text).TryGet("First"));

            window.Content = new TextBlock();
            window.UpdateLayout();
            document.Text = "<root><data name='External'><value>Two</value></data></root>";
            var external = document.Text;
            window.Content = editor;
            window.UpdateLayout();
            Click(editor, "ResourceUndo");

            Assert.Equal(external, document.Text);
            Assert.NotNull(new WinFormsResourceDocument(document.Text).TryGet("External"));
        }
        finally
        {
            window.Close();
        }
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Control root, string name) =>
        Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void PressKey(Control root, Key key, KeyModifiers modifiers) =>
        root.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
}
