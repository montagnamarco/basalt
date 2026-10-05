using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class VisualBasicEditorNavigationTests
{
    private const string Source = """
        Partial Class MainForm
            Private Sub Existing(sender As Object, e As System.EventArgs) Handles Button1.Click
                System.Console.WriteLine("Keep body")
            End Sub
        End Class
        """;
    private const string Companion = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Friend WithEvents Button1 As System.Windows.Forms.Button
        End Class
        """;

    [Fact]
    public void LocalShadowedFrameworkNameUsesOnlyItsParsedEventSignature()
    {
        const string source = """
            Class SerialPort
                Public Event DataReceived(value As Integer)
            End Class
            Class Consumer
                Private WithEvents Port As SerialPort
            End Class
            """;
        var target = VisualBasicEditorNavigation.Read(source, null).Single(item => item.Name == "Port");
        var member = Assert.Single(target.Members);
        Assert.Null(member.DelegateType);
        var edited = VisualBasicEditorNavigation.SelectEvent(source, target, member);
        Assert.Contains("Private Sub Port_DataReceived(value As Integer) Handles Port.DataReceived", edited.Source);
        Assert.DoesNotContain("SerialDataReceivedEventArgs", edited.Source);
    }

    [Fact]
    public void ImportedAliasDoesNotGuessFrameworkEvents()
    {
        const string source = """
            Imports SerialPort = CustomLibrary.Publisher
            Class Consumer
                Private WithEvents Port As SerialPort
            End Class
            """;
        var target = VisualBasicEditorNavigation.Read(source, null).Single(item => item.Name == "Port");
        Assert.Empty(target.Members);
    }

    [Theory]
    [InlineData("System.Timers")]
    [InlineData("System.Threading")]
    public void ImportedFrameworkNamespaceDoesNotTurnTimerIntoTheFormsTimer(string importedNamespace)
    {
        var source = $"""
            Imports {importedNamespace}
            Class Consumer
                Private WithEvents Clock As Timer
            End Class
            """;
        var target = VisualBasicEditorNavigation.Read(source, null).Single(item => item.Name == "Clock");
        Assert.DoesNotContain(target.Members, member => member.EventName == "Tick");
    }

    [Fact]
    public void UnqualifiedTimerWithoutImportsStillOffersTheFormsTick()
    {
        const string source = """
            Class Consumer
                Private WithEvents Clock As Timer
            End Class
            """;
        var target = VisualBasicEditorNavigation.Read(source, null).Single(item => item.Name == "Clock");
        Assert.Contains(target.Members, member => member.EventName == "Tick");
    }

    [Fact]
    public void SerialPortEventsCreateTypedHandlersFromTrustedMetadata()
    {
        const string source = """
            Partial Class MainForm
                Private Sub Keep()
                    System.Console.WriteLine("Untouched")
                End Sub
            End Class
            """;
        foreach (var typeName in new[] { "SerialPort", "Global.System.IO.Ports.SerialPort" })
        {
            var companion = $"Partial Class MainForm\nFriend WithEvents SerialPort1 As {typeName}\nEnd Class";
            var target = VisualBasicEditorNavigation.Read(source, companion).Single(item => item.Name == "SerialPort1");
            var dataReceived = target.Members.Single(member => member.Name == "DataReceived");
            Assert.Equal(typeof(System.IO.Ports.SerialDataReceivedEventHandler), dataReceived.DelegateType);
            Assert.Contains(target.Members, member => member.Name == "ErrorReceived");
            Assert.Contains(target.Members, member => member.Name == "PinChanged");
            var edited = VisualBasicEditorNavigation.SelectEvent(source, target, dataReceived);
            Assert.Contains("Handles SerialPort1.DataReceived", edited.Source);
            Assert.Contains("Global.System.IO.Ports.SerialDataReceivedEventArgs", edited.Source);
            Assert.Contains("System.Console.WriteLine(\"Untouched\")", edited.Source);
            Assert.DoesNotContain(Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(edited.Source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            Assert.Equal(edited.Source, VisualBasicEditorNavigation.SelectEvent(edited.Source, target, dataReceived).Source);
        }
    }

    [Fact]
    public void ReadsPartialFieldsFrameworkEventsAndGeneralMembers()
    {
        var objects = VisualBasicEditorNavigation.Read(Source, Companion);
        Assert.Contains(objects.Single(item => item.Name == "MainForm").Members, member => member.Name == "Existing");
        Assert.Contains(objects.Single(item => item.Name == "Button1").Members, member => member.EventName == "Click");
        Assert.Contains(objects.Single(item => item.Name == "(MainForm)").Members, member => member.EventName == "Load");
        var selected = VisualBasicEditorNavigation.SelectEvent(Source, objects.Single(item => item.Name == "Button1"),
            objects.Single(item => item.Name == "Button1").Members.Single(member => member.Name == "Click"));
        Assert.Equal(Source, selected.Source);
        Assert.True(selected.Offset > Source.IndexOf("Handles Button1.Click", StringComparison.Ordinal));
        Assert.True(selected.Offset < Source.IndexOf("End Sub", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task SerialPortDropdownCreatesHandlerWithoutOpeningAPort()
    {
        var vm = new MainWindowViewModel();
        vm.OpenDocuments.Add(new EditorDocumentViewModel("MainForm.Designer.vb", """
            Partial Class MainForm
                Friend WithEvents SerialPort1 As Global.System.IO.Ports.SerialPort
            End Class
            """));
        var document = new EditorDocumentViewModel("MainForm.vb", "Partial Class MainForm\nEnd Class");
        var original = document.Text;
        var code = new CodeEditor(document, vm) { AutoFormatWhileTyping = false };
        var window = new Window { Content = code, Width = 700, Height = 400 };
        window.Show();
        try
        {
            var selectors = code.GetVisualDescendants().OfType<ComboBox>().ToArray();
            var objects = selectors.Single(control => control.Name == "VisualBasicObjects");
            var members = selectors.Single(control => control.Name == "VisualBasicMembers");
            await WaitUntilAsync(() => objects.Items.OfType<VisualBasicNavigationObject>()
                .Any(item => item.Name == "SerialPort1"));
            objects.SelectedItem = objects.Items.OfType<VisualBasicNavigationObject>()
                .Single(item => item.Name == "SerialPort1");
            members.SelectedItem = members.Items.OfType<VisualBasicNavigationMember>()
                .Single(item => item.Name == "DataReceived");
            await WaitUntilAsync(() => document.Text.Contains("Handles SerialPort1.DataReceived", StringComparison.Ordinal));
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            Assert.Contains("Global.System.IO.Ports.SerialDataReceivedEventArgs", document.Text);
            Assert.True(editor.TextArea.IsFocused);
            Assert.Equal(document.CaretOffset, editor.CaretOffset);
            editor.Document.UndoStack.Undo();
            Assert.Equal(original, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RepeatedCaretRequestRegainsEditorFocusAtTheSamePosition()
    {
        var vm = new MainWindowViewModel();
        var document = new EditorDocumentViewModel("MainForm.vb", Source);
        var code = new CodeEditor(document, vm) { AutoFormatWhileTyping = false, Height = 300 };
        var other = new TextBox();
        var window = new Window
        {
            Content = new StackPanel { Children = { code, other } },
            Width = 700,
            Height = 400
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            var position = Source.IndexOf("System.Console", StringComparison.Ordinal);
            document.RequestCaret(position);
            Assert.Equal(position, editor.CaretOffset);
            Assert.True(editor.TextArea.IsFocused);
            Assert.True(other.Focus());
            Assert.True(other.IsFocused);

            document.CaretOffset = position;
            Assert.True(other.IsFocused);
            document.RequestCaret(position);

            Assert.True(editor.TextArea.IsFocused);
            Assert.Equal(position, editor.CaretOffset);
            Assert.Equal(Source, document.Text);
            Assert.False(document.IsModified);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RequestedModelCaretMovesActualEditorAndFocusesTextArea()
    {
        var vm = new MainWindowViewModel();
        var document = new EditorDocumentViewModel("MainForm.vb", Source);
        var code = new CodeEditor(document, vm) { AutoFormatWhileTyping = false };
        var window = new Window { Content = code, Width = 700, Height = 400 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            document.CaretOffset = Source.IndexOf("System.Console", StringComparison.Ordinal);
            Assert.Equal(document.CaretOffset, editor.CaretOffset);
            Assert.True(editor.TextArea.IsFocused);
            Assert.Equal(Source, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ObjectEventSelectorCreatesHandlerAndNavigatesExistingWithoutDuplicating()
    {
        var vm = new MainWindowViewModel();
        vm.OpenDocuments.Add(new EditorDocumentViewModel("MainForm.Designer.vb", Companion));
        var document = new EditorDocumentViewModel("MainForm.vb", Source);
        var code = new CodeEditor(document, vm) { AutoFormatWhileTyping = false };
        var window = new Window { Content = code, Width = 700, Height = 400 };
        window.Show();
        try
        {
            var left = code.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "VisualBasicObjects");
            var right = code.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "VisualBasicMembers");
            await WaitUntilAsync(() => left.Items.OfType<VisualBasicNavigationObject>().Any(item => item.Name == "Button1"));
            left.SelectedItem = left.Items.OfType<VisualBasicNavigationObject>().Single(item => item.Name == "Button1");
            right.SelectedItem = right.Items.OfType<VisualBasicNavigationMember>().Single(item => item.Name == "MouseEnter");
            await WaitUntilAsync(() => document.Text.Contains("Handles Button1.MouseEnter", StringComparison.Ordinal));
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            Assert.Equal(document.CaretOffset, editor.CaretOffset);
            Assert.True(editor.TextArea.IsFocused);
            Assert.Contains("Keep body", document.Text);
            var created = document.Text;
            await WaitUntilAsync(() => right.SelectedItem is null);
            right.SelectedItem = right.Items.OfType<VisualBasicNavigationMember>().Single(item => item.Name == "MouseEnter");
            await WaitUntilAsync(() => editor.TextArea.IsFocused);
            Assert.Equal(created, document.Text);
            Assert.True(editor.CaretOffset > created.IndexOf("Handles Button1.MouseEnter", StringComparison.Ordinal));
            Assert.True(editor.CaretOffset < created.LastIndexOf("End Sub", StringComparison.Ordinal));
            editor.Document.UndoStack.Undo();
            Assert.Equal(Source, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void LocalCustomEventUsesParsedSignatureWithoutExecutingApplicationCode()
    {
        const string source = """
            Class Publisher
                Public Event Changed(sender As Object, value As Integer)
            End Class
            Class Consumer
                Private WithEvents Publisher1 As Publisher
            End Class
            """;
        var target = VisualBasicEditorNavigation.Read(source, null).Single(item => item.Name == "Publisher1");
        var member = Assert.Single(target.Members);
        var selected = VisualBasicEditorNavigation.SelectEvent(source, target, member);
        Assert.Contains("Private Sub Publisher1_Changed(sender As Object, value As Integer) Handles Publisher1.Changed", selected.Source);
        Assert.True(selected.Offset > selected.Source.IndexOf("Handles Publisher1.Changed", StringComparison.Ordinal));
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
}
