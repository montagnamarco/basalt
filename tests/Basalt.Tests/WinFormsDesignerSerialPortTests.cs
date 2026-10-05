using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public sealed class WinFormsDesignerSerialPortTests
{
    private const string Source = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.components = New System.ComponentModel.Container()
                Me.SerialPort1 = New Global.System.IO.Ports.SerialPort(Me.components)
                Me.SerialPort1.PortName = "COM42"
                Me.SerialPort1.BaudRate = 19200
                Me.ResumeLayout(False)
            End Sub
            Private components As System.ComponentModel.IContainer
            Friend WithEvents SerialPort1 As Global.System.IO.Ports.SerialPort
            Private Sub SerialPort1_DataReceived(sender As Object, e As Global.System.IO.Ports.SerialDataReceivedEventArgs) Handles SerialPort1.DataReceived
            End Sub
        End Class
        """;

    [Fact]
    public void SerialPortIsATrayComponentAndGeneratedSourceUsesExistingContainer()
    {
        var document = new WinFormsDesignerDocument(Source);
        Assert.Empty(document.Controls);
        var model = Assert.Single(document.Components);
        Assert.Equal("SerialPort1", model.Name);
        Assert.Equal("19200", model.Properties["BaudRate"]);

        var added = document.AddComponent("SerialPort2", "Global.System.IO.Ports.SerialPort");
        Assert.Contains("Me.SerialPort2 = New Global.System.IO.Ports.SerialPort(Me.components)", added);
        Assert.Contains("Friend WithEvents SerialPort2 As Global.System.IO.Ports.SerialPort", added);
        Assert.DoesNotContain("Me.Controls.Add(Me.SerialPort2)", added);
        AssertCompiles(added);
    }

    [Fact]
    public void SerialPortWithoutAnExistingContainerUsesParameterlessConstruction()
    {
        const string noContainer = """
            Partial Class MainForm
                Inherits System.Windows.Forms.Form
                Private Sub InitializeComponent()
                    Me.ResumeLayout(False)
                End Sub
            End Class
            """;
        var added = new WinFormsDesignerDocument(noContainer)
            .AddComponent("SerialPort1", "Global.System.IO.Ports.SerialPort");

        Assert.Contains("Me.SerialPort1 = New Global.System.IO.Ports.SerialPort()", added);
        Assert.DoesNotContain("Me.components", added);
        AssertCompiles(added);
    }

    [AvaloniaFact]
    public async Task PreviewStaysClosedWhilePropertiesAndUndoUseTheDesignerSource()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(Source));
        var port = Assert.IsType<SerialPort>(session.FindComponent("SerialPort1"));
        Assert.False(port.IsOpen);
        Assert.Equal("COM42", port.PortName);
        Assert.Equal(19200, port.BaudRate);
        Assert.NotNull(System.ComponentModel.TypeDescriptor.GetEvents(port)[nameof(SerialPort.DataReceived)]);

        await session.SetComponentPropertyAsync(port, "BaudRate", "38400");
        Assert.Contains("Me.SerialPort1.BaudRate = 38400", session.Document.Source);
        Assert.Equal(38400, Assert.IsType<SerialPort>(session.FindComponent("SerialPort1")).BaudRate);
        Assert.False(Assert.IsType<SerialPort>(session.FindComponent("SerialPort1")).IsOpen);
        await session.UndoAsync();
        Assert.Equal(Source, session.Document.Source);
        Assert.Equal(19200, Assert.IsType<SerialPort>(session.FindComponent("SerialPort1")).BaudRate);
    }

    [AvaloniaFact]
    public async Task ToolboxAddsSerialPortToTheTray()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", Source, true);
        using var view = new WinFormsDesignerView(document);
        var toolbox = Assert.IsType<ListBox>(view.ToolboxContent);
        var item = Assert.Single(toolbox.ItemsSource!.Cast<WinFormsToolboxItem>(),
            candidate => candidate.ControlType == typeof(SerialPort));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var tray = Assert.Single(view.GetVisualDescendants().OfType<WinFormsComponentTray>());
            await WaitUntilAsync(() => tray.GetVisualDescendants().OfType<ListBox>()
                .SingleOrDefault()?.ItemsSource?.Cast<object>().Count() == 1);
            toolbox.SelectedItem = item;
            toolbox.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
            await WaitUntilAsync(() => document.Text.Contains("Friend WithEvents SerialPort2", StringComparison.Ordinal));
            Assert.Contains("Friend WithEvents SerialPort2", document.Text);
            Assert.Contains(tray.GetVisualDescendants().OfType<ListBox>().Single().ItemsSource!.Cast<object>(),
                entry => entry.ToString()!.StartsWith("SerialPort2", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TrayDoubleClickCreatesDataReceivedHandlerAndUndoRestoresCode()
    {
        const string originalCode = "Partial Class MainForm\nEnd Class\n";
        var originalDesigner = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddComponent("SerialPort1", "Global.System.IO.Ports.SerialPort");
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", originalDesigner, true);
        var code = new EditorDocumentViewModel("MainForm.vb", originalCode);
        using var view = new WinFormsDesignerView(designer, _ => Task.FromResult(code));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var tray = Assert.Single(view.GetVisualDescendants().OfType<WinFormsComponentTray>());
            var list = Assert.Single(tray.GetVisualDescendants().OfType<ListBox>());
            await WaitUntilAsync(() => list.ItemsSource?.Cast<object>().Count() == 1);
            list.SelectedIndex = 0;
            list.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
            await WaitUntilAsync(() => code.Text.Contains("Handles SerialPort1.DataReceived", StringComparison.Ordinal));
            Assert.Equal(originalDesigner, designer.Text);
            Assert.Contains("SerialPort1_DataReceived", code.Text);

            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => code.Text == originalCode);
        }
        finally { window.Close(); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The component operation did not finish.");
    }

    private static void AssertCompiles(string source)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(Forms.Form).Assembly.Location, typeof(SerialPort).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = VisualBasicCompilation.Create("SerialPortDesigner",
            [VisualBasicSyntaxTree.ParseText(source)],
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
