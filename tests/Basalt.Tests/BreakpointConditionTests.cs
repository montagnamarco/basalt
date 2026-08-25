using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Services;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;
using StackFrame = Basalt.Core.Services.StackFrame;

namespace Basalt.Tests;

/// <summary>The window where a breakpoint's condition is set.</summary>
public class BreakpointConditionDialogTests
{
    private static Breakpoint At(int line, string? condition = null, int? hits = null) =>
        new("/src/Program.vb", line, Condition: condition, HitCount: hits);

    [AvaloniaFact]
    public void StartsFromWhatTheBreakpointAlreadyHas()
    {
        var dialog = new BreakpointConditionDialog(At(6, "i = 4", 3));

        Assert.Equal("i = 4", dialog.ConditionText);
        Assert.Equal("3", dialog.HitCountText);
    }

    [AvaloniaFact]
    public void StartsEmptyForABreakpointWithNoCondition()
    {
        var dialog = new BreakpointConditionDialog(At(6));

        Assert.Equal("", dialog.ConditionText);
        Assert.Equal("", dialog.HitCountText);
    }

    [AvaloniaFact]
    public void KeepsWhatWasTyped()
    {
        var dialog = new BreakpointConditionDialog(At(6));

        dialog.ConditionText = "total > 10";
        dialog.HitCountText = "5";
        dialog.Accept(At(6));

        Assert.Equal("total > 10", dialog.Result!.Condition);
        Assert.Equal(5, dialog.Result.HitCount);
    }

    [AvaloniaFact]
    public void TreatsAnEmptyConditionAsNone()
    {
        var dialog = new BreakpointConditionDialog(At(6, "i = 4"));

        dialog.ConditionText = "   ";
        dialog.Accept(At(6));

        Assert.Null(dialog.Result!.Condition);
    }

    [AvaloniaFact]
    public void TreatsAnUnreadableHitCountAsEveryTime()
    {
        // The user meant "every time" and typed something odd; refusing it
        // would be worse than taking the meaning.
        var dialog = new BreakpointConditionDialog(At(6));

        dialog.HitCountText = "lots";
        dialog.Accept(At(6));

        Assert.Null(dialog.Result!.HitCount);
    }

    [AvaloniaFact]
    public void ShowsWhatTheDebuggerWillActuallyEvaluate()
    {
        // Its evaluator reads C# whatever the program is written in, so
        // "i = 4" becomes "i == 4". A surprising result should be explainable.
        var dialog = new BreakpointConditionDialog(At(6));

        dialog.ConditionText = "i = 4";

        Assert.Contains("i == 4", dialog.PreviewText);
    }

    [AvaloniaFact]
    public void SaysNothingWhenTheConditionNeedsNoTranslation()
    {
        var dialog = new BreakpointConditionDialog(At(6));

        dialog.ConditionText = "i > 4";

        Assert.Equal("", dialog.PreviewText);
    }

    [AvaloniaFact]
    public void SaysNothingForALanguageThatIsNotBasic()
    {
        var dialog = new BreakpointConditionDialog(new Breakpoint("/src/Program.cs", 6));

        dialog.ConditionText = "i = 4";

        Assert.Equal("", dialog.PreviewText);
    }
}

/// <summary>The menu on the breakpoint margin.</summary>
public class BreakpointMarginMenuTests
{
    [AvaloniaFact]
    public void HasAMenu()
    {
        Assert.NotNull(new BreakpointMargin().Menu);
    }

    [AvaloniaFact]
    public void OffersToggleConditionAndRunToHere()
    {
        var margin = new BreakpointMargin();

        var headers = margin.Menu!.ItemsSource!
            .OfType<MenuItem>()
            .Select(i => i.Header?.ToString())
            .ToList();

        Assert.Contains(headers, h => h?.Contains("Breakpoint") == true);
        Assert.Contains("Condition…", headers);
        Assert.Contains("Run to Here", headers);
    }

    [AvaloniaFact]
    public void AsksForAConditionOnTheLineTheMenuWasOpenedOn()
    {
        var margin = new BreakpointMargin();

        var asked = 0;
        margin.ConditionRequested += (_, line) => asked = line;

        margin.OpenMenuForTests(12);

        margin.Menu!.ItemsSource!.OfType<MenuItem>()
            .Single(i => i.Header?.ToString() == "Condition…")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(12, asked);
    }
}

/// <summary>
/// A condition set from the IDE, honoured by the real debugger.
///
/// The dialog and the translator are each tested on their own; this is the
/// one that says the two, plus the session and netcoredbg, agree.
/// </summary>
public sealed class ConditionalBreakpointEndToEndTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-cond", Guid.NewGuid().ToString("N"));

    private string _sourcePath = "";
    private string _assemblyPath = "";

    private static string? AdapterPath => DebugAdapterLocator.Find();

    public async ValueTask InitializeAsync()
    {
        if (AdapterPath is null) return;

        Directory.CreateDirectory(_root);

        _sourcePath = Path.Combine(_root, "Program.vb");

        await File.WriteAllTextAsync(_sourcePath, """
            Namespace CondTarget
                Module Program
                    Sub Main()
                        Dim total As Integer = 0
                        For i As Integer = 1 To 5
                            total += i
                        Next
                        Console.WriteLine(total)
                    End Sub
                End Module
            End Namespace
            """);

        await File.WriteAllTextAsync(Path.Combine(_root, "CondTarget.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <DebugType>portable</DebugType>
              </PropertyGroup>
            </Project>
            """);

        var build = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "build", "-c", "Debug", "--nologo", "-v", "q" },
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        await build.WaitForExitAsync();

        _assemblyPath = Path.Combine(_root, "bin", "Debug", "net10.0", "CondTarget.dll");
    }

    [AvaloniaFact]
    public async Task AConditionSetThroughTheDialogStopsAtTheRightPass()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var session = new DebugSession();

        // Exactly what the margin menu does: toggle, then apply what the
        // dialog produced.
        await session.ToggleBreakpointAsync(_sourcePath, 6);

        var breakpoint = session.BreakpointsIn(_sourcePath).Single();

        var dialog = new BreakpointConditionDialog(breakpoint);
        dialog.ConditionText = "i = 4";
        dialog.Accept(breakpoint);

        await session.SetBreakpointConditionAsync(
            _sourcePath, 6, dialog.Result!.Condition);

        var paused = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        session.Paused += (_, frame) => paused.TrySetResult(frame);

        await session.StartAsync(_assemblyPath, _root);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var locals = await session.GetLocalsAsync();

        // The condition was written in Visual Basic and honoured as such:
        // stopped on the fourth pass, with 1 + 2 + 3 already added.
        Assert.Equal("4", locals.Single(v => v.Name == "i").Value);
        Assert.Equal("6", locals.Single(v => v.Name == "total").Value);
    }

    [AvaloniaFact]
    public async Task AHitCountSetThroughTheDialogSkipsThePassesBeforeIt()
    {
        Assert.SkipWhen(AdapterPath is null, "netcoredbg is not installed.");

        using var session = new DebugSession();

        await session.ToggleBreakpointAsync(_sourcePath, 6);
        await session.SetBreakpointHitCountAsync(_sourcePath, 6, 3);

        var paused = new TaskCompletionSource<StackFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        session.Paused += (_, frame) => paused.TrySetResult(frame);

        await session.StartAsync(_assemblyPath, _root);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var locals = await session.GetLocalsAsync();

        Assert.Equal("3", locals.Single(v => v.Name == "i").Value);
    }

    public ValueTask DisposeAsync()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }
}
