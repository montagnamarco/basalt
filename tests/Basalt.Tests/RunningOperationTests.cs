using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Services;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Showing what is running, and stopping it.
///
/// A build that has gone wrong is one the user wants to end rather than wait
/// out, so the cancelling has to reach the work: a button that only hides
/// itself would be worse than none.
/// </summary>
public sealed class RunningOperationTests
{
    [Fact]
    public void HasNothingRunningToStartWith()
    {
        var tracker = new OperationTracker();

        Assert.Null(tracker.Current);
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void ShowsWhatWasStarted()
    {
        var tracker = new OperationTracker();

        tracker.Start("Building…");

        Assert.Equal("Building…", tracker.Current?.Description);
    }

    [Fact]
    public void ForgetsAnOperationThatFinished()
    {
        var tracker = new OperationTracker();

        var operation = tracker.Start("Building…");

        tracker.Finish(operation);

        Assert.Null(tracker.Current);
    }

    [Fact]
    public void OffersTheMostRecentWhenSeveralAreRunning()
    {
        // It is the one the user just started and is waiting on.
        var tracker = new OperationTracker();

        tracker.Start("Opening…");
        tracker.Start("Building…");

        Assert.Equal("Building…", tracker.Current?.Description);
        Assert.Equal(2, tracker.Count);
    }

    [Fact]
    public void CancellingReachesTheWorkItself()
    {
        // The token is what the build is given, so cancelling it is what
        // stops the build rather than merely stopping the wait.
        var tracker = new OperationTracker();

        var operation = tracker.Start("Building…");

        Assert.False(operation.Token.IsCancellationRequested);

        tracker.CancelCurrent();

        Assert.True(operation.Token.IsCancellationRequested);
        Assert.True(operation.IsCancelled);
    }

    [Fact]
    public void SaysWhenSomethingStartsAndFinishes()
    {
        var tracker = new OperationTracker();

        var changes = 0;
        tracker.Changed += (_, _) => changes++;

        var operation = tracker.Start("Building…");
        tracker.Finish(operation);

        Assert.Equal(2, changes);
    }

    [Fact]
    public void DoesNothingWhenAskedToCancelNothing()
    {
        // Rather than throwing while the status bar is being clicked.
        new OperationTracker().CancelCurrent();
    }

    [AvaloniaFact]
    public async Task HidesTheStopButtonWhileNothingIsRunning()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        var button = host.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(b => b.Name == "CancelOperationButton");

        Assert.False(button.IsVisible);
    }

    [AvaloniaFact]
    public async Task ShowsTheStopButtonWhileSomethingIs()
    {
        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await host.SettleAsync();

        vm.Operations.Start("Building…");

        await host.SettleAsync();

        var button = host.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(b => b.Name == "CancelOperationButton");

        Assert.True(button.IsVisible);

        var label = host.Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(t => t.Name == "CancelOperationLabel");

        Assert.Contains("Building", label.Text ?? "", StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task StoppingFromTheStatusBarCancelsTheWork()
    {
        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await host.SettleAsync();

        var operation = vm.Operations.Start("Building…");

        await host.SettleAsync();

        host.Window.GetVisualDescendants()
            .OfType<Button>()
            .Single(b => b.Name == "CancelOperationButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.True(operation.Token.IsCancellationRequested);
    }
}
