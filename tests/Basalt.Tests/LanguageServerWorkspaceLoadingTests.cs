using System.Text.Json;
using Basalt.Razor.Vb.LanguageServer;
using LspHarness = Basalt.Tests.LanguageServerProtocolTests.LspHarness;

namespace Basalt.Tests;

public sealed class LanguageServerWorkspaceLoadingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-lsp-loading-" + Guid.NewGuid().ToString("N"));

    public LanguageServerWorkspaceLoadingTests() => Directory.CreateDirectory(_root);

    public void Dispose() => ScratchFolder.Delete(_root);

    private string WriteSolution(string name, string projectName)
    {
        var projectDirectory = Path.Combine(_root, projectName);
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(projectDirectory, projectName + ".vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDirectory, "Marker.vb"), "Public Class Marker\nEnd Class");
        var solution = Path.Combine(_root, name);
        File.WriteAllText(solution, $"<Solution><Project Path=\"{projectName}/{projectName}.vbproj\" /></Solution>");
        return solution;
    }

    [Fact]
    public async Task ClientChoosesSecondSolutionAndReceivesSuccessfulProgress()
    {
        WriteSolution("First.slnx", "First");
        var chosen = WriteSolution("Second.slnx", "Second");
        await using var client = new LspHarness
        {
            ServerRequestHandler = message =>
            {
                if (message.GetProperty("method").GetString() == "window/showMessageRequest")
                {
                    var actions = message.GetProperty("params").GetProperty("actions");
                    Assert.Equal(2, actions.GetArrayLength());
                    Assert.Equal("Second.slnx", actions[1].GetProperty("title").GetString());
                    return Task.FromResult<object?>(actions[1].Clone());
                }
                Assert.Equal("window/workDoneProgress/create", message.GetProperty("method").GetString());
                return Task.FromResult<object?>(null);
            }
        };
        await client.InitializeAsync(_root, workDoneProgress: true);
        Assert.DoesNotContain(client.ServerMessages, message =>
            message.GetProperty("method").GetString() is "window/showMessageRequest" or "window/workDoneProgress/create");
        await client.InitializedAsync();
        var compilation = await client.CompilationAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await WaitForMessageAsync(client, IsLoadResult);

        Assert.True(compilation.IsReady, compilation.Problem);
        Assert.Equal(chosen, compilation.Target, ignoreCase: OperatingSystem.IsWindows());
        AssertProgress(client, "Loaded Second.slnx.");
    }

    [Theory]
    [InlineData("dismiss")]
    [InlineData("unknown")]
    [InlineData("unsupported")]
    public async Task MissingOrInvalidChoiceLeavesCompilerUnloaded(string response)
    {
        WriteSolution("First.slnx", "First");
        WriteSolution("Second.slnx", "Second");
        await using var client = new LspHarness
        {
            ServerRequestHandler = message => response switch
            {
                "unknown" => Task.FromResult<object?>(new { title = "Other.slnx" }),
                "unsupported" => Task.FromException<object?>(new InvalidOperationException("Unsupported request")),
                _ => Task.FromResult<object?>(null)
            }
        };
        await client.InitializeAsync(_root, workDoneProgress: true);
        await client.InitializedAsync();
        var compilation = await client.CompilationAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await WaitForMessageAsync(client, IsLoadResult);

        Assert.False(compilation.IsReady);
        Assert.Null(compilation.Target);
        Assert.NotNull(compilation.Problem);
        Assert.Single(client.ServerMessages, message =>
            message.GetProperty("method").GetString() == "window/showMessageRequest");
        Assert.DoesNotContain(client.ServerMessages, message =>
            message.GetProperty("method").GetString() == "window/workDoneProgress/create");
    }

    [Fact]
    public async Task FailedSolutionLoadEndsProgressWithItsFailure()
    {
        File.WriteAllText(Path.Combine(_root, "Broken.slnx"), "<Solution>");
        await using var client = new LspHarness
        {
            ServerRequestHandler = message => Task.FromResult<object?>(null)
        };
        await client.InitializeAsync(_root, workDoneProgress: true);
        await client.InitializedAsync();
        var compilation = await client.CompilationAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await WaitForMessageAsync(client, IsLoadResult);

        Assert.False(compilation.IsReady);
        Assert.NotNull(compilation.Problem);
        AssertProgress(client, "Project loading failed:");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedOrRejectedProgressDoesNotPreventLoading(bool rejected)
    {
        var chosen = WriteSolution("Only.slnx", "Only");
        await using var client = new LspHarness
        {
            ServerRequestHandler = message => Task.FromException<object?>(
                new InvalidOperationException("Progress is unavailable"))
        };
        await client.InitializeAsync(_root, workDoneProgress: rejected);
        await client.InitializedAsync();
        var compilation = await client.CompilationAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await WaitForMessageAsync(client, IsLoadResult);

        Assert.True(compilation.IsReady, compilation.Problem);
        Assert.Equal(chosen, compilation.Target, ignoreCase: OperatingSystem.IsWindows());
        Assert.DoesNotContain(client.ServerMessages, message =>
            message.GetProperty("method").GetString() == "$/progress");
        Assert.Equal(rejected ? 1 : 0, client.ServerMessages.Count(message =>
            message.GetProperty("method").GetString() == "window/workDoneProgress/create"));
    }

    private static bool IsLoadResult(JsonElement message) =>
        message.GetProperty("method").GetString() == "window/logMessage" &&
        message.GetProperty("params").GetProperty("message").GetString() is { } text &&
        (text.StartsWith("Basalt: the project is loaded", StringComparison.Ordinal) ||
            text.StartsWith("Basalt: no project loaded", StringComparison.Ordinal));

    private static async Task WaitForMessageAsync(LspHarness client, Func<JsonElement, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!client.ServerMessages.Any(predicate))
        {
            Assert.True(DateTime.UtcNow < deadline, "The server did not finish reporting its project load.");
            await Task.Delay(25);
        }
        // The result log precedes progress disposal, so wait for that end too
        // when progress was begun instead of assuming both arrive together.
        if (client.ServerMessages.Any(message => IsProgressKind(message, "begin")))
            while (!client.ServerMessages.Any(message => IsProgressKind(message, "end")))
            {
                Assert.True(DateTime.UtcNow < deadline, "The server did not end project loading progress.");
                await Task.Delay(25);
            }
    }

    private static bool IsProgressKind(JsonElement message, string kind) =>
        message.GetProperty("method").GetString() == "$/progress" &&
        message.GetProperty("params").GetProperty("value").GetProperty("kind").GetString() == kind;

    private static void AssertProgress(LspHarness client, string expectedEnd)
    {
        var begin = Assert.Single(client.ServerMessages, message => IsProgressKind(message, "begin"));
        var end = Assert.Single(client.ServerMessages, message => IsProgressKind(message, "end"));
        var create = Assert.Single(client.ServerMessages, message =>
            message.GetProperty("method").GetString() == "window/workDoneProgress/create");
        var token = create.GetProperty("params").GetProperty("token").GetRawText();
        Assert.Equal(token, begin.GetProperty("params").GetProperty("token").GetRawText());
        Assert.Equal(token, end.GetProperty("params").GetProperty("token").GetRawText());
        var beginValue = begin.GetProperty("params").GetProperty("value");
        Assert.False(beginValue.TryGetProperty("cancellable", out var cancellable) && cancellable.GetBoolean());
        Assert.StartsWith(expectedEnd, end.GetProperty("params").GetProperty("value").GetProperty("message").GetString());
    }
}
