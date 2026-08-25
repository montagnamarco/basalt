using Basalt.Extensibility;
using Basalt.QuickBasic;
using Basalt.Workspace.Languages;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>Reading QuickBASIC into tokens.</summary>
public class QuickBasicLexerTests
{
    private static IReadOnlyList<Token> Tokens(string source) =>
        [.. new Lexer(source).Tokenize().Where(t => t.Kind != TokenKind.EndOfFile)];

    [Fact]
    public void TellsKeywordsFromNames()
    {
        var tokens = Tokens("PRINT total");

        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
    }

    [Fact]
    public void ReadsKeywordsWhateverTheirCase()
    {
        // QuickBASIC is case-insensitive, and users write it both ways.
        Assert.Equal(TokenKind.Keyword, Tokens("print")[0].Kind);
        Assert.Equal(TokenKind.Keyword, Tokens("Print")[0].Kind);
    }

    [Fact]
    public void KeepsTheTypeSuffixWithTheName()
    {
        // "n%" and "n$" are different variables, so the suffix is part of the
        // name rather than a separate token.
        Assert.Equal("n%", Tokens("n% = 1")[0].Text);
        Assert.Equal("name$", Tokens("name$ = \"a\"")[0].Text);
    }

    [Fact]
    public void ReadsAStringLiteral()
    {
        var token = Tokens("\"hello\"")[0];

        Assert.Equal(TokenKind.String, token.Kind);
        Assert.Equal("\"hello\"", token.Text);
    }

    [Fact]
    public void EndsAnUnterminatedStringAtTheLine()
    {
        // Otherwise one missing quote swallows the rest of the program.
        var tokens = Tokens("PRINT \"oops\nPRINT 1");

        Assert.Contains(tokens, t => t.Kind == TokenKind.EndOfLine);
    }

    [Fact]
    public void SkipsAComment()
    {
        var tokens = Tokens("PRINT 1 ' this is a comment");

        Assert.DoesNotContain(tokens, t => t.Text.Contains("comment"));
    }

    [Fact]
    public void SkipsARemComment()
    {
        Assert.DoesNotContain(Tokens("REM nothing here"), t => t.Kind == TokenKind.Identifier);
    }

    [Fact]
    public void KeepsTheLineEndingAfterAComment()
    {
        // The parser needs it: a comment ends the text, not the statement.
        Assert.Contains(Tokens("PRINT 1 ' note\nPRINT 2"), t => t.Kind == TokenKind.EndOfLine);
    }

    [Fact]
    public void ReadsTwoCharacterOperatorsWhole()
    {
        Assert.Equal("<=", Tokens("a <= b")[1].Text);
        Assert.Equal("<>", Tokens("a <> b")[1].Text);
    }

    [Fact]
    public void CountsLinesAndColumns()
    {
        var tokens = Tokens("PRINT 1\nPRINT 2");

        var second = tokens.Last(t => t.Kind == TokenKind.Keyword);

        Assert.Equal(2, second.Line);
        Assert.Equal(1, second.Column);
    }
}

/// <summary>Building a tree from QuickBASIC.</summary>
public class QuickBasicParserTests
{
    [Fact]
    public void ReadsAnAssignment()
    {
        var program = Parser.Parse("total = 1 + 2");

        var assignment = Assert.IsType<Assignment>(program.Main[0]);
        Assert.IsType<Binary>(assignment.Value);
    }

    [Fact]
    public void ReadsAForLoop()
    {
        var program = Parser.Parse("FOR i = 1 TO 10\n  PRINT i\nNEXT i");

        var loop = Assert.IsType<ForStatement>(program.Main[0]);

        Assert.Equal("i", loop.Variable);
        Assert.Single(loop.Body);
    }

    [Fact]
    public void ReadsAStepOnALoop()
    {
        var loop = Assert.IsType<ForStatement>(
            Parser.Parse("FOR i = 10 TO 1 STEP -1\nNEXT").Main[0]);

        Assert.NotNull(loop.Step);
    }

    [Fact]
    public void ReadsABlockIfWithItsBranches()
    {
        const string source = """
            IF a = 1 THEN
                PRINT "one"
            ELSEIF a = 2 THEN
                PRINT "two"
            ELSE
                PRINT "other"
            END IF
            """;

        var statement = Assert.IsType<IfStatement>(Parser.Parse(source).Main[0]);

        Assert.Single(statement.Then);
        Assert.Single(statement.ElseIfs);
        Assert.NotNull(statement.Else);
    }

    [Fact]
    public void ReadsASingleLineIf()
    {
        var statement = Assert.IsType<IfStatement>(
            Parser.Parse("IF a = 1 THEN PRINT \"yes\"").Main[0]);

        Assert.Single(statement.Then);
    }

    [Fact]
    public void ReadsAFunctionWithItsParameters()
    {
        const string source = """
            FUNCTION Add%(a%, b%)
                Add% = a% + b%
            END FUNCTION
            """;

        var procedure = Assert.Single(Parser.Parse(source).Procedures);

        Assert.True(procedure.IsFunction);
        Assert.Equal(2, procedure.Parameters.Count);
        Assert.Equal(BasicType.Integer, procedure.ReturnType);
    }

    [Fact]
    public void ReadsASubAsReturningNothing()
    {
        var procedure = Assert.Single(
            Parser.Parse("SUB Greet\n  PRINT 1\nEND SUB").Procedures);

        Assert.False(procedure.IsFunction);
        Assert.Equal(BasicType.Void, procedure.ReturnType);
    }

    [Fact]
    public void ReadsAnArrayDeclaration()
    {
        var declaration = Assert.IsType<Declaration>(
            Parser.Parse("DIM numbers(10) AS INTEGER").Main[0]);

        Assert.NotNull(declaration.Bounds);
        Assert.Equal(BasicType.Integer, declaration.Type);
    }

    [Fact]
    public void GivesMultiplicationPrecedenceOverAddition()
    {
        var assignment = Assert.IsType<Assignment>(Parser.Parse("a = 1 + 2 * 3").Main[0]);

        // The tree, not the text, decides: the addition is on top.
        var binary = Assert.IsType<Binary>(assignment.Value);
        Assert.Equal("+", binary.Operator);
    }

    [Fact]
    public void MakesPowerRightAssociative()
    {
        var assignment = Assert.IsType<Assignment>(Parser.Parse("a = 2 ^ 3 ^ 2").Main[0]);

        var binary = Assert.IsType<Binary>(assignment.Value);

        Assert.Equal("^", binary.Operator);
        Assert.IsType<Binary>(binary.Right);
    }

    [Fact]
    public void ReportsAMissingEnd()
    {
        var program = Parser.Parse("SUB Greet\n  PRINT 1");

        Assert.Contains(program.Diagnostics, d => d.Message.Contains("Missing END"));
    }

    [Fact]
    public void KeepsGoingAfterABadStatement()
    {
        // An editor asks for a tree while the program is half-written.
        var program = Parser.Parse("THEN\nPRINT 1");

        Assert.NotEmpty(program.Diagnostics);
        Assert.Contains(program.Main, s => s is PrintStatement);
    }

    [Fact]
    public void ReadsSeveralStatementsSeparatedByColons()
    {
        var program = Parser.Parse("a = 1 : b = 2");

        Assert.Equal(2, program.Main.Count);
    }

    [Fact]
    public void ReadsALabel()
    {
        var program = Parser.Parse("Start:\nGOTO Start");

        Assert.IsType<LabelStatement>(program.Main[0]);
        Assert.IsType<GotoStatement>(program.Main[1]);
    }
}

/// <summary>Checking a QuickBASIC program.</summary>
public class QuickBasicSemanticTests
{
    private static IReadOnlyList<Basalt.QuickBasic.Diagnostic> Check(string source)
    {
        var program = Parser.Parse(source);

        return [.. program.Diagnostics, .. SymbolTable.Build(program).Diagnostics];
    }

    [Fact]
    public void AcceptsAProgramThatIsRight()
    {
        Assert.Empty(Check("DIM n AS INTEGER\nn = 1\nPRINT n"));
    }

    [Fact]
    public void ReportsACallToSomethingUndefined()
    {
        Assert.Contains(Check("CALL Missing"), d => d.Message.Contains("not defined"));
    }

    [Fact]
    public void ReportsTheWrongNumberOfArguments()
    {
        const string source = """
            CALL Greet(1, 2)

            SUB Greet(a%)
            END SUB
            """;

        Assert.Contains(Check(source), d => d.Message.Contains("takes 1 argument"));
    }

    [Fact]
    public void ReportsAStringAssignedToANumber()
    {
        // The one type error QuickBASIC itself refuses to compile.
        Assert.Contains(
            Check("DIM n AS INTEGER\nn = \"text\""),
            d => d.Message.Contains("Cannot assign a string"));
    }

    [Fact]
    public void ReportsANumberAssignedToAString()
    {
        Assert.Contains(
            Check("DIM s AS STRING\ns = 42"),
            d => d.Message.Contains("Cannot assign a number"));
    }

    [Fact]
    public void ReportsMixingTextAndNumbers()
    {
        Assert.Contains(
            Check("DIM s AS STRING\ns = \"a\"\nPRINT s - 1"),
            d => d.Message.Contains("string and a number"));
    }

    [Fact]
    public void ReportsAProcedureDeclaredTwice()
    {
        const string source = """
            SUB Greet
            END SUB

            SUB Greet
            END SUB
            """;

        Assert.Contains(Check(source), d => d.Message.Contains("more than once"));
    }

    [Fact]
    public void AllowsAVariableUsedBeforeItIsAssigned()
    {
        // QuickBASIC declares by use: an unassigned number is zero.
        Assert.Empty(Check("PRINT n"));
    }

    [Fact]
    public void KnowsAnArrayFromAFunctionCall()
    {
        // Both are written with parentheses; only the DIM tells them apart.
        Assert.Empty(Check("DIM a(10)\na(1) = 5\nPRINT a(1)"));
    }
}

/// <summary>QuickBASIC reaching the IDE through the language contracts.</summary>
public class QuickBasicProviderTests
{
    private readonly QuickBasicLanguageProvider _provider = new();

    [Fact]
    public void ClaimsTheBasicExtensions()
    {
        Assert.True(_provider.Identity.Matches("/a/game.bas"));
        Assert.True(_provider.Identity.Matches("/a/game.qb"));
    }

    [Fact]
    public void IsNotCaseSensitive()
    {
        Assert.False(_provider.Identity.IsCaseSensitive);
    }

    [Fact]
    public async Task OffersItsKeywords()
    {
        var items = await _provider.Completion!.GetCompletionsAsync(
            new LanguageDocument("/a/game.bas", "PRI"), 3);

        Assert.Contains(items, i => i.DisplayText == "PRINT");
    }

    [Fact]
    public async Task OffersWhatTheProgramItselfDeclares()
    {
        const string source = """
            SUB Greet
            END SUB
            Gr
            """;

        var items = await _provider.Completion!.GetCompletionsAsync(
            new LanguageDocument("/a/game.bas", source), source.IndexOf("Gr") + 2);

        Assert.Contains(items, i => i.DisplayText == "Greet");
    }

    [Fact]
    public async Task ReportsProblemsThroughTheContract()
    {
        var diagnostics = await _provider.Diagnostics!.GetDiagnosticsAsync(
            new LanguageDocument("/a/game.bas", "CALL Missing"));

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.Equal("/a/game.bas", d.FilePath));
    }

    [Fact]
    public async Task ListsTheProceduresForTheOutline()
    {
        const string source = """
            SUB Greet
            END SUB

            FUNCTION Add%(a%)
            END FUNCTION
            """;

        var symbols = await _provider.Navigation!.GetDocumentSymbolsAsync(
            new LanguageDocument("/a/game.bas", source));

        Assert.Equal(2, symbols.Count);
    }

    [Fact]
    public async Task JumpsToAProcedureDefinition()
    {
        const string source = """
            CALL Greet

            SUB Greet
            END SUB
            """;

        var location = await _provider.Navigation!.GoToDefinitionAsync(
            new LanguageDocument("/a/game.bas", source), source.IndexOf("Greet") + 2);

        Assert.NotNull(location);
        Assert.Equal(3, location!.Range.Start.Line);
    }

    [Fact]
    public void UsesTheEditorsExistingColouring()
    {
        // Its keywords and strings are Visual Basic's; a grammar saying so
        // again would be work for nothing.
        Assert.Equal("VB", _provider.Highlighting!.BuiltInDefinitionName);
    }

    [Fact]
    public void OffersEveryTargetItCanBuildFor()
    {
        var targets = _provider.Compiler!.SupportedTargets;

        Assert.Contains("osx-arm64", targets);
        Assert.Contains("linux-x64", targets);
        Assert.Contains("win-x64", targets);
    }

    [Theory]
    [InlineData("osx-arm64", "arm64", "macos")]
    [InlineData("osx-x64", "x86_64", "macos")]
    [InlineData("linux-arm64", "arm64", "linux")]
    [InlineData("win-x64", "x86_64", "windows")]
    public void ReadsARuntimeIdentifier(string identifier, string architecture, string operatingSystem)
    {
        // .NET writes x64 where clang writes x86_64.
        var (readArchitecture, readOperatingSystem) =
            QuickBasicCompilerBackend.Split(identifier);

        Assert.Equal(architecture, readArchitecture);
        Assert.Equal(operatingSystem, readOperatingSystem);
    }

    [Fact]
    public void IsRegisteredWithTheIde()
    {
        // The proof that the architecture holds: a language from its own
        // project, reached the same way as the built-in ones.
        using var roslyn = new RoslynLanguageService();
        var registry = LanguageCatalog.CreateDefault(roslyn, new RoslynFormattingService());

        Assert.NotNull(registry.ForFile("/a/game.bas"));
    }
}
