namespace Basalt.Workspace.Snippets;

/// <summary>
/// The snippets Visual Basic ships with, as far as they still apply.
///
/// These are the shortcuts a VB user expects to work: typing "for" and
/// pressing Tab should write the loop, not leave three lines to type by hand.
/// </summary>
public static class VbSnippets
{
    public static IReadOnlyList<CodeSnippet> All { get; } =
    [
        new("for", "For loop", "For $i$ As Integer = 0 To $count$\n    $end$\nNext")
        {
            Description = "Counted loop."
        },

        new("foreach", "For Each loop", "For Each $item$ In $collection$\n    $end$\nNext")
        {
            Description = "Loop over a collection."
        },

        new("while", "While loop", "While $condition$\n    $end$\nEnd While"),

        new("do", "Do loop", "Do\n    $end$\nLoop While $condition$"),

        new("if", "If block", "If $condition$ Then\n    $end$\nEnd If"),

        new("ifelse", "If/Else block",
            "If $condition$ Then\n    $end$\nElse\n\nEnd If"),

        new("select", "Select Case",
            "Select Case $value$\n    Case $first$\n        $end$\n    Case Else\n\nEnd Select"),

        new("try", "Try/Catch",
            "Try\n    $end$\nCatch ex As Exception\n\nEnd Try"),

        new("tryf", "Try/Catch/Finally",
            "Try\n    $end$\nCatch ex As Exception\n\nFinally\n\nEnd Try"),

        new("sub", "Sub", "Public Sub $name$()\n    $end$\nEnd Sub"),

        new("function", "Function",
            "Public Function $name$() As $type$\n    $end$\nEnd Function"),

        new("property", "Property", "Public Property $name$ As $type$"),

        new("class", "Class", "Public Class $name$\n    $end$\nEnd Class"),

        new("module", "Module", "Public Module $name$\n    $end$\nEnd Module"),

        new("interface", "Interface", "Public Interface $name$\n    $end$\nEnd Interface"),

        new("enum", "Enum", "Public Enum $name$\n    $end$\nEnd Enum"),

        new("structure", "Structure", "Public Structure $name$\n    $end$\nEnd Structure"),

        new("using", "Using block", "Using $resource$ As New $type$()\n    $end$\nEnd Using"),

        new("with", "With block", "With $target$\n    $end$\nEnd With"),

        new("ctor", "Constructor", "Public Sub New()\n    $end$\nEnd Sub"),

        new("region", "Region", "#Region \"$name$\"\n$end$\n#End Region")
    ];

    /// <summary>
    /// The snippet a shortcut names, or null when there is none.
    ///
    /// Matching ignores case, as Visual Basic does everywhere else.
    /// </summary>
    public static CodeSnippet? ByShortcut(string shortcut) =>
        All.FirstOrDefault(s =>
            s.Shortcut.Equals(shortcut, StringComparison.OrdinalIgnoreCase));

    /// <summary>Snippets whose shortcut begins with what has been typed.</summary>
    public static IReadOnlyList<CodeSnippet> Matching(string prefix) =>
        prefix.Length == 0
            ? All
            : [.. All.Where(s =>
                s.Shortcut.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
}
