namespace Basalt.QuickBasic;

/// <summary>What a token is.</summary>
public enum TokenKind
{
    EndOfFile, EndOfLine,
    Identifier, Number, String,
    Keyword, Operator, Comma, Colon, Semicolon,
    OpenParen, CloseParen,
    Unknown
}

/// <summary>One token, with where it came from.</summary>
public readonly record struct Token(
    TokenKind Kind, string Text, int Line, int Column, int Start)
{
    /// <summary>Whether this is the given keyword, ignoring case as QuickBASIC does.</summary>
    public bool IsKeyword(string keyword) =>
        Kind == TokenKind.Keyword && Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Kind} '{Text}' at {Line}:{Column}";
}

/// <summary>
/// Turns QuickBASIC source into tokens.
///
/// Line-oriented, as the language is: a statement ends at the end of a line
/// unless a colon separates several on one, and a line ending is a token
/// rather than whitespace because the parser needs to see it.
/// </summary>
public sealed class Lexer
{
    /// <summary>
    /// The reserved words.
    ///
    /// A word not in this set is an identifier, so a program using "Count" as
    /// a variable keeps working even though "COUNT" looks like a keyword in
    /// other dialects.
    /// </summary>
    public static IReadOnlySet<string> Keywords { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "AS", "CALL", "CASE", "CONST", "DECLARE", "DIM", "DO", "DOUBLE",
            "ELSE", "ELSEIF", "END", "EXIT", "FOR", "FUNCTION", "GOSUB", "GOTO",
            "IF", "INPUT", "INTEGER", "LET", "LOOP", "LONG", "MOD", "NEXT",
            "NOT", "OR", "PRINT", "REM", "RETURN", "SELECT", "SHARED", "SINGLE",
            "STATIC", "STEP", "STRING", "SUB", "THEN", "TO", "UNTIL", "WEND",
            "WHILE", "XOR", "TRUE", "FALSE"
        };

    private readonly string _text;
    private int _index;
    private int _line = 1;
    private int _column = 1;

    public Lexer(string text) => _text = text;

    /// <summary>Reads the whole document.</summary>
    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (true)
        {
            var token = Next();
            tokens.Add(token);

            if (token.Kind == TokenKind.EndOfFile) return tokens;
        }
    }

    private Token Next()
    {
        SkipSpacesAndComments();

        if (_index >= _text.Length) return Make(TokenKind.EndOfFile, "");

        var c = _text[_index];

        if (c is '\n' or '\r') return ReadLineEnding();
        if (char.IsDigit(c) || (c == '.' && PeekIsDigit())) return ReadNumber();
        if (c == '"') return ReadString();
        if (char.IsLetter(c) || c == '_') return ReadWord();

        return ReadSymbol();
    }

    /// <summary>
    /// Skips whitespace and comments.
    ///
    /// A comment runs to the end of the line but does not consume the line
    /// ending: the parser still needs that to know the statement has ended.
    /// </summary>
    private void SkipSpacesAndComments()
    {
        while (_index < _text.Length)
        {
            var c = _text[_index];

            if (c is ' ' or '\t')
            {
                Advance();
                continue;
            }

            // "'" starts a comment; so does REM, handled where words are read.
            if (c == '\'')
            {
                while (_index < _text.Length && _text[_index] is not ('\n' or '\r')) Advance();
                continue;
            }

            // A line continued with "_" joins the next line.
            if (c == '_' && IsAtEndOfLine(_index + 1))
            {
                Advance();
                while (_index < _text.Length && _text[_index] is not ('\n' or '\r')) Advance();
                if (_index < _text.Length) ConsumeLineEnding();
                continue;
            }

            return;
        }
    }

    private bool IsAtEndOfLine(int from)
    {
        for (var i = from; i < _text.Length; i++)
        {
            if (_text[i] is ' ' or '\t') continue;
            return _text[i] is '\n' or '\r';
        }

        return true;
    }

    private Token ReadLineEnding()
    {
        var start = _index;
        var line = _line;
        var column = _column;

        ConsumeLineEnding();

        return new Token(TokenKind.EndOfLine, _text[start.._index], line, column, start);
    }

    private void ConsumeLineEnding()
    {
        if (_text[_index] == '\r') Advance();
        if (_index < _text.Length && _text[_index] == '\n') Advance();

        _line++;
        _column = 1;
    }

    private bool PeekIsDigit() =>
        _index + 1 < _text.Length && char.IsDigit(_text[_index + 1]);

    private Token ReadNumber()
    {
        var start = _index;
        var line = _line;
        var column = _column;

        while (_index < _text.Length && (char.IsDigit(_text[_index]) || _text[_index] == '.'))
            Advance();

        // A trailing type character says what kind of number it is.
        if (_index < _text.Length && _text[_index] is '!' or '#' or '%' or '&') Advance();

        return new Token(TokenKind.Number, _text[start.._index], line, column, start);
    }

    /// <summary>
    /// Reads a string literal.
    ///
    /// QuickBASIC strings cannot span lines, so an unterminated one ends at
    /// the line ending rather than swallowing the rest of the program.
    /// </summary>
    private Token ReadString()
    {
        var start = _index;
        var line = _line;
        var column = _column;

        Advance();

        while (_index < _text.Length && _text[_index] != '"' && _text[_index] is not ('\n' or '\r'))
            Advance();

        if (_index < _text.Length && _text[_index] == '"') Advance();

        return new Token(TokenKind.String, _text[start.._index], line, column, start);
    }

    private Token ReadWord()
    {
        var start = _index;
        var line = _line;
        var column = _column;

        while (_index < _text.Length && (char.IsLetterOrDigit(_text[_index]) || _text[_index] == '_'))
            Advance();

        // "$", "%", "!", "#" and "&" are part of the name in QuickBASIC: they
        // say the type, and "name$" is a different variable from "name".
        if (_index < _text.Length && _text[_index] is '$' or '%' or '!' or '#' or '&') Advance();

        var text = _text[start.._index];

        // REM starts a comment that runs to the end of the line.
        if (text.Equals("REM", StringComparison.OrdinalIgnoreCase))
        {
            while (_index < _text.Length && _text[_index] is not ('\n' or '\r')) Advance();

            return Next();
        }

        var kind = Keywords.Contains(text) ? TokenKind.Keyword : TokenKind.Identifier;

        return new Token(kind, text, line, column, start);
    }

    private Token ReadSymbol()
    {
        var start = _index;
        var line = _line;
        var column = _column;

        var c = _text[_index];
        Advance();

        // Two-character operators, checked before the single-character ones so
        // that "<=" is not read as "<" followed by "=".
        if (_index < _text.Length)
        {
            var pair = _text[start..(_index + 1)];

            if (pair is "<=" or ">=" or "<>")
            {
                Advance();
                return new Token(TokenKind.Operator, pair, line, column, start);
            }
        }

        var kind = c switch
        {
            ',' => TokenKind.Comma,
            ':' => TokenKind.Colon,
            ';' => TokenKind.Semicolon,
            '(' => TokenKind.OpenParen,
            ')' => TokenKind.CloseParen,
            '+' or '-' or '*' or '/' or '\\' or '^' or '=' or '<' or '>' => TokenKind.Operator,
            _ => TokenKind.Unknown
        };

        return new Token(kind, c.ToString(), line, column, start);
    }

    private Token Make(TokenKind kind, string text) =>
        new(kind, text, _line, _column, _index);

    private void Advance()
    {
        _index++;
        _column++;
    }
}
