namespace Basalt.QuickBasic;

/// <summary>
/// Builds a tree from QuickBASIC tokens.
///
/// Recursive descent, and deliberately forgiving: an editor asks for a tree
/// while the program is half-written, so a statement that cannot be read is
/// reported and skipped rather than abandoning the file. What comes out is
/// always a program, even when the diagnostics say it is a broken one.
/// </summary>
public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics = [];
    private int _index;

    private Parser(IReadOnlyList<Token> tokens) => _tokens = tokens;

    public static Program Parse(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        return parser.ParseProgram();
    }

    private Token Current => _tokens[Math.Min(_index, _tokens.Count - 1)];

    private Token Ahead(int distance = 1) =>
        _tokens[Math.Min(_index + distance, _tokens.Count - 1)];

    private bool AtEnd => Current.Kind == TokenKind.EndOfFile;

    private Token Take() => _tokens[Math.Min(_index++, _tokens.Count - 1)];

    private Program ParseProgram()
    {
        var main = new List<Statement>();
        var procedures = new List<Procedure>();

        while (!AtEnd)
        {
            SkipBlankLines();
            if (AtEnd) break;

            if (Current.IsKeyword("SUB") || Current.IsKeyword("FUNCTION"))
            {
                if (ParseProcedure() is { } procedure) procedures.Add(procedure);
                continue;
            }

            // DECLARE announces a procedure defined later; the definition is
            // what matters, so the announcement is read and dropped.
            if (Current.IsKeyword("DECLARE"))
            {
                SkipToEndOfLine();
                continue;
            }

            if (ParseStatement() is { } statement) main.Add(statement);
        }

        return new Program(main, procedures) { Diagnostics = _diagnostics };
    }

    private Procedure? ParseProcedure()
    {
        var keyword = Take();
        var isFunction = keyword.IsKeyword("FUNCTION");

        if (Current.Kind != TokenKind.Identifier)
        {
            Report("QB001", "Expected a name after " + keyword.Text.ToUpperInvariant());
            SkipToEndOfLine();
            return null;
        }

        var name = Take();
        var parameters = new List<Parameter>();

        if (Current.Kind == TokenKind.OpenParen)
        {
            Take();

            while (!AtEnd && Current.Kind != TokenKind.CloseParen)
            {
                if (Current.Kind != TokenKind.Identifier)
                {
                    Report("QB002", "Expected a parameter name.");
                    break;
                }

                var parameterName = Take().Text;
                var isArray = false;

                // "name()" marks a parameter passed as an array.
                if (Current.Kind == TokenKind.OpenParen)
                {
                    Take();
                    if (Current.Kind == TokenKind.CloseParen) Take();
                    isArray = true;
                }

                var type = TypeSuffix.Of(parameterName);

                if (Current.IsKeyword("AS"))
                {
                    Take();
                    if (Current.Kind == TokenKind.Keyword) type = TypeSuffix.FromKeyword(Take().Text);
                }

                parameters.Add(new Parameter(parameterName, type, isArray));

                if (Current.Kind == TokenKind.Comma) Take();
            }

            if (Current.Kind == TokenKind.CloseParen) Take();
        }

        var returnType = isFunction ? TypeSuffix.Of(name.Text) : BasicType.Void;

        if (isFunction && Current.IsKeyword("AS"))
        {
            Take();
            if (Current.Kind == TokenKind.Keyword) returnType = TypeSuffix.FromKeyword(Take().Text);
        }

        var body = ParseBlock(end: isFunction ? "FUNCTION" : "SUB");

        return new Procedure(
            name.Text, parameters, returnType, body, keyword.Line, keyword.Column);
    }

    /// <summary>
    /// Reads statements until the matching END.
    ///
    /// <paramref name="end"/> names what is being closed, so "END IF" does not
    /// close a SUB and a missing END is reported against the right construct.
    /// </summary>
    private List<Statement> ParseBlock(string end, params string[] alsoStopAt)
    {
        var statements = new List<Statement>();

        while (!AtEnd)
        {
            SkipBlankLines();
            if (AtEnd) break;

            if (Current.IsKeyword("END") && Ahead().IsKeyword(end))
            {
                Take();
                Take();
                return statements;
            }

            // A one-word terminator such as NEXT, WEND or LOOP.
            if (Current.Kind == TokenKind.Keyword &&
                (Current.IsKeyword(end) || alsoStopAt.Any(Current.IsKeyword)))
            {
                return statements;
            }

            if (ParseStatement() is { } statement) statements.Add(statement);
        }

        Report("QB003", $"Missing END {end}.");
        return statements;
    }

    private Statement? ParseStatement()
    {
        var token = Current;

        // A label is a name followed by a colon at the start of a line.
        if (token.Kind == TokenKind.Identifier && Ahead().Kind == TokenKind.Colon)
        {
            Take();
            Take();
            return new LabelStatement(token.Text, token.Line, token.Column);
        }

        if (token.Kind == TokenKind.Keyword)
        {
            var statement = token.Text.ToUpperInvariant() switch
            {
                "DIM" => ParseDim(),
                "PRINT" => ParsePrint(),
                "INPUT" => ParseInput(),
                "IF" => ParseIf(),
                "FOR" => ParseFor(),
                "WHILE" => ParseWhile(),
                "DO" => ParseDo(),
                "SELECT" => ParseSelect(),
                "GOTO" => ParseGoto(isGosub: false),
                "GOSUB" => ParseGoto(isGosub: true),
                "CALL" => ParseCall(),
                "LET" => ParseLet(),
                "EXIT" => ParseExit(),
                "RETURN" => ParseReturn(),
                "END" => ParseEnd(),
                "CONST" => ParseConst(),
                _ => null
            };

            if (statement is not null)
            {
                EndOfStatement();
                return statement;
            }

            Report("QB004", $"'{token.Text}' cannot start a statement.");
            SkipToEndOfLine();
            return null;
        }

        if (token.Kind == TokenKind.Identifier)
        {
            var assignment = ParseAssignmentOrCall();
            EndOfStatement();
            return assignment;
        }

        Report("QB005", $"Unexpected '{token.Text}'.");
        SkipToEndOfLine();
        return null;
    }

    private Statement ParseLet()
    {
        Take();
        return ParseAssignmentOrCall()
            ?? new EndStatement(Current.Line, Current.Column);
    }

    /// <summary>
    /// Reads an assignment, or a call written without CALL.
    ///
    /// QuickBASIC allows "Greet name" as well as "CALL Greet(name)", and the
    /// two look alike until the "=" does or does not appear.
    /// </summary>
    private Statement? ParseAssignmentOrCall()
    {
        var start = Current;
        var target = ParsePrimary();

        if (Current.Kind == TokenKind.Operator && Current.Text == "=")
        {
            Take();
            var value = ParseExpression();

            return new Assignment(target, value, start.Line, start.Column);
        }

        // A bare name followed by arguments is a call.
        if (target is VariableReference reference)
        {
            var arguments = new List<Expression>();

            while (!AtEnd && Current.Kind is not (TokenKind.EndOfLine or TokenKind.EndOfFile
                                                  or TokenKind.Colon))
            {
                arguments.Add(ParseExpression());

                if (Current.Kind == TokenKind.Comma) Take();
                else break;
            }

            return new CallStatement(reference.Name, arguments, start.Line, start.Column);
        }

        if (target is IndexOrCall call)
            return new CallStatement(call.Name, call.Arguments, start.Line, start.Column);

        Report("QB006", "Expected an assignment or a call.");
        SkipToEndOfLine();
        return null;
    }

    private Statement ParseConst()
    {
        var start = Take();

        if (Current.Kind != TokenKind.Identifier)
        {
            Report("QB007", "Expected a name after CONST.");
            return new EndStatement(start.Line, start.Column);
        }

        var name = Take();

        if (Current.Kind == TokenKind.Operator && Current.Text == "=") Take();

        var value = ParseExpression();

        // A constant behaves as a variable that is assigned once.
        return new Assignment(
            new VariableReference(name.Text, name.Line, name.Column),
            value, start.Line, start.Column);
    }

    private Statement ParseDim()
    {
        var start = Take();

        if (Current.IsKeyword("SHARED")) Take();

        if (Current.Kind != TokenKind.Identifier)
        {
            Report("QB008", "Expected a name after DIM.");
            SkipToEndOfLine();
            return new EndStatement(start.Line, start.Column);
        }

        var name = Take();
        List<Expression>? bounds = null;

        if (Current.Kind == TokenKind.OpenParen)
        {
            Take();
            bounds = [];

            while (!AtEnd && Current.Kind != TokenKind.CloseParen)
            {
                bounds.Add(ParseExpression());

                // "DIM a(1 TO 10)": the upper bound is what decides the size.
                if (Current.IsKeyword("TO"))
                {
                    Take();
                    bounds[^1] = ParseExpression();
                }

                if (Current.Kind == TokenKind.Comma) Take();
                else break;
            }

            if (Current.Kind == TokenKind.CloseParen) Take();
        }

        var type = TypeSuffix.Of(name.Text);

        if (Current.IsKeyword("AS"))
        {
            Take();
            if (Current.Kind == TokenKind.Keyword) type = TypeSuffix.FromKeyword(Take().Text);
        }

        return new Declaration(name.Text, type, bounds, start.Line, start.Column);
    }

    private Statement ParsePrint()
    {
        var start = Take();
        var values = new List<Expression>();
        var trailing = false;

        while (!AtEnd && Current.Kind is not (TokenKind.EndOfLine or TokenKind.EndOfFile
                                              or TokenKind.Colon))
        {
            values.Add(ParseExpression());

            if (Current.Kind is TokenKind.Semicolon or TokenKind.Comma)
            {
                // A trailing separator suppresses the line break, which is how
                // QuickBASIC prints several values on one line.
                trailing = Current.Kind == TokenKind.Semicolon;
                Take();
                continue;
            }

            break;
        }

        return new PrintStatement(values, trailing, start.Line, start.Column);
    }

    private Statement ParseInput()
    {
        var start = Take();
        string? prompt = null;

        if (Current.Kind == TokenKind.String)
        {
            prompt = Unquote(Take().Text);

            if (Current.Kind is TokenKind.Semicolon or TokenKind.Comma) Take();
        }

        var targets = new List<string>();

        while (!AtEnd && Current.Kind == TokenKind.Identifier)
        {
            targets.Add(Take().Text);

            if (Current.Kind == TokenKind.Comma) Take();
            else break;
        }

        return new InputStatement(prompt, targets, start.Line, start.Column);
    }

    private Statement ParseIf()
    {
        var start = Take();
        var condition = ParseExpression();

        if (Current.IsKeyword("THEN")) Take();

        // A single-line IF ends with the line; a block IF continues to END IF.
        if (Current.Kind is not (TokenKind.EndOfLine or TokenKind.EndOfFile))
        {
            var thenBody = new List<Statement>();

            if (ParseStatement() is { } single) thenBody.Add(single);

            List<Statement>? elseBody = null;

            if (Current.IsKeyword("ELSE"))
            {
                Take();
                elseBody = [];
                if (ParseStatement() is { } other) elseBody.Add(other);
            }

            return new IfStatement(condition, thenBody, [], elseBody, start.Line, start.Column);
        }

        var body = ParseBlock("IF", "ELSE", "ELSEIF");
        var elseIfs = new List<(Expression, IReadOnlyList<Statement>)>();
        List<Statement>? otherwise = null;

        while (Current.IsKeyword("ELSEIF"))
        {
            Take();
            var elseIfCondition = ParseExpression();
            if (Current.IsKeyword("THEN")) Take();

            elseIfs.Add((elseIfCondition, ParseBlock("IF", "ELSE", "ELSEIF")));
        }

        if (Current.IsKeyword("ELSE"))
        {
            Take();
            otherwise = ParseBlock("IF");
        }

        return new IfStatement(condition, body, elseIfs, otherwise, start.Line, start.Column);
    }

    private Statement ParseFor()
    {
        var start = Take();

        if (Current.Kind != TokenKind.Identifier)
        {
            Report("QB009", "Expected a variable after FOR.");
            SkipToEndOfLine();
            return new EndStatement(start.Line, start.Column);
        }

        var variable = Take().Text;

        if (Current.Kind == TokenKind.Operator && Current.Text == "=") Take();

        var from = ParseExpression();

        if (Current.IsKeyword("TO")) Take();
        else Report("QB010", "Expected TO in a FOR loop.");

        var to = ParseExpression();

        Expression? step = null;
        if (Current.IsKeyword("STEP"))
        {
            Take();
            step = ParseExpression();
        }

        var body = ParseBlock("NEXT");

        if (Current.IsKeyword("NEXT"))
        {
            Take();

            // "NEXT i" names the variable again; it adds nothing but is legal.
            if (Current.Kind == TokenKind.Identifier) Take();
        }

        return new ForStatement(variable, from, to, step, body, start.Line, start.Column);
    }

    private Statement ParseWhile()
    {
        var start = Take();
        var condition = ParseExpression();

        var body = ParseBlock("WEND");

        if (Current.IsKeyword("WEND")) Take();

        return new WhileStatement(condition, body, start.Line, start.Column);
    }

    private Statement ParseDo()
    {
        var start = Take();

        Expression? condition = null;
        var until = false;
        var testAtEnd = true;

        if (Current.IsKeyword("WHILE") || Current.IsKeyword("UNTIL"))
        {
            until = Current.IsKeyword("UNTIL");
            Take();
            condition = ParseExpression();
            testAtEnd = false;
        }

        var body = ParseBlock("LOOP");

        if (Current.IsKeyword("LOOP"))
        {
            Take();

            if (Current.IsKeyword("WHILE") || Current.IsKeyword("UNTIL"))
            {
                until = Current.IsKeyword("UNTIL");
                Take();
                condition = ParseExpression();
                testAtEnd = true;
            }
        }

        return new DoStatement(condition, testAtEnd, until, body, start.Line, start.Column);
    }

    private Statement ParseSelect()
    {
        var start = Take();

        if (Current.IsKeyword("CASE")) Take();

        var value = ParseExpression();

        var cases = new List<(IReadOnlyList<Expression>, IReadOnlyList<Statement>)>();
        List<Statement>? otherwise = null;

        SkipBlankLines();

        while (Current.IsKeyword("CASE"))
        {
            Take();

            if (Current.IsKeyword("ELSE"))
            {
                Take();
                otherwise = ParseBlock("SELECT", "CASE");
                break;
            }

            var values = new List<Expression>();

            while (!AtEnd && Current.Kind is not (TokenKind.EndOfLine or TokenKind.EndOfFile))
            {
                values.Add(ParseExpression());

                if (Current.Kind == TokenKind.Comma) Take();
                else break;
            }

            cases.Add((values, ParseBlock("SELECT", "CASE")));
            SkipBlankLines();
        }

        if (Current.IsKeyword("END") && Ahead().IsKeyword("SELECT"))
        {
            Take();
            Take();
        }

        return new SelectStatement(value, cases, otherwise, start.Line, start.Column);
    }

    private Statement ParseGoto(bool isGosub)
    {
        var start = Take();

        var label = Current.Kind is TokenKind.Identifier or TokenKind.Number
            ? Take().Text
            : "";

        if (label.Length == 0)
            Report("QB011", $"Expected a label after {(isGosub ? "GOSUB" : "GOTO")}.");

        return isGosub
            ? new GosubStatement(label, start.Line, start.Column)
            : new GotoStatement(label, start.Line, start.Column);
    }

    private Statement ParseCall()
    {
        var start = Take();

        if (Current.Kind != TokenKind.Identifier)
        {
            Report("QB012", "Expected a procedure name after CALL.");
            SkipToEndOfLine();
            return new EndStatement(start.Line, start.Column);
        }

        var name = Take().Text;
        var arguments = new List<Expression>();

        if (Current.Kind == TokenKind.OpenParen)
        {
            Take();

            while (!AtEnd && Current.Kind != TokenKind.CloseParen)
            {
                arguments.Add(ParseExpression());

                if (Current.Kind == TokenKind.Comma) Take();
                else break;
            }

            if (Current.Kind == TokenKind.CloseParen) Take();
        }

        return new CallStatement(name, arguments, start.Line, start.Column);
    }

    private Statement ParseExit()
    {
        var start = Take();

        var what = Current.Kind == TokenKind.Keyword ? Take().Text.ToUpperInvariant() : "";

        return new ExitStatement(what, start.Line, start.Column);
    }

    private Statement ParseReturn()
    {
        var start = Take();

        var value = Current.Kind is TokenKind.EndOfLine or TokenKind.EndOfFile or TokenKind.Colon
            ? null
            : ParseExpression();

        return new ReturnStatement(value, start.Line, start.Column);
    }

    private Statement ParseEnd()
    {
        var start = Take();

        // A bare END stops the program; "END IF" and the like are terminators
        // handled by the block that opened them.
        if (Current.Kind == TokenKind.Keyword) Take();

        return new EndStatement(start.Line, start.Column);
    }

    // Expressions, loosest binding first.

    private Expression ParseExpression() => ParseOr();

    private Expression ParseOr()
    {
        var left = ParseAnd();

        while (Current.IsKeyword("OR") || Current.IsKeyword("XOR"))
        {
            var op = Take();
            var right = ParseAnd();
            left = new Binary(op.Text.ToUpperInvariant(), left, right, op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseAnd()
    {
        var left = ParseNot();

        while (Current.IsKeyword("AND"))
        {
            var op = Take();
            var right = ParseNot();
            left = new Binary("AND", left, right, op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseNot()
    {
        if (!Current.IsKeyword("NOT")) return ParseComparison();

        var op = Take();
        return new Unary("NOT", ParseNot(), op.Line, op.Column);
    }

    private Expression ParseComparison()
    {
        var left = ParseAdditive();

        while (Current.Kind == TokenKind.Operator &&
               Current.Text is "=" or "<>" or "<" or ">" or "<=" or ">=")
        {
            var op = Take();
            var right = ParseAdditive();
            left = new Binary(op.Text, left, right, op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseAdditive()
    {
        var left = ParseMultiplicative();

        while (Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
        {
            var op = Take();
            var right = ParseMultiplicative();
            left = new Binary(op.Text, left, right, op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseMultiplicative()
    {
        var left = ParseUnary();

        while ((Current.Kind == TokenKind.Operator && Current.Text is "*" or "/" or "\\")
               || Current.IsKeyword("MOD"))
        {
            var op = Take();
            var right = ParseUnary();

            left = new Binary(op.Text.ToUpperInvariant(), left, right, op.Line, op.Column);
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (Current.Kind == TokenKind.Operator && Current.Text is "-" or "+")
        {
            var op = Take();
            return new Unary(op.Text, ParseUnary(), op.Line, op.Column);
        }

        return ParsePower();
    }

    private Expression ParsePower()
    {
        var left = ParsePrimary();

        // Right-associative: 2 ^ 3 ^ 2 is 2 ^ (3 ^ 2).
        if (Current.Kind == TokenKind.Operator && Current.Text == "^")
        {
            var op = Take();
            return new Binary("^", left, ParseUnary(), op.Line, op.Column);
        }

        return left;
    }

    private Expression ParsePrimary()
    {
        var token = Current;

        switch (token.Kind)
        {
            case TokenKind.Number:
                Take();
                return new NumberLiteral(
                    ParseNumber(token.Text), TypeSuffix.Of(token.Text), token.Line, token.Column);

            case TokenKind.String:
                Take();
                return new StringLiteral(Unquote(token.Text), token.Line, token.Column);

            case TokenKind.OpenParen:
            {
                Take();
                var inner = ParseExpression();

                if (Current.Kind == TokenKind.CloseParen) Take();
                else Report("QB013", "Expected ')'.");

                return inner;
            }

            case TokenKind.Identifier:
            {
                Take();

                if (Current.Kind != TokenKind.OpenParen)
                    return new VariableReference(token.Text, token.Line, token.Column);

                Take();
                var arguments = new List<Expression>();

                while (!AtEnd && Current.Kind != TokenKind.CloseParen)
                {
                    arguments.Add(ParseExpression());

                    if (Current.Kind == TokenKind.Comma) Take();
                    else break;
                }

                if (Current.Kind == TokenKind.CloseParen) Take();
                else Report("QB013", "Expected ')'.");

                return new IndexOrCall(token.Text, arguments, token.Line, token.Column);
            }

            case TokenKind.Keyword when token.IsKeyword("TRUE") || token.IsKeyword("FALSE"):
                Take();
                return new NumberLiteral(
                    token.IsKeyword("TRUE") ? -1 : 0, BasicType.Integer, token.Line, token.Column);

            default:
                Report("QB014", $"Expected a value, found '{token.Text}'.");
                Take();
                return new NumberLiteral(0, BasicType.Integer, token.Line, token.Column);
        }
    }

    private static double ParseNumber(string text)
    {
        var digits = text.TrimEnd('!', '#', '%', '&');

        return double.TryParse(
            digits, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    /// <summary>Removes the quotes from a literal, leaving the text itself.</summary>
    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') return text[1..^1];

        return text.TrimStart('"');
    }

    private void SkipBlankLines()
    {
        while (Current.Kind is TokenKind.EndOfLine or TokenKind.Colon) Take();
    }

    private void SkipToEndOfLine()
    {
        while (!AtEnd && Current.Kind != TokenKind.EndOfLine) Take();
    }

    /// <summary>
    /// Consumes what ends a statement.
    ///
    /// A colon separates statements on one line, so it ends this one without
    /// ending the line.
    /// </summary>
    private void EndOfStatement()
    {
        if (Current.Kind is TokenKind.Colon or TokenKind.EndOfLine) Take();
    }

    private void Report(string id, string message) =>
        _diagnostics.Add(new Diagnostic(id, message, Current.Line, Current.Column));
}
