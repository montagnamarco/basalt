using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// Renaming a name a view declares, through the language server, and
/// refusing the renames it cannot yet make safely.
/// </summary>
/// <remarks>
/// The server had no rename at all. In VS Code, Visual Studio and Rider,
/// renaming a model property from a view that uses it did nothing, and the
/// other views kept the old name until the build said so.
/// </remarks>
public sealed class LanguageServerRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-lsp-rename-" + Guid.NewGuid().ToString("N"));

    private const string Model = """
        Public Module Program
            Public Sub Main()
            End Sub
        End Module

        Public Class Customer
            Public Property Name As String
        End Class
        """;

    private const string OtherView = "@ModelType Global.Site.Customer\n<h1>@Model.Name</h1>\n";

    public LanguageServerRenameTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Views"));
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), Model);
        File.WriteAllText(Path.Combine(_root, "Views", "Other.vbhtml"), OtherView);
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    private async Task<(ProjectCompilation Compilation, DocumentStore Documents, DocumentUri Uri)> OpenAsync(
        string name, string onDisk, string inEditor)
    {
        var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);

        var path = Path.Combine(_root, "Views", name);
        await File.WriteAllTextAsync(path, onDisk);

        var documents = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(path);
        documents.Update(uri.ToString(), inEditor, 1);

        return (compilation, documents, uri);
    }

    private static Position PositionOf(string text, string before)
    {
        var offset = text.IndexOf(before, StringComparison.Ordinal) + before.Length;
        var (line, character) = VbHtmlSemanticTokensHandler.LineAndCharacterOf(text, offset);

        return new Position(line, character);
    }

    private static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        foreach (var edit in edits.OrderByDescending(edit => edit.Range.Start.Line).ThenByDescending(edit => edit.Range.Start.Character))
        {
            var start = VbHtmlCompletionHandler.OffsetOf(text, edit.Range.Start);
            var end = VbHtmlCompletionHandler.OffsetOf(text, edit.Range.End);

            text = text[..start] + edit.NewText + text[end..];
        }

        return text;
    }
    private static async Task<WorkspaceEdit?> RenameAsync(
        ProjectCompilation compilation, DocumentStore documents, DocumentUri uri, Position position, string newName) =>
        await new VbHtmlRenameHandler(documents, compilation).Handle(new RenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = position,
            NewName = newName
        }, default);

    private static async Task<RangeOrPlaceholderRange?> PrepareAsync(
        ProjectCompilation compilation, DocumentStore documents, DocumentUri uri, Position position) =>
        await new VbHtmlPrepareRenameHandler(documents, compilation).Handle(new PrepareRenameParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = position
        }, default);

    /// <summary>The one document edit, which must be versioned for the text it was made from.</summary>
    private static TextDocumentEdit OnlyEdit(WorkspaceEdit? edit, DocumentUri uri)
    {
        Assert.NotNull(edit?.DocumentChanges);

        var change = Assert.Single(edit.DocumentChanges!).TextDocumentEdit!;

        Assert.Equal(uri, change.TextDocument.Uri);
        Assert.Equal(1, change.TextDocument.Version);

        return change;
    }

    [Theory]
    [InlineData("Dim to")]
    [InlineData("<p>@to")]
    public async Task AVariableOfACodeBlockIsRenamedWhereverTheViewUsesIt(string caretAfter)
    {
        // Visual Basic reads "TOTAL" and "total" as one name, so both go.
        const string view = "@Code\n    Dim total = 1\n    TOTAL = total + 1\nEnd Code\n<p>@total</p>\n";

        var (compilation, documents, uri) = await OpenAsync("Local.vbhtml", view, view);

        using (compilation)
        {
            var edit = OnlyEdit(await RenameAsync(compilation, documents, uri, PositionOf(view, caretAfter), "sum"), uri);

            Assert.Equal("@Code\n    Dim sum = 1\n    sum = sum + 1\nEnd Code\n<p>@sum</p>\n", Apply(view, edit.Edits));
        }
    }

    [Fact]
    public async Task AMethodOfFunctionsIsRenamedWithItsCalls()
    {
        const string view = "<p>@Twice(2)</p>\n@Functions\n    Function Twice(n As Integer) As Integer\n" +
                            "        Return n * 2\n    End Function\nEnd Functions\n";

        var (compilation, documents, uri) = await OpenAsync("Functions.vbhtml", view, view);

        using (compilation)
        {
            var edit = OnlyEdit(await RenameAsync(compilation, documents, uri, PositionOf(view, "Function Tw"), "Doubled"), uri);

            Assert.Equal(view.Replace("Twice", "Doubled", StringComparison.Ordinal), Apply(view, edit.Edits));
        }
    }

    [Theory]
    [InlineData("End")]
    [InlineData("2nd")]
    [InlineData("two words")]
    public async Task ANewNameVisualBasicCannotTakeIsRefused(string newName)
    {
        const string view = "@Code\n    Dim total = 1\nEnd Code\n<p>@total</p>\n";

        var (compilation, documents, uri) = await OpenAsync("Keyword.vbhtml", view, view);

        using (compilation)
            Assert.Null(await RenameAsync(compilation, documents, uri, PositionOf(view, "Dim to"), newName));
    }

    [Theory]
    [InlineData("Code")]
    [InlineData("other")]
    [InlineData("_")]
    public async Task ANameThatWouldChangeWhatTheViewMeansIsRefused(string newName)
    {
        // "@Code" in markup opens a code block; "other" is already a local.
        const string view = "@Code\n    Dim total = 1\n    Dim other = 2\nEnd Code\n<p>@total</p>\n";

        var (compilation, documents, uri) = await OpenAsync("Meaning.vbhtml", view, view);

        using (compilation)
            Assert.Null(await RenameAsync(compilation, documents, uri, PositionOf(view, "Dim to"), newName));
    }

    [Fact]
    public async Task AMethodRenamedOverTheViewsOwnWriteIsRefused()
    {
        // Visual Basic lets it shadow the base class's Write with a warning,
        // and every Write(...) the generated view makes would then call it.
        const string view = "<p>@Twice(2)</p>\n@Functions\n    Function Twice(n As Integer) As Integer\n" +
                            "        Return n * 2\n    End Function\nEnd Functions\n";

        var (compilation, documents, uri) = await OpenAsync("Shadow.vbhtml", view, view);

        using (compilation)
            Assert.Null(await RenameAsync(compilation, documents, uri, PositionOf(view, "Function Tw"), "Write"));
    }

    [Fact]
    public async Task AWarningAlreadyThereDoesNotStopTheRename()
    {
        // "unused" is declared and never read: a warning before and after.
        const string view = "@Code\n    Dim total = 1\n    Dim unused As Integer\nEnd Code\n<p>@total</p>\n";

        var (compilation, documents, uri) = await OpenAsync("Warned.vbhtml", view, view);

        using (compilation)
        {
            var edit = OnlyEdit(await RenameAsync(compilation, documents, uri, PositionOf(view, "Dim to"), "sum"), uri);

            Assert.Equal(view.Replace("total", "sum", StringComparison.Ordinal), Apply(view, edit.Edits));
        }
    }

    [Fact]
    public async Task AViewTheParserFindsAProblemInIsNotRenamed()
    {
        // Not compiled while it has one, so nothing to check the rename against.
        const string view = "@Code\n    Dim total = 1\nEnd Code\n<p>@total</p>\n@If total = 1 Then\n<b>one</b>\n";

        var (compilation, documents, uri) = await OpenAsync("Unclosed.vbhtml", view, view);

        using (compilation)
            Assert.Null(await RenameAsync(compilation, documents, uri, PositionOf(view, "Dim to"), "sum"));
    }

    [Fact]
    public async Task AContextualKeywordIsAnOrdinaryName()
    {
        const string view = "@Code\n    Dim total = 1\nEnd Code\n<p>@total</p>\n";

        var (compilation, documents, uri) = await OpenAsync("Contextual.vbhtml", view, view);

        using (compilation)
        {
            var edit = OnlyEdit(await RenameAsync(compilation, documents, uri, PositionOf(view, "Dim to"), "Text"), uri);

            Assert.Equal(view.Replace("total", "Text", StringComparison.Ordinal), Apply(view, edit.Edits));
        }
    }

    [Fact]
    public async Task ANameDeclaredOutsideTheViewIsRefusedBeforeAnythingIsTyped()
    {
        // A model property is renamed in other files too, and the server
        // cannot see what an editor holds unsaved for them: refused, rather
        // than an edit computed from disk landing on a buffer that differs.
        const string view = "@ModelType Global.Site.Customer\n<p>@Model.Name</p>\n@Code\n    Dim now = DateTime.Now\nEnd Code\n";

        var (compilation, documents, uri) = await OpenAsync("Index.vbhtml", view, view);

        using (compilation)
        {
            Assert.Null(await PrepareAsync(compilation, documents, uri, PositionOf(view, "@Model.Na")));
            Assert.Null(await RenameAsync(compilation, documents, uri, PositionOf(view, "@Model.Na"), "FullName"));
            Assert.Null(await PrepareAsync(compilation, documents, uri, PositionOf(view, "DateTi")));

            var offered = await PrepareAsync(compilation, documents, uri, PositionOf(view, "Dim no"));

            Assert.Equal("now", offered?.PlaceholderRange?.Placeholder);
        }
    }

    [Fact]
    public async Task AComponentIsNotRenamedFromInside()
    {
        // What a component declares, other components use: a rename here
        // would leave their parameters behind.
        const string component = "<p>@count</p>\n@Code\n    Private count As Integer\nEnd Code\n";

        var (compilation, documents, uri) = await OpenAsync("Counter.vbrazor", component, component);

        using (compilation)
            Assert.Null(await PrepareAsync(compilation, documents, uri, PositionOf(component, "Private co")));
    }
}
