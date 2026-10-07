using Avalonia.Headless.XUnit;
using Basalt.Shell.Docking;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// The bottom dock must stay small while editing and grow into Visual
/// Studio's "Debug" layout only while a session is running (bottom dock
/// clutter: Problems, Output, Search, References, Object Browser, the debug
/// panels, Tests, Database, History, Diff and the terminal all docked by
/// default at once). MainWindow wires <see cref="IdeDockFactory.EnterDebugLayout"/>
/// and <see cref="IdeDockFactory.ExitDebugLayout"/> to the debug session's
/// Started/Exited events and to the Stop command (see MainWindow.axaml.cs);
/// these exercise the factory's own half of that switch directly, the way
/// DockingTests exercises the rest of the layout.
/// </summary>
public sealed class DebugLayoutTests
{
    private static IdeDockFactory NewFactory()
    {
        var factory = new IdeDockFactory { TerminalWorkingDirectory = Path.GetTempPath() };
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        return factory;
    }

    private static IReadOnlyList<IDockable> DebugTools(IdeDockFactory factory) =>
        [factory.CallStack, factory.Variables, factory.Watch, factory.Breakpoints, factory.Immediate];

    [AvaloniaFact]
    public void TheDesignLayoutsBottomStripHasOnlyProblemsOutputAndTheTerminal()
    {
        var factory = NewFactory();
        var bottom = factory.BottomArea!.VisibleDockables!;

        Assert.Equal(3, bottom.Count);
        Assert.Contains(factory.Problems, bottom);
        Assert.Contains(factory.Output, bottom);
        Assert.Contains(bottom, d => d is TerminalTool);

        // The rest open on demand instead of crowding the strip from the start.
        Assert.DoesNotContain(factory.Search, bottom);
        Assert.DoesNotContain(factory.References, bottom);
        Assert.DoesNotContain(factory.ObjectBrowser, bottom);
        Assert.DoesNotContain(factory.Tests, bottom);
        Assert.DoesNotContain(factory.Database, bottom);
        Assert.DoesNotContain(factory.GitHistory, bottom);
        Assert.DoesNotContain(factory.GitDiff, bottom);
        Assert.All(DebugTools(factory), tool => Assert.DoesNotContain(tool, bottom));
    }

    [AvaloniaFact]
    public void EnteringTheDebugLayoutShowsCallStackVariablesWatchBreakpointsAndImmediate()
    {
        var factory = NewFactory();

        factory.EnterDebugLayout();

        var bottom = factory.BottomArea!.VisibleDockables!;
        Assert.All(DebugTools(factory), tool => Assert.Contains(tool, bottom));
    }

    [AvaloniaFact]
    public void LeavingTheDebugLayoutHidesTheDebugToolsAgain()
    {
        var factory = NewFactory();

        factory.EnterDebugLayout();
        factory.ExitDebugLayout();

        var bottom = factory.BottomArea!.VisibleDockables!;
        Assert.All(DebugTools(factory), tool => Assert.DoesNotContain(tool, bottom));

        // Problems, Output and the terminal were never the debug layout's to
        // touch: leaving it must not take them away too.
        Assert.Contains(factory.Problems, bottom);
        Assert.Contains(factory.Output, bottom);
    }

    [AvaloniaFact]
    public void ATooLTheUserOpenedBeforeDebuggingIsNotClosedByLeavingTheDebugLayout()
    {
        // "User changes to the layout should be respected within each mode":
        // a debug tool the session itself opened is the one EnterDebugLayout
        // tracks and ExitDebugLayout may take back; one the user already had
        // open is not its to close.
        var factory = NewFactory();
        factory.ShowTool(factory.Watch, factory.BottomArea!);

        factory.EnterDebugLayout();
        factory.ExitDebugLayout();

        Assert.Contains(factory.Watch, factory.BottomArea!.VisibleDockables!);
    }
}
