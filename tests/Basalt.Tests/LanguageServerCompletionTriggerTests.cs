using Basalt.Razor.Vb;
using Basalt.Razor.Vb.LanguageServer;
using Basalt.Workspace.Web;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// A space after "= New " in a view's code, through the language server:
/// the list the IDE shows, with the same entry preselected and the same
/// text written, for the editors that use the server.
/// </summary>
public sealed class LanguageServerCompletionTriggerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-lsp-trigger-" + Guid.NewGuid().ToString("N"));

    public LanguageServerCompletionTriggerTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), """
            Imports Microsoft.AspNetCore.Builder
            Public Module Program
                Public Sub Main(args As String())
                    WebApplication.CreateBuilder(args).Build().Run()
                End Sub
            End Module
            """);
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    /// <summary>
    /// The statement last in its block, the block's only statement, and one
    /// in the middle; with an expression after the block, which the nearest
    /// mapping used to answer about instead; and after an "@" in a string,
    /// which is not an expression.
    /// </summary>
    [Theory]
    [InlineData("", "", "", "<p>Hello</p>")]
    [InlineData("    Dim first = 1\n", "", "", "<p>@DateTime.Now</p>")]
    [InlineData("    Dim first = 1\n", "", "    Dim last = 2\n", "<p>@DateTime.Now</p>")]
    [InlineData("    Dim first = 1\n", "Dim mail = \"a@b.c\" : ", "", "<p>@DateTime.Now</p>")]
    public async Task ASpaceAfterNewPreselectsTheDeclaredTypeAndInsertsWhatRoslynWrites(
        string before, string sameLine, string after, string markup)
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var documents = new DocumentStore();
        var documentUri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Index.vbhtml"));
        var statement = "    " + sameLine + "Dim options As System.Text.Json.JsonSerializerOptions = New ";
        var text = "@Code\n" + before + statement + "\n" + after + "End Code\n" + markup;
        documents.Update(documentUri.ToString(), text, 1);

        var handler = new VbHtmlCompletionHandler(documents, compilation);
        var line = 1 + before.Count(character => character == '\n');
        var items = await handler.Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(documentUri),
            Position = new Position(line, statement.Length),
            Context = new CompletionContext
            {
                TriggerKind = CompletionTriggerKind.TriggerCharacter,
                TriggerCharacter = " "
            }
        }, default);

        var preselected = Assert.Single(items, item => item.Preselect);

        // Views import System.Text but not System.Text.Json: Roslyn writes
        // the type qualified from what is imported, the list filters on its
        // bare name, and the bare name does not compile.
        Assert.EndsWith("JsonSerializerOptions", preselected.Label, StringComparison.Ordinal);
        Assert.Equal("Json.JsonSerializerOptions", preselected.InsertText);
    }

    [Fact]
    public async Task ABodyLineThatCallsWriteItselfIsCarriedAcrossAsWritten()
    {
        // The writer puts expressions inside Write(...); a statement that
        // calls Write itself is copied as it stands, and the caret after its
        // dot must stay after the dot, not move past the parenthesis.
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var documents = new DocumentStore();
        var documentUri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Writes.vbhtml"));
        const string statement = "    Write(DateTime.Now.";
        documents.Update(documentUri.ToString(), "@Code\n    Dim first = 1\n" + statement + ")\nEnd Code\n", 1);

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(documentUri),
            Position = new Position(2, statement.Length),
            Context = new CompletionContext
            {
                TriggerKind = CompletionTriggerKind.TriggerCharacter,
                TriggerCharacter = "."
            }
        }, default);

        Assert.Contains(items, item => item.Label == "AddDays");
    }

    [Theory]
    [InlineData("        Return x.", ".", "CompareTo")]
    [InlineData("        Dim options As System.Text.Json.JsonSerializerOptions = New ", " ", "JsonSerializerOptions")]
    public async Task MembersDeclaredInFunctionsAreVisualBasicToo(string statement, string typed, string expected)
    {
        // The members of a view's @Functions block were never counted as
        // code: every list there was asked of HTML and came back empty.
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var documents = new DocumentStore();
        var documentUri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Functions.vbhtml"));
        var text = "<p>@Twice(2)</p>\n@Functions\n    Function Twice(x As Integer) As Integer\n" +
                   statement + "\n    End Function\nEnd Functions\n";
        documents.Update(documentUri.ToString(), text, 1);

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(documentUri),
            Position = new Position(3, statement.Length),
            Context = new CompletionContext
            {
                TriggerKind = CompletionTriggerKind.TriggerCharacter,
                TriggerCharacter = typed
            }
        }, default);

        Assert.Contains(items, item => item.Label.EndsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFieldInitialiserLastInFunctionsIsAskedOfVisualBasic()
    {
        // The block's last line, so the caret after the space sits past the
        // trimmed body, as on a code block's last line.
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var documents = new DocumentStore();
        var documentUri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Field.vbhtml"));
        const string statement = "    Private ReadOnly _options As System.Text.Json.JsonSerializerOptions = New ";
        documents.Update(documentUri.ToString(), "<p>Hi</p>\n@Functions\n" + statement + "\nEnd Functions\n", 1);

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(documentUri),
            Position = new Position(2, statement.Length),
            Context = new CompletionContext
            {
                TriggerKind = CompletionTriggerKind.TriggerCharacter,
                TriggerCharacter = " "
            }
        }, default);

        var preselected = Assert.Single(items, item => item.Preselect);
        Assert.Equal("Json.JsonSerializerOptions", preselected.InsertText);
    }

    [Fact]
    public void AFunctionsBodyIsCodeFromItsFirstCharacterToItsLast()
    {
        const string text = "<p>Hi</p>\n@Functions\n    Function One() As Integer\n        Return 1\n    End Function\nEnd Functions\n<p>Bye</p>";
        var first = text.IndexOf("Function One", StringComparison.Ordinal);
        var last = text.IndexOf("End Function\n", StringComparison.Ordinal) + "End Function".Length;

        Assert.False(VbHtmlCodeRegions.IsInCode(text, first));
        Assert.True(VbHtmlCodeRegions.IsInCode(text, first + 1));
        Assert.True(VbHtmlCodeRegions.IsInCode(text, last));
        Assert.False(VbHtmlCodeRegions.IsInCode(text, last + 1));
        Assert.True(VbHtmlCodeRegions.IsAfterStatementBody(text, last + 1));
        Assert.False(VbHtmlCodeRegions.IsInCode(text, text.IndexOf("Bye", StringComparison.Ordinal)));
        Assert.False(VbHtmlCodeRegions.IsAfterStatementBody(text, text.IndexOf("Bye", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ASpaceInCodeIsNotTakenForMarkupWhileTheProjectLoads()
    {
        // Without a compilation the markup reading answers; it took "a < b "
        // for a tag and offered HTML attributes in the middle of Visual Basic.
        using var compilation = new ProjectCompilation();
        var documents = new DocumentStore();
        var documentUri = DocumentUri.FromFileSystemPath(Path.Combine(_root, "Loading.vbhtml"));
        const string statement = "    Dim smaller = a < b ";
        documents.Update(documentUri.ToString(), "@Code\n" + statement + "\nEnd Code\n", 1);

        var items = await new VbHtmlCompletionHandler(documents, compilation).Handle(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(documentUri),
            Position = new Position(1, statement.Length),
            Context = new CompletionContext
            {
                TriggerKind = CompletionTriggerKind.TriggerCharacter,
                TriggerCharacter = " "
            }
        }, default);

        Assert.Empty(items);
    }

    [Fact]
    public void OnlyCompletionCountsTheSpacesAfterAStatementAsCode()
    {
        // Hover and navigation have nothing to say about blank space, and
        // counting it as code made them answer about the next expression.
        const string text = "@Code\n    Dim x = New \n\nEnd Code\n<p>@x</p>";
        var afterSpace = text.IndexOf("New ", StringComparison.Ordinal) + "New ".Length;

        Assert.False(VbHtmlCodeRegions.IsInCode(text, afterSpace));
        Assert.True(VbHtmlCodeRegions.IsAfterStatementBody(text, afterSpace));
        Assert.True(VbHtmlCodeRegions.IsAfterStatementBody(text, afterSpace + 1));
        Assert.False(VbHtmlCodeRegions.IsAfterStatementBody(text, text.IndexOf("New", StringComparison.Ordinal)));
        Assert.False(VbHtmlCodeRegions.IsAfterStatementBody(text, text.IndexOf("@x", StringComparison.Ordinal) + 2));
    }

    [Theory]
    [InlineData("    Dim x = New |", " ")]
    [InlineData("    Dim x = New \t|", " \t")]
    [InlineData("    |", "")]
    [InlineData("|", "")]
    [InlineData("    Dim x = New|", "")]
    public void TheSpacesBeforeTheCaretAreThoseAfterAWord(string template, string expected)
    {
        var position = template.IndexOf('|');

        Assert.Equal(expected, VbHtmlCompletionProvider.SpacesBeforeCaret(template.Replace("|", ""), position));
    }

    [Theory]
    [InlineData("Dim x = New^\n", " ", "Dim x = New ^\n")]
    [InlineData("Dim x As ^Integer\n", "", "Dim x As ^Integer\n")]
    [InlineData("Dim x As ^Integer\n", " ", "Dim x As ^Integer\n")]
    public void DroppedSpacesAreGivenBackAndKeptOnesLeftAlone(string code, string spaces, string expected)
    {
        var at = code.IndexOf('^');

        var (patched, caret) = VbHtmlCompletionProvider.WithSpacesBefore(code.Replace("^", ""), at, spaces);

        Assert.Equal(expected, patched.Insert(caret, "^"));
    }
}
