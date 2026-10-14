using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// Turns a Visual Basic expression typed in a watch, the Immediate window or a hover into the C# that
/// netcoredbg evaluates, as a short list of candidates to try in order.
/// </summary>
/// <remarks>
/// <para>
/// netcoredbg evaluates C# whatever the program is written in. A VB user's "total Mod 4", "numbers(1)",
/// "Not found" or "Me.Name" all came back "not available". The expression is parsed with the Visual Basic
/// parser, never matched by text, and written out node by node.
/// </para>
/// <para>
/// Some VB syntax means two things the parser alone cannot tell apart: "items(1)" indexes an array or calls a
/// function, "Not x" negates a Boolean or an Integer. Rather than guess, each reading becomes a candidate, the
/// likelier first. The evaluator refuses the wrong ones, so the first accepted is the right one. The forms
/// written are the ones netcoredbg was measured to accept (3.2.0): it has no "== null", no casts and no
/// "?:", calls a property's getter where it cannot read the property ("s.get_Length()"), and converts through
/// System.Convert.
/// </para>
/// <para>
/// An expression the parser does not accept whole, or that uses syntax with no C# the evaluator understands
/// (a date literal, TypeOf, a lambda), is returned untranslated, as the single candidate: C# typed by
/// someone used to it keeps working, and the rest fails as it did before.
/// </para>
/// </remarks>
public static class VbExpressionTranslator
{
    /// <summary>The most candidates returned: each one is a round trip to the debugger.</summary>
    public const int MaxCandidates = 8;

    /// <summary>The C# readings of a Visual Basic expression, likeliest first, never empty for non-empty text.</summary>
    public static IReadOnlyList<string> Candidates(string expression)
    {
        var text = expression.Trim();

        if (text.Length == 0) return [];

        var syntax = SyntaxFactory.ParseExpression(text);

        // A partial parse ("a = = b", "i == 4") is not the user's VB: what they typed goes to the evaluator as it is.
        if (syntax.ContainsDiagnostics || syntax.FullSpan.Length != text.Length) return [text];

        try
        {
            // Readings that run nothing first: once one that calls something has been refused, no other is sent
            // (it may have run the call already), so every harmless reading must have had its turn.
            var candidates = Translate(syntax)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(Calls)
                .Take(MaxCandidates)
                .ToList();

            return candidates.Count == 0 ? [text] : candidates;
        }
        catch (UnsupportedSyntaxException)
        {
            return [text];
        }
    }

    private sealed class UnsupportedSyntaxException : Exception;

    private static List<string> Translate(ExpressionSyntax node) => node switch
    {
        IdentifierNameSyntax name => [Identifier(name.Identifier)],
        MeExpressionSyntax or MyClassExpressionSyntax => ["this"],
        MyBaseExpressionSyntax => ["base"],
        LiteralExpressionSyntax literal => [Literal(literal)],
        PredefinedTypeSyntax type => [PredefinedType(type.Keyword)],
        ParenthesizedExpressionSyntax parenthesized => [.. Translate(parenthesized.Expression).Select(inner => $"({inner})")],
        UnaryExpressionSyntax unary => Unary(unary),
        BinaryExpressionSyntax binary => Binary(binary),
        MemberAccessExpressionSyntax access => MemberAccess(access, withGetter: true),
        InvocationExpressionSyntax invocation => Invocation(invocation),
        PredefinedCastExpressionSyntax cast => PredefinedCast(cast),
        // DirectCast leaves the value as it is; the evaluator has no casts, so the operand alone is read, grouped
        // so that "DirectCast(a + b, Object).ToString()" stays (a + b).ToString(). TryCast also tests the type,
        // which nothing here can write, so it is not translated.
        DirectCastExpressionSyntax cast => [.. Translate(cast.Expression).Select(Grouped)],
        TernaryConditionalExpressionSyntax ternary => Combine(
            [Translate(ternary.Condition), Translate(ternary.WhenTrue), Translate(ternary.WhenFalse)],
            parts => $"({Grouped(parts[0])} ? {Grouped(parts[1])} : {Grouped(parts[2])})"),
        BinaryConditionalExpressionSyntax coalesce => Combine(
            [Translate(coalesce.FirstExpression), Translate(coalesce.SecondExpression)],
            parts => $"({Grouped(parts[0])} ?? {Grouped(parts[1])})"),
        _ => throw new UnsupportedSyntaxException(),
    };

    /// <summary>A name as C# spells it: VB's [Escaped] brackets dropped, a C# keyword escaped with @.</summary>
    private static string Identifier(SyntaxToken identifier)
    {
        var name = identifier.ValueText;

        return CSharpKeywords.Contains(name) ? "@" + name : name;
    }

    /// <summary>C#'s reserved words: a VB variable may be called "string" or "base"; C# needs "@string".</summary>
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    private static string PredefinedType(SyntaxToken keyword) => keyword.Kind() switch
    {
        SyntaxKind.StringKeyword => "System.String",
        SyntaxKind.IntegerKeyword => "System.Int32",
        SyntaxKind.LongKeyword => "System.Int64",
        SyntaxKind.ShortKeyword => "System.Int16",
        SyntaxKind.ByteKeyword => "System.Byte",
        SyntaxKind.SByteKeyword => "System.SByte",
        SyntaxKind.UIntegerKeyword => "System.UInt32",
        SyntaxKind.ULongKeyword => "System.UInt64",
        SyntaxKind.UShortKeyword => "System.UInt16",
        SyntaxKind.DoubleKeyword => "System.Double",
        SyntaxKind.SingleKeyword => "System.Single",
        SyntaxKind.DecimalKeyword => "System.Decimal",
        SyntaxKind.BooleanKeyword => "System.Boolean",
        SyntaxKind.CharKeyword => "System.Char",
        SyntaxKind.DateKeyword => "System.DateTime",
        SyntaxKind.ObjectKeyword => "System.Object",
        _ => throw new UnsupportedSyntaxException(),
    };

    private static string Literal(LiteralExpressionSyntax literal)
    {
        var token = literal.Token;

        switch (token.Kind())
        {
            case SyntaxKind.NothingKeyword:
                return "null";
            case SyntaxKind.TrueKeyword:
                return "true";
            case SyntaxKind.FalseKeyword:
                return "false";
            case SyntaxKind.StringLiteralToken:
                return Quote((string)token.Value!, '"');
            case SyntaxKind.CharacterLiteralToken:
                return Quote(((char)token.Value!).ToString(), '\'');
        }

        // Numbers: the value the VB parser read, written with the C# suffix of its type, so "10L", "&HFF" and
        // "1.5D" keep their type and value.
        return token.Value switch
        {
            int value => value.ToString(CultureInfo.InvariantCulture),
            long value => value.ToString(CultureInfo.InvariantCulture) + "L",
            uint value => value.ToString(CultureInfo.InvariantCulture) + "U",
            ulong value => value.ToString(CultureInfo.InvariantCulture) + "UL",
            short or ushort or byte or sbyte => Convert.ToString(token.Value, CultureInfo.InvariantCulture)!,
            double value => Floating(value.ToString("R", CultureInfo.InvariantCulture)),
            float value => value.ToString("R", CultureInfo.InvariantCulture) + "f",
            decimal value => value.ToString(CultureInfo.InvariantCulture) + "m",
            _ => throw new UnsupportedSyntaxException(),
        };
    }

    /// <summary>A double literal C# reads as a double: "3" alone would be an integer.</summary>
    private static string Floating(string text) =>
        text.Contains('.') || text.Contains('E') || text.Contains("Infinity") || text.Contains("NaN") ? text : text + ".0";

    private static string Quote(string value, char quote)
    {
        var builder = new StringBuilder(value.Length + 2).Append(quote);

        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '\\' => @"\\",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                '\0' => @"\0",
                _ when c == quote => "\\" + quote,
                _ => c.ToString(),
            });
        }

        return builder.Append(quote).ToString();
    }

    private static List<string> Unary(UnaryExpressionSyntax unary)
    {
        var operands = Translate(unary.Operand).Select(Grouped).ToList();

        return unary.Kind() switch
        {
            SyntaxKind.UnaryMinusExpression => [.. operands.Select(o => "-" + o)],
            SyntaxKind.UnaryPlusExpression => [.. operands.Select(o => "+" + o)],

            // Not negates a Boolean and complements an Integer; the operand's type decides, and only the
            // evaluator knows it.
            // The two negations of each operand side by side, so the cap cuts the least likely operands, never "~".
            SyntaxKind.NotExpression => Capped([.. operands.SelectMany(o => (string[])["!" + o, "~" + o])]),
            _ => throw new UnsupportedSyntaxException(),
        };
    }

    private static List<string> Binary(BinaryExpressionSyntax binary)
    {
        var kind = binary.Kind();

        // "x Is Nothing": the evaluator refuses "== null", so the forms it may take are tried in turn.
        if (kind is SyntaxKind.IsExpression or SyntaxKind.IsNotExpression
            && (IsNothing(binary.Left) || IsNothing(binary.Right)))
        {
            var other = IsNothing(binary.Left) ? binary.Right : binary.Left;
            var negate = kind == SyntaxKind.IsNotExpression;

            return [.. Translate(other).Select(Grouped).SelectMany(o => (string[])
            [
                negate ? $"{o} != null" : $"{o} == null",
                negate ? $"!System.Object.ReferenceEquals({o}, null)" : $"System.Object.ReferenceEquals({o}, null)",
                negate ? $"!System.Object.Equals({o}, null)" : $"System.Object.Equals({o}, null)",
            ])];
        }

        // Every operand is grouped: VB and C# rank their operators differently ("Not a = b" is Not (a = b)),
        // and parentheses make the C# mean what the VB meant whatever the ranks.
        return Combine([Translate(binary.Left), Translate(binary.Right)], parts =>
        {
            var left = Grouped(parts[0]);
            var right = Grouped(parts[1]);

            return kind switch
            {
                SyntaxKind.AddExpression => $"{left} + {right}",
                SyntaxKind.SubtractExpression => $"{left} - {right}",
                SyntaxKind.MultiplyExpression => $"{left} * {right}",
                SyntaxKind.ModuloExpression => $"{left} % {right}",

                // "\" makes whole numbers of its operands first, Doubles included: 7.5 \ 2 is 4 \ 2.
                SyntaxKind.IntegerDivideExpression =>
                    $"System.Convert.ToInt64({parts[0]}) / System.Convert.ToInt64({parts[1]})",

                // "/" always gives a Double in VB; on two integers C# would drop the fraction.
                SyntaxKind.DivideExpression => $"1.0 * {left} / {right}",

                // "&" joins as text whatever the operands are; "" first makes C#'s "+" do the same.
                SyntaxKind.ConcatenateExpression => $"\"\" + {left} + {right}",
                SyntaxKind.ExponentiateExpression =>
                    $"System.Math.Pow(System.Convert.ToDouble({parts[0]}), System.Convert.ToDouble({parts[1]}))",
                SyntaxKind.EqualsExpression => $"{left} == {right}",
                SyntaxKind.NotEqualsExpression => $"{left} != {right}",
                SyntaxKind.LessThanExpression => $"{left} < {right}",
                SyntaxKind.LessThanOrEqualExpression => $"{left} <= {right}",
                SyntaxKind.GreaterThanExpression => $"{left} > {right}",
                SyntaxKind.GreaterThanOrEqualExpression => $"{left} >= {right}",
                SyntaxKind.IsExpression => $"{left} == {right}",
                SyntaxKind.IsNotExpression => $"{left} != {right}",
                SyntaxKind.AndExpression => $"{left} & {right}",
                SyntaxKind.OrExpression => $"{left} | {right}",
                SyntaxKind.ExclusiveOrExpression => $"{left} ^ {right}",
                SyntaxKind.AndAlsoExpression => $"{left} && {right}",
                SyntaxKind.OrElseExpression => $"{left} || {right}",
                SyntaxKind.LeftShiftExpression => $"{left} << {right}",
                SyntaxKind.RightShiftExpression => $"{left} >> {right}",
                _ => throw new UnsupportedSyntaxException(),
            };
        });
    }

    private static bool IsNothing(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax { Token.RawKind: (int)SyntaxKind.NothingKeyword };

    /// <param name="withGetter">False for the target of a call: "s.ToUpper()" has no getter to try.</param>
    private static List<string> MemberAccess(MemberAccessExpressionSyntax access, bool withGetter)
    {
        // ".Name" inside a With block: what it belongs to is not in the expression.
        if (access.Expression is null || access.Name is not IdentifierNameSyntax name)
            throw new UnsupportedSyntaxException();

        // "row!Name" looks a key up; it is the default property with "Name" as a string.
        if (access.IsKind(SyntaxKind.DictionaryAccessExpression))
            return Capped([.. Translate(access.Expression).Select(t => $"{Grouped(t)}[{Quote(name.Identifier.ValueText, '"')}]")]);

        var member = Identifier(name.Identifier);
        var targets = Translate(access.Expression).Select(Grouped).ToList();

        // The property as written, then its getter: netcoredbg reads some properties (a string's or an
        // array's Length) only that way.
        return Capped(withGetter
            ? [.. targets.Select(t => $"{t}.{member}"), .. targets.Select(t => $"{t}.get_{member}()")]
            : [.. targets.Select(t => $"{t}.{member}")]);
    }

    private static List<string> Invocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is null) throw new UnsupportedSyntaxException();

        var arguments = invocation.ArgumentList?.Arguments ?? default;

        foreach (var argument in arguments)
        {
            if (argument is not SimpleArgumentSyntax { NameColonEquals: null }) throw new UnsupportedSyntaxException();
        }

        var argumentTexts = arguments.Select(a => Translate(((SimpleArgumentSyntax)a).GetExpression())).ToList();

        // Len(x) is the length of a string; written as the getter netcoredbg reads.
        if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: var function }
            && function.Equals("Len", StringComparison.OrdinalIgnoreCase)
            && argumentTexts.Count == 1)
        {
            return Capped([.. argumentTexts[0].Select(a => $"{Grouped(a)}.get_Length()"), .. argumentTexts[0].Select(a => $"{Grouped(a)}.Length")]);
        }

        // The index first, after a dot too ("dt.Rows(0)", "Me.items(1)"): reading it runs nothing, and the
        // Immediate window never tries a call again once one is refused. The call goes first instead where it is
        // likelier and a refused index would cost it its turn: when the target already calls something
        // ("s.Trim().Substring(1)"), or when the result is used with a dot ("Name.Substring(0, 3).ToUpper()").
        var callFirst = invocation.Parent is MemberAccessExpressionSyntax { Expression: var dereferenced }
                        && dereferenced == invocation;

        var targets = invocation.Expression is MemberAccessExpressionSyntax access
            ? MemberAccess(access, withGetter: false)
            : Translate(invocation.Expression);

        return Combine([targets, .. argumentTexts], parts =>
        {
            var target = parts[0];
            var list = string.Join(", ", parts.Skip(1));

            return argumentTexts.Count == 0 ? [$"{target}()"]
                : callFirst || Calls(target) ? [$"{target}({list})", $"{target}[{list}]"]
                : [$"{target}[{list}]", $"{target}({list})"];
        });
    }

    private static List<string> PredefinedCast(PredefinedCastExpressionSyntax cast)
    {
        // VB turns True into -1, System.Convert into 1: a Boolean literal is written as VB's number. A Boolean
        // variable cannot be told from the syntax, which is the one difference left.
        if (cast.Expression is LiteralExpressionSyntax { Token.RawKind: (int)SyntaxKind.TrueKeyword or (int)SyntaxKind.FalseKeyword } flag
            && cast.Keyword.Kind() is not (SyntaxKind.CBoolKeyword or SyntaxKind.CStrKeyword or SyntaxKind.CObjKeyword))
        {
            var isTrue = flag.Token.IsKind(SyntaxKind.TrueKeyword);

            // True is all bits set: -1 where the type is signed, its largest value where it is not (CByte(True) is 255).
            var value = !isTrue ? "0" : cast.Keyword.Kind() switch
            {
                SyntaxKind.CByteKeyword => "255",
                SyntaxKind.CUShortKeyword => "65535",
                SyntaxKind.CUIntKeyword => "4294967295",
                SyntaxKind.CULngKeyword => "18446744073709551615",
                _ => "-1",
            };

            return [Conversion(cast.Keyword, value)];
        }

        return [.. Translate(cast.Expression).Select(operand => Conversion(cast.Keyword, operand))];
    }

    /// <summary>CInt, CStr and the like, through System.Convert: the evaluator has no casts.</summary>
    private static string Conversion(SyntaxToken keyword, string operand) => keyword.Kind() switch
    {
        SyntaxKind.CStrKeyword => $"System.Convert.ToString({operand})",
        SyntaxKind.CIntKeyword => $"System.Convert.ToInt32({operand})",
        SyntaxKind.CLngKeyword => $"System.Convert.ToInt64({operand})",
        SyntaxKind.CShortKeyword => $"System.Convert.ToInt16({operand})",
        SyntaxKind.CByteKeyword => $"System.Convert.ToByte({operand})",
        SyntaxKind.CSByteKeyword => $"System.Convert.ToSByte({operand})",
        SyntaxKind.CUIntKeyword => $"System.Convert.ToUInt32({operand})",
        SyntaxKind.CULngKeyword => $"System.Convert.ToUInt64({operand})",
        SyntaxKind.CUShortKeyword => $"System.Convert.ToUInt16({operand})",
        SyntaxKind.CDblKeyword => $"System.Convert.ToDouble({operand})",
        SyntaxKind.CSngKeyword => $"System.Convert.ToSingle({operand})",
        SyntaxKind.CDecKeyword => $"System.Convert.ToDecimal({operand})",
        SyntaxKind.CBoolKeyword => $"System.Convert.ToBoolean({operand})",
        SyntaxKind.CCharKeyword => $"System.Convert.ToChar({operand})",
        SyntaxKind.CDateKeyword => $"System.Convert.ToDateTime({operand})",
        SyntaxKind.CObjKeyword => Grouped(operand),
        _ => throw new UnsupportedSyntaxException(),
    };

    /// <summary>An operand wrapped in parentheses unless it is a single name, literal or member chain.</summary>
    private static string Grouped(string text) =>
        text.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '@')
        || IsSingleLiteral(text)
        || (text.StartsWith('(') && Closes(text))
            ? text
            : $"({text})";

    /// <summary>Whether the text is one string or character literal, whatever is inside it.</summary>
    private static bool IsSingleLiteral(string text)
    {
        if (text.Length < 2 || text[0] is not ('"' or '\'') || text[^1] != text[0]) return false;

        for (var i = 1; i < text.Length - 1; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == text[0]) return false;
        }

        return true;
    }

    /// <summary>Whether the parenthesis that opens the text is the one that closes it.</summary>
    private static bool Closes(string text)
    {
        var depth = 0;
        var inString = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i == text.Length - 1;
        }

        return false;
    }

    /// <summary>
    /// Every way of putting the parts' readings together, the first reading of each first, at most
    /// <see cref="MaxCandidates"/> of them.
    /// </summary>
    private static List<string> Combine(IReadOnlyList<List<string>> parts, Func<IReadOnlyList<string>, IEnumerable<string>> build)
    {
        var results = new List<string>();

        void Walk(int index, string[] chosen)
        {
            if (results.Count >= MaxCandidates) return;

            if (index == parts.Count)
            {
                foreach (var text in build(chosen))
                {
                    if (results.Count >= MaxCandidates) return;
                    results.Add(text);
                }

                return;
            }

            foreach (var option in parts[index])
            {
                chosen[index] = option;
                Walk(index + 1, chosen);
            }
        }

        Walk(0, new string[parts.Count]);

        return results;
    }

    /// <summary>At most <see cref="MaxCandidates"/> readings, so a long chain of members stays small at every level.</summary>
    private static List<string> Capped(List<string> readings) =>
        readings.Count <= MaxCandidates ? readings : readings.GetRange(0, MaxCandidates);

    /// <summary>
    /// Whether a reading calls something. Such a reading can run code before the evaluator refuses it
    /// ("!Compute(n)" calls Compute, then finds ! does not apply to an Integer), so no later reading is tried
    /// after it fails: it would run the call again.
    /// </summary>
    public static bool Calls(string csharp)
    {
        var inString = false;

        for (var i = 0; i < csharp.Length; i++)
        {
            var c = csharp[i];

            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '(' && i > 0 && (char.IsLetterOrDigit(csharp[i - 1]) || csharp[i - 1] is '_' or ')')
                     && !IsGetter(csharp, i)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the call opening at <paramref name="parenthesis"/> is a property getter, "x.get_Length()": it reads
    /// what the property reads, which a watch on the property does too, so it does not count as running code.
    /// </summary>
    private static bool IsGetter(string csharp, int parenthesis)
    {
        if (parenthesis + 1 >= csharp.Length || csharp[parenthesis + 1] != ')') return false;

        var start = parenthesis;

        while (start > 0 && (char.IsLetterOrDigit(csharp[start - 1]) || csharp[start - 1] == '_')) start--;

        return start > 0 && csharp[start - 1] == '.'
            && csharp.AsSpan(start, parenthesis - start).StartsWith("get_", StringComparison.Ordinal);
    }

    private static List<string> Combine(IReadOnlyList<List<string>> parts, Func<IReadOnlyList<string>, string> build) =>
        Combine(parts, chosen => (IEnumerable<string>)[build(chosen)]);
}
