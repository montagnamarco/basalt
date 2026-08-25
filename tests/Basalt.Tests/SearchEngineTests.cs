using Basalt.Workspace.Search;

namespace Basalt.Tests;

/// <summary>Finding and replacing, in a document and across a directory.</summary>
public sealed class SearchEngineTests : IDisposable
{
    private readonly SearchEngine _engine = new();

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-search", Guid.NewGuid().ToString("N"));

    public SearchEngineTests() => Directory.CreateDirectory(_root);

    private const string Sample = """
        Public Class Customer
            Public Property Name As String

            Public Sub Save()
                Dim name As String = Name
            End Sub
        End Class
        """;

    [Fact]
    public void FindsEveryOccurrence()
    {
        var hits = _engine.FindInText(Sample, "Public", new SearchOptions());

        // Class, Property and Sub: three declarations.
        Assert.Equal(3, hits.Count);
    }

    [Fact]
    public void ReportsLineAndColumnStartingAtOne()
    {
        var hits = _engine.FindInText(Sample, "Class Customer", new SearchOptions());

        var hit = Assert.Single(hits);
        Assert.Equal(1, hit.Line);
        Assert.Equal(8, hit.Column);
    }

    [Fact]
    public void CarriesTheWholeLineForPreview()
    {
        var hits = _engine.FindInText(Sample, "Save", new SearchOptions());

        var hit = Assert.Single(hits);
        // The raw string literal strips the common indentation, so the line
        // carries the four spaces of the declaration, not eight.
        Assert.Equal("    Public Sub Save()", hit.LineText);
    }

    [Fact]
    public void IgnoresCaseByDefault()
    {
        // Visual Basic is case-insensitive, so this is the behaviour that fits.
        var hits = _engine.FindInText(Sample, "CUSTOMER", new SearchOptions());

        Assert.Single(hits);
    }

    [Fact]
    public void MatchesCaseWhenAsked()
    {
        var hits = _engine.FindInText(Sample, "CUSTOMER", new SearchOptions { MatchCase = true });

        Assert.Empty(hits);
    }

    [Fact]
    public void DistinguishesNameFromNameWhenCaseMatters()
    {
        // "name" the local and "Name" the property, told apart only by case.
        var lower = _engine.FindInText(Sample, "name", new SearchOptions { MatchCase = true });

        Assert.Single(lower);
    }

    [Fact]
    public void MatchesWholeWordsOnlyWhenAsked()
    {
        const string text = "Name NameLength SurName Name";

        var all = _engine.FindInText(text, "Name", new SearchOptions());
        var whole = _engine.FindInText(text, "Name", new SearchOptions { WholeWord = true });

        Assert.Equal(4, all.Count);
        Assert.Equal(2, whole.Count);
    }

    [Fact]
    public void SearchesWithRegularExpressions()
    {
        var hits = _engine.FindInText(
            Sample, @"Public\s+(Sub|Property)", new SearchOptions { UseRegex = true });

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void TreatsRegexCharactersLiterallyWhenRegexIsOff()
    {
        const string text = "cost is 3.50 or 3x50";

        var hits = _engine.FindInText(text, "3.50", new SearchOptions());

        // With regex off the dot matches only a dot, not any character.
        Assert.Single(hits);
    }

    [Fact]
    public void ReturnsNothingForAHalfTypedRegularExpression()
    {
        // The user is mid-typing, not making an error worth reporting.
        var hits = _engine.FindInText(Sample, "Public(", new SearchOptions { UseRegex = true });

        Assert.Empty(hits);
    }

    [Fact]
    public void ReplacesEveryOccurrence()
    {
        var result = _engine.ReplaceInText(Sample, "Public", "Friend", new SearchOptions());

        Assert.DoesNotContain("Public", result);
        Assert.Equal(3, _engine.FindInText(result, "Friend", new SearchOptions()).Count);
    }

    [Fact]
    public void ReplacesASingleOccurrenceLeavingTheOthers()
    {
        var hits = _engine.FindInText(Sample, "Public", new SearchOptions());

        var result = _engine.ReplaceHit(Sample, hits[1], "Friend");

        Assert.Contains("Friend Property Name", result);
        Assert.Contains("Public Class Customer", result);
    }

    [Fact]
    public void KeepsADollarSignLiteralWhenRegexIsOff()
    {
        // Without regex, "$1" in the replacement is text, not a group.
        var result = _engine.ReplaceInText("price", "price", "$1 each", new SearchOptions());

        Assert.Equal("$1 each", result);
    }

    [Fact]
    public void UsesGroupReferencesWhenRegexIsOn()
    {
        var result = _engine.ReplaceInText(
            "Dim x As Integer", @"Dim (\w+)", "Private $1", new SearchOptions { UseRegex = true });

        Assert.Equal("Private x As Integer", result);
    }

    [Fact]
    public async Task SearchesEveryFileUnderADirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "A.vb"), "Public Class A\nEnd Class");
        Directory.CreateDirectory(Path.Combine(_root, "Sub"));
        await File.WriteAllTextAsync(Path.Combine(_root, "Sub", "B.vb"), "Public Class B\nEnd Class");

        var results = new List<FileHits>();
        await foreach (var file in _engine.SearchDirectoryAsync(_root, "Public", new SearchOptions()))
            results.Add(file);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SkipsBuildOutputDirectories()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "A.vb"), "Public Class A");
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        await File.WriteAllTextAsync(Path.Combine(_root, "obj", "Generated.vb"), "Public Class G");

        var results = new List<FileHits>();
        await foreach (var file in _engine.SearchDirectoryAsync(_root, "Public", new SearchOptions()))
            results.Add(file);

        Assert.Single(results);
        Assert.EndsWith("A.vb", results[0].FilePath);
    }

    [Fact]
    public async Task LimitsTheSearchToChosenExtensions()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Code.vb"), "target");
        await File.WriteAllTextAsync(Path.Combine(_root, "Notes.txt"), "target");

        var options = new SearchOptions { IncludeExtensions = [".vb"] };

        var results = new List<FileHits>();
        await foreach (var file in _engine.SearchDirectoryAsync(_root, "target", options))
            results.Add(file);

        Assert.Single(results);
        Assert.EndsWith("Code.vb", results[0].FilePath);
    }

    [Fact]
    public async Task ReplacesAcrossADirectoryAndReportsHowManyFilesChanged()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "A.vb"), "Public Class A");
        await File.WriteAllTextAsync(Path.Combine(_root, "B.vb"), "Public Class B");
        await File.WriteAllTextAsync(Path.Combine(_root, "C.vb"), "Friend Class C");

        var changed = await _engine.ReplaceInDirectoryAsync(
            _root, "Public", "Friend", new SearchOptions());

        Assert.Equal(2, changed);
        Assert.Equal("Friend Class A", await File.ReadAllTextAsync(Path.Combine(_root, "A.vb")));
    }

    [Fact]
    public async Task SkipsBinaryFiles()
    {
        // A null byte marks content whose "hits" would be meaningless.
        await File.WriteAllTextAsync(Path.Combine(_root, "Data.bin"), "target\0\0\0target");
        await File.WriteAllTextAsync(Path.Combine(_root, "Code.vb"), "target");

        var results = new List<FileHits>();
        await foreach (var file in _engine.SearchDirectoryAsync(_root, "target", new SearchOptions()))
            results.Add(file);

        Assert.Single(results);
        Assert.EndsWith("Code.vb", results[0].FilePath);
    }

    [Fact]
    public void FindsNothingForAnEmptyTerm()
    {
        Assert.Empty(_engine.FindInText(Sample, "", new SearchOptions()));
    }

    [Fact]
    public void IgnoresAZeroWidthRegularExpression()
    {
        // A pattern matching nothing at every position would otherwise produce
        // one hit per character.
        var hits = _engine.FindInText(Sample, "x*", new SearchOptions { UseRegex = true });

        Assert.Empty(hits);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
