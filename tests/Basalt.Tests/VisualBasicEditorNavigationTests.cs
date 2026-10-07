using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Localization;
using Basalt.Core.Model;
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
        var general = Localizer.Get(StringKeys.EditorVbGeneral);
        var formEvents = Localizer.Get(StringKeys.EditorVbFormEvents, "MainForm");
        Assert.Contains(objects.Single(item => item.Name == general).Members, member => member.Name == "Existing");
        Assert.Contains(objects.Single(item => item.Name == "Button1").Members, member => member.EventName == "Click");
        Assert.Contains(objects.Single(item => item.Name == formEvents).Members, member => member.EventName == "Load");
        var selected = VisualBasicEditorNavigation.SelectEvent(Source, objects.Single(item => item.Name == "Button1"),
            objects.Single(item => item.Name == "Button1").Members.Single(member => member.Name == "Click"));
        Assert.Equal(Source, selected.Source);
        Assert.True(selected.Offset > Source.IndexOf("Handles Button1.Click", StringComparison.Ordinal));
        Assert.True(selected.Offset < Source.IndexOf("End Sub", StringComparison.Ordinal));
    }

    /// <summary>
    /// The entry the left combo marks "general" is the one the caret-tracking
    /// logic falls back to; without <see cref="VisualBasicNavigationObject.IsGeneral"/>
    /// correctly set, following the caret into a type with no base-class
    /// events and no fields would have nothing to fall back to.
    /// </summary>
    [Fact]
    public void GeneralEntryIsMarkedGeneralAndLeadsWithDeclarations()
    {
        var target = VisualBasicEditorNavigation.Read(Source, Companion)
            .Single(item => item.Name == Localizer.Get(StringKeys.EditorVbGeneral));
        Assert.True(target.IsGeneral);
        Assert.Equal(Localizer.Get(StringKeys.EditorVbDeclarations), target.Members[0].Name);
        Assert.Null(target.Members[0].EventName);
    }

    /// <summary>
    /// VB6 and Visual Studio's own Procedure list show a type's members in
    /// the order they were written, not alphabetically — the author's
    /// ordering is information, and scrambling it was a regression a plain
    /// alphabetical sort would not have caught.
    /// </summary>
    [Fact]
    public void GeneralMembersKeepDeclarationOrderRatherThanAlphabetical()
    {
        const string source = """
            Partial Class MainForm
                Private Sub Zebra()
                End Sub
                Private Sub Apple()
                End Sub
            End Class
            """;
        var target = VisualBasicEditorNavigation.Read(source, null)
            .Single(item => item.Name == Localizer.Get(StringKeys.EditorVbGeneral));

        // Skip the "(Declarations)" entry that always leads the list.
        var names = target.Members.Skip(1).Select(member => member.Name).ToArray();
        Assert.Equal(["Zebra", "Apple"], names);
    }

    /// <summary>
    /// A method, property and event each carry the icon kind matching Visual
    /// Studio's symbol icons, so the Procedure list is not just text.
    /// </summary>
    [Fact]
    public void GeneralMembersCarryIconKindsForMethodsPropertiesAndEvents()
    {
        const string source = """
            Partial Class MainForm
                Public Sub DoWork()
                End Sub
                Public Property Count As Integer
                Public Event Changed()
            End Class
            """;
        var target = VisualBasicEditorNavigation.Read(source, null)
            .Single(item => item.Name == Localizer.Get(StringKeys.EditorVbGeneral));

        Assert.Equal(CompletionKind.Method, target.Members.Single(member => member.Name == "DoWork").Kind);
        Assert.Equal(CompletionKind.Property, target.Members.Single(member => member.Name == "Count").Kind);
        Assert.Equal(CompletionKind.Event, target.Members.Single(member => member.Name == "Changed").Kind);
    }

    /// <summary>
    /// A control event that already has a handler is marked so the list can
    /// show it in bold, the way Visual Studio marks a wired-up event;
    /// an event nothing handles yet is not.
    /// </summary>
    [Fact]
    public void ExistingHandlersAreFlaggedButUnhandledEventsAreNot()
    {
        var target = VisualBasicEditorNavigation.Read(Source, Companion).Single(item => item.Name == "Button1");
        Assert.True(target.Members.Single(member => member.EventName == "Click").HasHandler);
        Assert.False(target.Members.Single(member => member.EventName == "MouseEnter").HasHandler);
    }

    /// <summary>
    /// The Procedure list draws an already-handled event in bold and an
    /// unhandled one in normal weight — otherwise the list is only text, and
    /// the user cannot tell which events already have code without opening
    /// every one. The icon matches the member's kind.
    /// </summary>
    [AvaloniaFact]
    public void HandledEventRendersBoldWithAnIconAndUnhandledDoesNot()
    {
        var handled = new VisualBasicNavigationMember("Click", 0, "Click",
            Kind: CompletionKind.Event, HasHandler: true);
        var unhandled = new VisualBasicNavigationMember("MouseEnter", 0, "MouseEnter",
            Kind: CompletionKind.Event, HasHandler: false);

        var handledText = Assert.IsType<TextBlock>(
            FindText(CodeEditor.BuildMemberVisualForTests(handled)));
        var unhandledText = Assert.IsType<TextBlock>(
            FindText(CodeEditor.BuildMemberVisualForTests(unhandled)));

        Assert.Equal(FontWeight.Bold, handledText.FontWeight);
        Assert.Equal(FontWeight.Normal, unhandledText.FontWeight);

        // Both carry an icon: the member kind, not whether it is handled,
        // decides that.
        Assert.NotNull(FindImage(CodeEditor.BuildMemberVisualForTests(handled)));
        Assert.NotNull(FindImage(CodeEditor.BuildMemberVisualForTests(unhandled)));
    }

    /// <summary>
    /// "(Declarations)" names a position rather than a symbol, so it carries
    /// no icon — just the text, as Visual Studio shows it.
    /// </summary>
    [AvaloniaFact]
    public void PseudoDeclarationsEntryHasNoIcon()
    {
        var declarations = new VisualBasicNavigationMember(
            Localizer.Get(StringKeys.EditorVbDeclarations), 0);

        var visual = CodeEditor.BuildMemberVisualForTests(declarations);

        Assert.IsType<TextBlock>(visual);
    }

    private static Image? FindImage(Control? visual) =>
        visual as Image ?? (visual as Panel)?.Children.OfType<Image>().FirstOrDefault();

    private static TextBlock? FindText(Control? visual) =>
        visual as TextBlock ?? (visual as Panel)?.Children.OfType<TextBlock>().FirstOrDefault();

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
