namespace Basalt.Extensibility;

/// <summary>
/// One piece of a symbol's description, and what kind of thing it is.
///
/// The pieces carry their meaning rather than their appearance: a tooltip
/// decides that a parameter name is bold, and it decides the same way for
/// every language. A language that hands over pre-formatted text would have
/// to agree with every other on how bold looks.
/// </summary>
public enum SymbolPartKind
{
    /// <summary>Punctuation, spacing, anything with no meaning of its own.</summary>
    Plain,

    /// <summary>A language keyword: Public, Function, As.</summary>
    Keyword,

    /// <summary>The name of the thing being described.</summary>
    Name,

    /// <summary>A type name.</summary>
    Type,

    /// <summary>A parameter's name.</summary>
    ParameterName
}

/// <summary>A run of a symbol's description.</summary>
public sealed record SymbolPart(string Text, SymbolPartKind Kind = SymbolPartKind.Plain)
{
    public static SymbolPart Plain(string text) => new(text);
    public static SymbolPart Keyword(string text) => new(text, SymbolPartKind.Keyword);
    public static SymbolPart Name(string text) => new(text, SymbolPartKind.Name);
    public static SymbolPart Type(string text) => new(text, SymbolPartKind.Type);

    public static SymbolPart Parameter(string text) =>
        new(text, SymbolPartKind.ParameterName);
}

/// <summary>One parameter of a call.</summary>
public sealed record ParameterDescription(string Name, string? Type = null)
{
    /// <summary>What this parameter is for, when the author said.</summary>
    public string? Documentation { get; init; }

    /// <summary>The parameter as it reads in a list: "name As String".</summary>
    public string Display => Type is { Length: > 0 } type ? $"{Name} As {type}" : Name;
}

/// <summary>
/// What the IDE can say about a symbol, in a form no language owns.
///
/// The same description serves the tooltip shown by resting the pointer on a
/// name and the help shown while writing a call: they are the same question
/// asked two ways, and answering them separately made two things to keep in
/// step. Visual Basic fills this from Roslyn; QuickBASIC from its table of
/// intrinsics; a dialect added later fills it from whatever it has.
/// </summary>
public sealed record SymbolDescription(IReadOnlyList<SymbolPart> Signature)
{
    /// <summary>The parameters, when the symbol takes any.</summary>
    public IReadOnlyList<ParameterDescription> Parameters { get; init; } = [];

    /// <summary>
    /// Which parameter is being written, counted from zero.
    ///
    /// -1 when the question was not about a call in progress. A tooltip shows
    /// this one in bold, which is what tells the reader where they are in a
    /// call of five arguments.
    /// </summary>
    public int ActiveParameter { get; init; } = -1;

    /// <summary>What the symbol is for, from its documentation comment.</summary>
    public string? Documentation { get; init; }

    /// <summary>What the symbol is: a method, a variable, a type.</summary>
    public SymbolKind Kind { get; init; } = SymbolKind.Unknown;

    /// <summary>
    /// The signature as plain text.
    ///
    /// For a status bar, a log line, or a test that does not care about the
    /// parts.
    /// </summary>
    public string PlainSignature => string.Concat(Signature.Select(p => p.Text));

    /// <summary>One line of description with no parts marked up.</summary>
    public static SymbolDescription FromText(string signature, string? documentation = null) =>
        new([SymbolPart.Plain(signature)]) { Documentation = documentation };
}

/// <summary>
/// The overloads a call has, and which one is being written.
///
/// Separate from a single description because a call can mean several things
/// until enough arguments are typed, and the reader has to be able to look
/// through them.
/// </summary>
public sealed record SymbolDescriptionSet(IReadOnlyList<SymbolDescription> Overloads)
{
    /// <summary>Which overload best fits what has been typed so far.</summary>
    public int Active { get; init; }

    public static SymbolDescriptionSet One(SymbolDescription description) =>
        new([description]);
}
