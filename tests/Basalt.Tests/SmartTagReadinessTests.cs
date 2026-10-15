using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Basalt.Shell.ViewModels;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Tests;

/// <summary>
/// Ctrl+. asked before the solution has finished loading. The workspace then holds no document for the file, so the
/// answer is empty; the answer must come from the loaded solution, and an empty answer must not be kept for the same text.
/// </summary>
public sealed class SmartTagReadinessTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-smart-readiness-").FullName;

    private const string Source =
        "Module Program\n    Sub Main()\n        Dim text As New StringBuilder()\n    End Sub\nEnd Module\n";

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private (string Project, string Path) WriteProject()
    {
        var project = Path.Combine(_root, "Journey.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        var path = Path.Combine(_root, "Program.vb");
        File.WriteAllText(path, Source);
        return (project, path);
    }

    private static int CaretInStringBuilder => Source.IndexOf("StringBuilder", StringComparison.Ordinal) + 3;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "The editor did not reach the expected state.");
    }

    /// <summary>
    /// A question asked while a load holds the gate is answered only after the gate is released. The hold stands in for the
    /// MSBuild load, which holds the gate for its whole length, so the test does not depend on how fast a real load runs.
    /// </summary>
    [Fact]
    public async Task AQuestionWaitsWhileTheSolutionIsLoading()
    {
        var (project, path) = WriteProject();
        using var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(project);
        // Warm up: the first answer compiles the project, later ones take milliseconds, so an unwaited question finishes in the wait below.
        await service.GetQuickActionsAsync(path, Source, CaretInStringBuilder);

        var finishLoad = await service.HoldGateForTestsAsync();
        var question = service.GetQuickActionsAsync(path, Source, CaretInStringBuilder);
        await Task.Delay(500);
        Assert.False(question.IsCompleted, "The question was answered while the solution was still loading.");

        finishLoad();
        var actions = await question;
        Assert.Contains(actions, action => action.Title.Contains("System.Text", StringComparison.Ordinal));
    }

    /// <summary>
    /// The first question, asked when the document opened, is answered with nothing. Ctrl+. at the same text and caret must
    /// ask again rather than show the empty answer as "none".
    /// </summary>
    [AvaloniaFact]
    public async Task CtrlDotAsksAgainWhenTheLastAnswerWasEmpty()
    {
        var asked = 0;
        var fix = new QuickAction("Imports System.Text", CodeAction.Create("Imports System.Text",
            _ => Task.FromResult<Microsoft.CodeAnalysis.Document>(null!)))
        {
            ApplicableSpan = new TextSpan(CaretInStringBuilder - 3, 13)
        };
        using var vm = new MainWindowViewModel();
        var document = new EditorDocumentViewModel("Program.vb", Source) { CaretOffset = CaretInStringBuilder };
        var code = new CodeEditor(document, vm)
        {
            AutoFormatWhileTyping = false,
            SmartTagProvider = (_, _, _, _) =>
            {
                asked++;
                IReadOnlyList<QuickAction> answer = asked == 1 ? [] : [fix];
                return Task.FromResult(answer);
            },
            DescribeSymbolProvider = (_, _, _) => Task.FromResult<Basalt.Extensibility.SymbolDescription?>(null),
        };
        var window = new Window { Content = code, Width = 700, Height = 400 };
        window.Show();
        try
        {
            await WaitUntilAsync(() => asked == 1);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();

            await code.ShowSmartTagsAsync();

            Assert.Equal(2, asked);
            Assert.NotNull(code.SmartTagMargin.ContextMenu);
            Assert.Contains(code.SmartTagMargin.ContextMenu!.Items.OfType<MenuItem>(),
                item => item.Header?.ToString() == "Imports System.Text");
        }
        finally
        {
            window.Close();
        }
    }
}
