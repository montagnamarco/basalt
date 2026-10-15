using Basalt.Core.Model;

namespace Basalt.Workspace.Completion;

/// <summary>
/// The types a Visual Basic programmer reaches for first.
///
/// Where a type is wanted, after "As" or "Of", these rank above the rest of
/// the types, which the list would otherwise sort alphabetically: "Dim x As "
/// offers Integer and String at the top, not AbandonedMutexException.
/// </summary>
public static class CommonTypes
{
    private static readonly string[] VisualBasicOrder =
    [
        "Integer", "String", "Boolean", "Double", "Long", "Date", "Object",
        "Decimal", "Char", "Byte", "Short", "Single",
    ];

    /// <summary>
    /// The rank of a name in a type position: higher first, and zero for any
    /// name that is not common, or for anything that is not a type.
    /// </summary>
    public static int RankFor(string name, CompletionKind kind)
    {
        // Roslyn offers the Visual Basic type names as keywords ("Integer", "String"),
        // and the other types under their own kinds.
        if (kind is not (CompletionKind.Keyword or CompletionKind.Class or CompletionKind.Structure or CompletionKind.Interface or CompletionKind.Enum))
            return 0;

        var index = Array.FindIndex(VisualBasicOrder,
            common => string.Equals(common, name, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? 0 : VisualBasicOrder.Length - index;
    }
}
