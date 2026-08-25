using System.Text;

namespace Basalt.Workspace.Debugging;

/// <summary>
/// Rewrites a Visual Basic condition into the C# the debugger understands.
///
/// netcoredbg evaluates expressions as C# whatever language the program is
/// written in. Left alone, a VB user's "i = 4" is read as an assignment, which
/// is always true, so the breakpoint stops on the first pass instead of the
/// one asked for — wrong in a way that looks like the debugger is broken.
///
/// This is a translation of the comparison syntax people actually type in a
/// breakpoint condition, not a VB parser. Anything it does not recognise is
/// passed through untouched.
/// </summary>
public static class VbConditionTranslator
{
    /// <summary>Turns a condition written in Visual Basic into its C# equivalent.</summary>
    public static string ToEvaluatorSyntax(string condition)
    {
        if (condition.Length == 0) return condition;

        var result = new StringBuilder(condition.Length + 8);
        var index = 0;

        while (index < condition.Length)
        {
            var current = condition[index];

            // String literals are copied across as they are: what is inside
            // them is data, not syntax.
            if (current == '"')
            {
                index = CopyStringLiteral(condition, index, result);
                continue;
            }

            if (TryWord(condition, index, out var word, out var after))
            {
                result.Append(TranslateWord(word));
                index = after;
                continue;
            }

            if (current == '=' && !IsPartOfAnOperator(condition, index))
            {
                result.Append("==");
                index++;
                continue;
            }

            if (current == '<' && index + 1 < condition.Length && condition[index + 1] == '>')
            {
                result.Append("!=");
                index += 2;
                continue;
            }

            result.Append(current);
            index++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Whether an "=" is already part of an operator such as "<=" or "==".
    ///
    /// Without this, "i <= 4" would become "i <== 4".
    /// </summary>
    private static bool IsPartOfAnOperator(string condition, int index)
    {
        if (index > 0 && condition[index - 1] is '<' or '>' or '!' or '=') return true;

        return index + 1 < condition.Length && condition[index + 1] == '=';
    }

    private static int CopyStringLiteral(string condition, int index, StringBuilder result)
    {
        result.Append(condition[index]);
        index++;

        while (index < condition.Length)
        {
            result.Append(condition[index]);

            // "" inside a VB literal is an escaped quote, not the end of it.
            if (condition[index] == '"')
            {
                if (index + 1 < condition.Length && condition[index + 1] == '"')
                {
                    result.Append(condition[index + 1]);
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return index;
    }

    private static bool TryWord(string condition, int index, out string word, out int after)
    {
        word = "";
        after = index;

        if (!char.IsLetter(condition[index]) && condition[index] != '_') return false;

        var end = index;
        while (end < condition.Length && (char.IsLetterOrDigit(condition[end]) || condition[end] == '_'))
            end++;

        word = condition[index..end];
        after = end;
        return true;
    }

    /// <summary>
    /// Translates the VB keywords that appear in conditions.
    ///
    /// Only whole words are replaced, so an identifier such as "android" keeps
    /// its "and" and a variable called "Nothing_found" is left alone.
    /// </summary>
    private static string TranslateWord(string word) => word.ToLowerInvariant() switch
    {
        "andalso" or "and" => "&&",
        "orelse" or "or" => "||",
        "not" => "!",
        "nothing" => "null",
        "true" => "true",
        "false" => "false",
        "mod" => "%",
        "is" => "==",
        "isnot" => "!=",
        _ => word
    };
}
