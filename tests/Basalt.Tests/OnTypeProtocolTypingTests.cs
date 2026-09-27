using System.Text.Json;
using Microsoft.CodeAnalysis.Text;
using Basalt.Razor.Vb.LanguageServer;
using Basalt.Workspace.Web;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspHarness = Basalt.Tests.LanguageServerProtocolTests.LspHarness;

namespace Basalt.Tests;

public sealed class OnTypeProtocolTypingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "basalt-on-type-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => ScratchFolder.Delete(_root);

    private async Task<string> OnTypeAsync(string before, string changed, int line, int character, string trigger)
    {
        Directory.CreateDirectory(_root);
        var uri = new Uri(Path.Combine(_root, "Index.vbhtml")).AbsoluteUri;
        await using var client = new LspHarness();
        await client.InitializeAsync(_root);
        await client.InitializedAsync();
        await client.DidOpenAsync(uri, before);
        await client.DidChangeAsync(uri, changed);
        var response = await client.RequestAsync("textDocument/onTypeFormatting", new
        {
            textDocument = new { uri }, position = new { line, character }, ch = trigger,
            options = new { tabSize = 4, insertSpaces = true }
        });
        if (response.ValueKind == JsonValueKind.Null) return changed;
        var source = SourceText.From(changed);
        var edits = response.EnumerateArray().Select(edit =>
        {
            var range = edit.GetProperty("range");
            var start = range.GetProperty("start");
            var end = range.GetProperty("end");
            var from = source.Lines.GetPosition(new LinePosition(start.GetProperty("line").GetInt32(),
                start.GetProperty("character").GetInt32()));
            var to = source.Lines.GetPosition(new LinePosition(end.GetProperty("line").GetInt32(),
                end.GetProperty("character").GetInt32()));
            return new TextChange(new TextSpan(from, to - from), edit.GetProperty("newText").GetString()!);
        });
        return source.WithChanges(edits).ToString();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task NewlineFormatsTheFinishedHeaderAndReusesItsAlreadyInsertedBodyLine(string newline)
    {
        var before = string.Join(newline, "<p>😀 header</p>", "@Code", "    if x=1 then", "    Return 7", "End Code");
        var changed = before.Replace("then" + newline, "then" + newline + newline, StringComparison.Ordinal);
        var actual = await OnTypeAsync(before, changed, 3, 0, "\n");
        Assert.Equal(string.Join(newline, "<p>😀 header</p>", "@Code", "    If x = 1 Then", "        ",
            "    End If", "    Return 7", "End Code"), actual);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task NewlineDoesNotConsumeFollowingCodeWhenTheClientHasNoSeparateBlankLine(string newline)
    {
        var before = string.Join(newline, "<p>😀 header</p>", "@Code", "    if x=1 then    Return 7", "End Code");
        var changed = before.Replace("then    Return", "then" + newline + "    Return", StringComparison.Ordinal);
        var actual = await OnTypeAsync(before, changed, 3, 0, "\n");
        Assert.Equal(string.Join(newline, "<p>😀 header</p>", "@Code", "    If x = 1 Then", "        ",
            "    End If", "    Return 7", "End Code"), actual);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task NewlineAfterAnExistingClosingOnlyFormatsThePreviousLine(string newline)
    {
        var before = string.Join(newline, "@Code", "    If x = 1 Then", "    end if", "    Return 7", "End Code");
        var changed = before.Replace("end if" + newline, "end if" + newline + newline, StringComparison.Ordinal);
        var actual = await OnTypeAsync(before, changed, 3, 0, "\n");
        Assert.Equal(changed.Replace("end if", "End If", StringComparison.Ordinal), actual);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task SpaceFormatsTheCurrentLineWithoutClosingTheBlock(string newline)
    {
        var before = string.Join(newline, "@Code", "    if x=1 then", "    Return 7", "End Code");
        var changed = before.Replace("then" + newline, "then " + newline, StringComparison.Ordinal);
        var actual = await OnTypeAsync(before, changed, 1, "    if x=1 then ".Length, " ");
        Assert.Equal(before.Replace("if x=1 then", "If x = 1 Then", StringComparison.Ordinal), actual);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task EnterAfterAnOpeningWithAnExistingMatchingClosingDoesNotDuplicateIt(string newline)
    {
        var before = string.Join(newline, "@Code", "    if x=1 then", "    End If", "    Return 7", "End Code");
        var changed = before.Replace("then" + newline, "then" + newline + newline, StringComparison.Ordinal);
        var actual = await OnTypeAsync(before, changed, 2, 0, "\n");
        Assert.Equal(changed.Replace("if x=1 then", "If x = 1 Then", StringComparison.Ordinal), actual);
    }

    [Fact]
    public async Task AlreadyCanceledSpaceRequestCannotReturnAReplacement()
    {
        // The server's own spelling of the URI, as the store is keyed by it:
        // Uri.AbsoluteUri keeps "C:", DocumentUri encodes the colon.
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "canceled-format.vbhtml")).ToString();
        var documents = new DocumentStore();
        documents.Update(uri, "@Code\n    if x=1 then \nEnd Code", 1);
        var handler = new VbHtmlOnTypeFormattingHandler(documents);
        var request = new DocumentOnTypeFormattingParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(1, 16),
            Character = " ", Options = new FormattingOptions { TabSize = 4, InsertSpaces = true }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await handler.Handle(request, new CancellationToken(canceled: true)));
    }

    [Theory]
    [InlineData("change")]
    [InlineData("close")]
    [InlineData("cancel")]
    public async Task InvalidatedRequestCannotPublishThePendingRealFormatting(string invalidation)
    {
        // The server's own spelling of the URI, as the store is keyed by it:
        // Uri.AbsoluteUri keeps "C:", DocumentUri encodes the colon.
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "pending-format.vbhtml")).ToString();
        var documents = new DocumentStore();
        documents.Update(uri, "@Code\n    if x=1 then \nEnd Code", 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var formatter = new VbHtmlFormattingProvider();
        var handler = new VbHtmlOnTypeFormattingHandler(documents, async (document, caret, ct) =>
        {
            var result = await formatter.FormatLineAsync(document, caret, ct);
            Assert.True(result.Changed);
            entered.SetResult();
            await resume.Task;
            return result;
        });
        var request = new DocumentOnTypeFormattingParams
        {
            TextDocument = new TextDocumentIdentifier(uri), Position = new Position(1, 16),
            Character = " ", Options = new FormattingOptions { TabSize = 4, InsertSpaces = true }
        };
        var pending = handler.Handle(request, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (invalidation == "close") documents.Remove(uri);
        else if (invalidation == "change") documents.Update(uri, "@Code\n    Return 99\nEnd Code", 2);
        else await cancellation.CancelAsync();
        resume.SetResult();
        if (invalidation == "cancel")
        {
            // The request itself is what is awaited: it was started above, on
            // purpose, so it could be invalidated while it ran.
#pragma warning disable VSTHRD003
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
#pragma warning restore VSTHRD003
            return;
        }
        Assert.Null(await pending);
        Assert.Equal(invalidation == "close" ? null : "@Code\n    Return 99\nEnd Code", documents.Get(uri)?.Text);
    }
}
