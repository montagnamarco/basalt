using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Basalt.Designer.Toolbox;
using Basalt.Workspace;
using Basalt.Shell.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

/// <summary>
/// Dragging the user's own control from the Toolbox onto a form: the drop is accepted by the same rule as
/// a double-click, so a control of the project's design context lands where a Forms control does.
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WinFormsUserControlDropTests : IAsyncLifetime
{
    private WinFormsUserControlProject? _project;
    private string _outputAssembly = null!;

    public async ValueTask InitializeAsync()
    {
        if (OperatingSystem.IsWindows() || WindowsDesktopSamples.FindSources() is null) return;

        _project = WinFormsUserControlProject.Create();
        var build = await new MsBuildBuildService().BuildAsync(_project.ProjectPath);
        Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Diagnostics));
        _outputAssembly = Assert.IsType<string>(build.OutputAssemblyPath);
    }

    public ValueTask DisposeAsync()
    {
        _project?.Dispose();
        return ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public void ADragOfAProjectControlIsAcceptedAndReportedAsADropOnTheForm()
    {
        RequireUnix();
        var context = WinFormsDesignAssemblyContext.Open(_outputAssembly);
        try
        {
            var gauge = context.ProjectAssembly.GetType("App.GaugeControl", throwOnError: true)!;
            var form = new Forms.Form { ClientSize = new Drawing.Size(300, 200), FormBorderStyle = Forms.FormBorderStyle.None };
            var surface = new WinFormsDesignSurface { RootForm = form, DesignContext = context };
            var window = new Window { Content = surface, Width = 380, Height = 280 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var received = new List<WinFormsControlDrop>();
                surface.ControlDropRequested += received.Add;
                var transfer = Transfer(gauge);
                var position = surface.TranslatePoint(new Point(12 + 40, 12 + 30), window)!.Value;

                var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, window, position, KeyModifiers.None);
                surface.RaiseEvent(over);
                Assert.Equal(DragDropEffects.Copy, over.DragEffects);

                var drop = new DragEventArgs(DragDrop.DropEvent, transfer, window, position, KeyModifiers.None);
                surface.RaiseEvent(drop);
                Dispatcher.UIThread.RunJobs();

                var placement = Assert.Single(received);
                Assert.Same(gauge, placement.ControlType);
                Assert.Same(form, placement.Parent);
            }
            finally { window.Close(); form.Dispose(); }
        }
        finally { context.Unload(); }
    }

    [AvaloniaFact]
    public void ADragOfATypeTheDesignContextDoesNotHaveIsStillRefused()
    {
        RequireUnix();
        var context = WinFormsDesignAssemblyContext.Open(_outputAssembly);
        try
        {
            var form = new Forms.Form { ClientSize = new Drawing.Size(300, 200), FormBorderStyle = Forms.FormBorderStyle.None };
            var surface = new WinFormsDesignSurface { RootForm = form, DesignContext = context };
            var window = new Window { Content = surface, Width = 380, Height = 280 };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var received = new List<WinFormsControlDrop>();
                surface.ControlDropRequested += received.Add;
                var transfer = Transfer(typeof(WinFormsUserControlDropTests));
                var position = surface.TranslatePoint(new Point(12 + 40, 12 + 30), window)!.Value;

                var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, window, position, KeyModifiers.None);
                surface.RaiseEvent(over);
                Assert.Equal(DragDropEffects.None, over.DragEffects);
                surface.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, window, position, KeyModifiers.None));
                Dispatcher.UIThread.RunJobs();

                Assert.Empty(received);
            }
            finally { window.Close(); form.Dispose(); }
        }
        finally { context.Unload(); }
    }

    private static DataTransfer Transfer(Type controlType)
    {
        var item = new DataTransferItem();
        item.Set(WinFormsToolboxDrag.Format, new WinFormsToolboxItem(controlType));
        item.SetText(controlType.Name);
        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    private static void RequireUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();
    }
}
