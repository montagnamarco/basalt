namespace Basalt.Razor.Vb;

/// <summary>
/// A piece of a parsed .vbhtml document.
///
/// The tree is deliberately shallow: a template is a sequence of literal HTML,
/// values to write out, and blocks of Visual Basic that wrap further content.
/// Everything the generator needs to emit code is expressible in those terms.
/// </summary>
public abstract class VbHtmlNode
{
    protected VbHtmlNode(int position, int line)
    {
        Position = position;
        Line = line;
    }

    /// <summary>Offset in the source file, used to map errors back to it.</summary>
    public int Position { get; }

    /// <summary>Line in the source file, one-based.</summary>
    public int Line { get; }
}

/// <summary>Literal markup, written to the output verbatim.</summary>
public sealed class HtmlNode : VbHtmlNode
{
    public HtmlNode(string text, int position, int line) : base(position, line) => Text = text;

    public string Text { get; }
}

/// <summary>
/// An expression whose value is written out: <c>@name</c> or <c>@(a + b)</c>.
/// </summary>
public sealed class ExpressionNode : VbHtmlNode
{
    public ExpressionNode(
        string expression, bool isRaw, int position, int line, bool isAwaited = false)
        : base(position, line)
    {
        Expression = expression;
        IsRaw = isRaw;
        IsAwaited = isAwaited;
    }

    public string Expression { get; }

    /// <summary>
    /// Whether the value is written as-is rather than HTML-encoded, which is
    /// what <c>@Html.Raw(...)</c> means in Razor.
    /// </summary>
    public bool IsRaw { get; }

    /// <summary>
    /// Whether the expression must be awaited, as <c>@Await</c> asks.
    /// </summary>
    /// <remarks>
    /// The async helpers are the ones that matter in ASP.NET Core:
    /// Html.PartialAsync and anything else returning a Task. Writing the Task
    /// itself puts its type name on the page.
    /// </remarks>
    public bool IsAwaited { get; }
}

/// <summary>
/// Visual Basic statements that produce no output on their own, written inside
/// a <c>@Code ... End Code</c> block.
/// </summary>
public sealed class StatementNode : VbHtmlNode
{
    public StatementNode(
        string code, int position, int line, int? bodyLine = null, int? bodyPosition = null,
        bool isContinuation = false)
        : base(position, line)
    {
        Code = code;
        BodyLine = bodyLine ?? line;
        BodyPosition = bodyPosition ?? position;
        IsContinuation = isContinuation;
    }

    /// <summary>
    /// Whether this is a later run of statements in a @Code block, after
    /// markup written inside it, rather than the one the block opens with.
    /// </summary>
    /// <remarks>
    /// Its Position is where its own code starts, not the "@Code" keyword:
    /// what highlights, folds or outlines the keyword must skip it, and what
    /// looks for its code from Position finds this run and not the first.
    /// </remarks>
    public bool IsContinuation { get; }

    public string Code { get; }

    /// <summary>
    /// Where the code itself starts, which is not where the keyword does.
    /// </summary>
    /// <remarks>
    /// The mapping between template and generated code is anchored here. From
    /// the keyword's position instead, an entry covered "@Code" and stopped
    /// short of the body's own end: a caret past that point mapped nowhere,
    /// and hover and go-to-definition inside a Code block answered nothing.
    /// </remarks>
    public int BodyPosition { get; }

    /// <summary>
    /// The line the code itself starts on, which is not always the line the
    /// keyword is on: "@Code" usually has its body on the next line, while
    /// "@Code Dim a = 1" has it on the same one. The pragma has to name the
    /// line the code is really on, or every error in the block is reported
    /// one line high.
    /// </summary>
    public int BodyLine { get; }
}

/// <summary>
/// A control-flow construct: the opening clause, the content it governs, and
/// the keyword that closes it.
/// </summary>
public sealed class BlockNode : VbHtmlNode
{
    public BlockNode(string opening, string closing, int position, int line)
        : base(position, line)
    {
        Opening = opening;
        Closing = closing;
    }

    /// <summary>The opening clause as written, for example "If x > 0 Then".</summary>
    public string Opening { get; }

    /// <summary>The closing keyword, for example "End If" or "Next".</summary>
    /// <summary>
    /// The closing keyword, and the loop variable where one was written:
    /// "Next" or "Next i".
    /// </summary>
    public string Closing { get; internal set; }

    /// <summary>
    /// Where the closing keyword sits in the template, past its "@"; -1 when
    /// the block was never closed.
    /// </summary>
    /// <remarks>
    /// Recorded so the closing line can carry its own #ExternalSource: a
    /// breakpoint on "@Next" or "@End If" otherwise had no line to bind to.
    /// </remarks>
    public int ClosingPosition { get; internal set; } = -1;

    /// <summary>The one-based line of the closing keyword.</summary>
    public int ClosingLine { get; internal set; }

    public List<VbHtmlNode> Body { get; } = new();

    /// <summary>
    /// Continuation clauses that split the block without closing it, such as
    /// ElseIf and Else, each with the content that follows it.
    /// </summary>
    public List<BlockClause> Clauses { get; } = new();
}

/// <summary>A continuation inside a block, such as ElseIf or Case.</summary>
public sealed class BlockClause
{
    public BlockClause(string keyword, int position = -1, int line = 0)
    {
        Keyword = keyword;
        Position = position;
        Line = line;
    }

    public string Keyword { get; }

    /// <summary>
    /// Where the keyword sits in the template, past its "@"; -1 when unknown.
    /// </summary>
    /// <remarks>
    /// An ElseIf or Case carries a condition of its own, and without a
    /// position it could be neither mapped for the editor nor given a line
    /// for the debugger.
    /// </remarks>
    public int Position { get; }

    /// <summary>The one-based line of the keyword.</summary>
    public int Line { get; }

    public List<VbHtmlNode> Body { get; } = new();
}

/// <summary>A directive such as <c>@ModelType</c> or <c>@Imports</c>.</summary>
/// <summary>
/// A named piece of markup a layout will place: <c>@Section Scripts ... End
/// Section</c>.
///
/// Its own node rather than a block, because its body does not run where it
/// is written: a section declared at the top of a view often belongs at the
/// bottom of the page, and only the layout knows where.
/// </summary>
public sealed class SectionNode : VbHtmlNode
{
    public SectionNode(string name, int position, int line) : base(position, line) =>
        Name = name;

    public string Name { get; }

    public List<VbHtmlNode> Body { get; } = [];
}

/// <summary>
/// Visual Basic members a view declares for itself: <c>@Functions ... End
/// Functions</c>.
///
/// Written into the class rather than into Execute, which is the difference
/// from a code block: a method cannot be declared inside a method.
/// </summary>
public sealed class FunctionsNode : VbHtmlNode
{
    public FunctionsNode(
        string code, int position, int line, int? bodyLine = null, int? bodyPosition = null)
        : base(position, line)
    {
        Code = code;
        BodyLine = bodyLine ?? line;
        BodyPosition = bodyPosition ?? position;
    }

    public string Code { get; }

    /// <summary>Where <see cref="Code"/> begins in the template.</summary>
    /// <remarks>
    /// Past the keyword and the line break after it, as for a code block: the
    /// members are mapped line for line from here, which is what lets a
    /// breakpoint inside a method declared in @Functions bind at all.
    /// </remarks>
    public int BodyPosition { get; }

    /// <summary>The one-based line <see cref="Code"/> begins on.</summary>
    public int BodyLine { get; }
}

public sealed class DirectiveNode : VbHtmlNode
{
    public DirectiveNode(string name, string value, int position, int line)
        : base(position, line)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string Value { get; }
}

/// <summary>A whole parsed template.</summary>
public sealed class VbHtmlDocument
{
    public List<VbHtmlNode> Nodes { get; } = new();
    public List<VbHtmlDiagnostic> Diagnostics { get; } = new();

    /// <summary>Type named by <c>@ModelType</c>, if the template declares one.</summary>
    public string? ModelType { get; set; }

    /// <summary>
    /// Visual Basic members the template declared with @Functions.
    ///
    /// Kept on the document rather than among the nodes because they are
    /// written into the class, not into the body that renders the page.
    /// </summary>
    public List<string> Functions { get; } = [];

    /// <summary>
    /// A base class the template asked for with @Inherits.
    ///
    /// Nothing means the usual base. The editor's grammar always coloured
    /// this as a directive while the parser read it as an expression, so a
    /// template using it wrote "Inherits" into the page.
    /// </summary>
    public string? Inherits { get; set; }

    /// <summary>
    /// A layout the folder's _ViewStart set, where the view sets none itself.
    ///
    /// Kept apart from anything the view says, because the view wins.
    /// </summary>
    public string? DefaultLayout { get; set; }

    /// <summary>
    /// The layout the template names with @Layout "…", which wins over a
    /// _ViewStart's as it does when written in a code block.
    /// </summary>
    public string? Layout { get; set; }

    /// <summary>
    /// A namespace the template asked for with @Namespace.
    ///
    /// Overrides the one derived from the folder, which is what a project
    /// with an unusual layout needs.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Whether the template wrote @Namespace itself, rather than being given
    /// one by a _ViewImports file: its own wins over any shared one.
    /// </summary>
    public bool DeclaresNamespace { get; set; }

    /// <summary>Interfaces the view implements, from @Implements.</summary>
    public List<string> Implements { get; } = [];

    /// <summary>Attributes to put on the generated class, from @Attribute.</summary>
    public List<string> Attributes { get; } = [];

    /// <summary>
    /// @addTagHelper, @removeTagHelper and @tagHelperPrefix, in the order
    /// written, shared files first: the order decides what is in scope.
    /// </summary>
    public List<TagHelperDirective> TagHelperDirectives { get; } = [];

    /// <summary>Namespaces imported by <c>@Imports</c>.</summary>
    public List<string> Imports { get; } = new();

    /// <summary>
    /// Services the view asks for with <c>@Inject</c>, name and type.
    /// </summary>
    public List<InjectedService> Injected { get; } = [];

    /// <summary>
    /// The route the page answers on, from <c>@Page</c>, when it has one.
    /// </summary>
    /// <remarks>
    /// Empty string for a bare "@Page", which takes its route from the file's
    /// own path; null when the directive is absent, which means this is an
    /// MVC view rather than a Razor Page.
    /// </remarks>
    public string? PageRoute { get; set; }

    /// <summary>
    /// The path ASP.NET Core keys the page on, such as /Pages/Index.cshtml.
    /// </summary>
    /// <remarks>
    /// Set by the generator, which alone knows where the project root is.
    /// </remarks>
    public string? PageIdentifier { get; set; }
}

/// <summary>A service a view asked for with <c>@Inject</c>.</summary>
public sealed record InjectedService(string Type, string Name);

/// <summary>A problem found while parsing, reported against the source file.</summary>
public sealed class VbHtmlDiagnostic
{
    public VbHtmlDiagnostic(string id, string message, int line, int column)
    {
        Id = id;
        Message = message;
        Line = line;
        Column = column;
    }

    public string Id { get; }
    public string Message { get; }
    public int Line { get; }
    public int Column { get; }
}

/// <summary>One of the directives that decide which tag helpers apply.</summary>
/// <param name="Kind">"addTagHelper", "removeTagHelper" or "tagHelperPrefix".</param>
/// <param name="Value">What follows it, unquoted: "*, Microsoft.AspNetCore.Mvc.TagHelpers".</param>
public sealed record TagHelperDirective(string Kind, string Value)
{
    /// <summary>Whether it came from a _ViewImports file rather than the view itself.</summary>
    public bool IsInherited { get; init; }
}
