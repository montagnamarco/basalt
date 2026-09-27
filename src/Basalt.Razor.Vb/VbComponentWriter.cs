using System.Text;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Basalt.Razor.Vb;

/// <summary>
/// Turns a template into a Blazor component.
/// </summary>
/// <remarks>
/// The same parse tree every other host is written from, emitted differently:
/// a component does not write HTML out as text. It builds a render tree —
/// OpenElement, AddContent, CloseElement — and Blazor diffs one tree against
/// the previous one to decide what to change on screen. A component that wrote
/// markup as text would compile, render nothing, and say nothing about why.
///
/// Every call takes a sequence number, and Blazor's diffing algorithm depends
/// on those numbers being stable between renders rather than on what they
/// count. They are assigned in source order here, which is what the C#
/// compiler does too: numbers derived from a loop counter would change shape
/// between renders and defeat the diff.
///
/// Server, WebAssembly and Auto need nothing different from each other — they
/// differ in where the component runs, not in what is generated for it.
/// </remarks>
public sealed class VbComponentWriter
{
    // One writer per template: the state below belongs to one run. It used
    // to be [ThreadStatic] static state, reset by hand at the start of every
    // run, which each new piece of state had to remember to join.
    private VbComponentWriter(IComponentCatalog? catalog)
    {
        catalog_ = catalog;
    }

    /// <summary>What the build knows about the components this one uses, when it knows.</summary>
    private readonly IComponentCatalog? catalog_;

    /// <summary>What an open tag is, so its closing tag knows which call to make.</summary>
    private enum FrameKind
    {
        Element,
        Component,

        /// <summary>A RenderFragment parameter written as an element: &lt;Header&gt; in &lt;Card&gt;.</summary>
        Fragment,
    }

    /// <summary>An open tag.</summary>
    private sealed class Frame(FrameKind kind)
    {
        public FrameKind Kind { get; } = kind;

        /// <summary>A component's parameters, when the build knows them.</summary>
        public ComponentShape? Shape { get; init; }

        /// <summary>Type arguments the tag wrote: (Of Person).</summary>
        public IReadOnlyList<string> TypeArguments { get; init; } = [];

        /// <summary>The name its content's context value takes: Context="item".</summary>
        public string? Context { get; init; }

        /// <summary>A component's frames that follow its content.</summary>
        public DeferredFrames? Deferred { get; init; }

        /// <summary>What closes the lambda open for its content, while one is.</summary>
        public string? ContentClose { get; set; }
    }

    /// <summary>The tags open where the writer is.</summary>
    private readonly Stack<Frame> open_ = new();

    /// <summary>How many content lambdas the calls being written are inside.</summary>
    private int lambdas_;

    /// <summary>
    /// The builder the calls being written go to.
    /// </summary>
    /// <remarks>
    /// Inside a ChildContent lambda that is the lambda's own parameter, not
    /// the component's: writing to the outer one put the children in the
    /// parent's tree, where the component never looked for them — the box
    /// rendered empty and the content simply disappeared.
    /// </remarks>
    private string Builder => lambdas_ > 0 ? ChildBuilder(lambdas_) : "__builder";

    /// <summary>
    /// The builder parameter of the child-content lambda at a depth: one name
    /// per depth, since Visual Basic refuses a lambda parameter that hides an
    /// outer one (BC36641), and a component with content inside another did
    /// not compile.
    /// </summary>
    private static string ChildBuilder(int depth) => $"__child{depth}";


    /// <summary>The class every component inherits.</summary>
    public const string ComponentBaseTypeName =
        "Global.Microsoft.AspNetCore.Components.ComponentBase";

    /// <summary>The generated class and where each piece came from.</summary>
    public sealed record Generated(string Code, SourceMap Map)
    {
        public override string ToString() => Code;
    }

    /// <summary>
    /// Writes the component class for a parsed template.
    /// </summary>
    public static string Write(
        VbHtmlDocument document,
        string className,
        string namespaceName,
        string? route = null) =>
        WriteWithMap(document, className, namespaceName, filePath: null, route).Code;

    /// <summary>
    /// Writes the class, recording where each fragment of Visual Basic came
    /// from in the template.
    /// </summary>
    /// <remarks>
    /// The mappings are what the editor runs on: a caret in a component is
    /// moved through them into the generated code, asked about there, and the
    /// answer moved back. Without them a component could be coloured — that
    /// comes from the parser — and could answer nothing else, which reads as
    /// an editor that half works.
    /// </remarks>
    public static Generated WriteWithMap(
        VbHtmlDocument document,
        string className,
        string namespaceName,
        string? filePath,
        string? route = null,
        string? checksum = null,
        bool optionStrict = false,
        IComponentCatalog? catalog = null) =>
        new VbComponentWriter(catalog).Generate(document, className, namespaceName, filePath, route, checksum, optionStrict);

    /// <summary>
    /// The component's class as declarations only — base type, interfaces,
    /// injected services and the members its @Functions and @Code blocks
    /// declare — with no render tree.
    /// </summary>
    /// <remarks>
    /// What the generator compiles first to learn every component's
    /// parameters before any render tree is written, the way the C# compiler
    /// declares components before it binds their bodies. <paramref name="lookupAt"/>
    /// is an offset inside the class, from which a tag's name is looked up
    /// with the template's imports and namespace in scope.
    /// </remarks>
    public static string DeclarationStub(
        VbHtmlDocument document, string className, string namespaceName, out int lookupAt)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(document.Namespace)) namespaceName = document.Namespace!.Trim();

        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        VbHtmlCodeWriter.WriteImport(builder, imported, "Microsoft.AspNetCore.Components.Web");
        VbHtmlCodeWriter.WriteImport(builder, imported, "Microsoft.AspNetCore.Components.Web.RenderMode");

        foreach (var import in document.Imports)
            VbHtmlCodeWriter.WriteImport(builder, imported, import);

        var hasNamespace = !string.IsNullOrWhiteSpace(namespaceName);

        if (hasNamespace) builder.AppendLine($"Namespace {namespaceName}");

        var baseType = string.IsNullOrWhiteSpace(document.Inherits)
            ? ComponentBaseTypeName
            : VbHtmlCodeWriter.Qualify(document.Inherits!);

        builder.AppendLine($"    Partial Public Class {ViewNaming.Escape(className)}");
        builder.AppendLine($"        Inherits {baseType}");

        foreach (var contract in document.Implements)
            builder.AppendLine($"        Implements {contract}");

        lookupAt = builder.Length;
        builder.AppendLine();

        foreach (var injected in document.Injected)
        {
            builder.AppendLine("        <Global.Microsoft.AspNetCore.Components.Inject>");
            builder.AppendLine($"        Protected Property {injected.Name} As {VbHtmlCodeWriter.Qualify(injected.Type)}");
        }

        foreach (var functions in VbHtmlCodeWriter.FunctionsIn(document.Nodes))
            builder.AppendLine(functions.Code);

        var members = MemberBlocks(document.Nodes);

        foreach (var block in document.Nodes.OfType<StatementNode>().Where(members.Contains))
            builder.AppendLine(block.Code);

        builder.AppendLine("    End Class");

        if (hasNamespace) builder.AppendLine("End Namespace");

        return builder.ToString();
    }

    private Generated Generate(
        VbHtmlDocument document,
        string className,
        string namespaceName,
        string? filePath,
        string? route,
        string? checksum,
        bool optionStrict)
    {
        var builder = new StringBuilder();
        var mappings = new List<SourceMapping>();
        var sequence = 0;

        // @Namespace names the component's namespace itself, as in C#.
        if (!string.IsNullOrWhiteSpace(document.Namespace)) namespaceName = document.Namespace!.Trim();

        builder.AppendLine("' <auto-generated/>");
        // The project's own setting, as for a view.
        builder.AppendLine(optionStrict ? "Option Strict On" : "Option Strict Off");
        builder.AppendLine("Option Explicit On");
        builder.AppendLine();

        // Each namespace once: _Imports.vbrazor naming Components.Web, as the
        // C# template's does, repeated the one written here (BC31051).
        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The web namespace, for the event argument types a handler names.
        VbHtmlCodeWriter.WriteImport(builder, imported, "Microsoft.AspNetCore.Components.Web");

        // The render modes by their bare names, as the Blazor template's
        // "@using static ...RenderMode" gives them to C#: @rendermode
        // InteractiveServer, and @rendermode="InteractiveAuto" on an element.
        VbHtmlCodeWriter.WriteImport(builder, imported, "Microsoft.AspNetCore.Components.Web.RenderMode");

        builder.AppendLine();

        foreach (var import in document.Imports)
            VbHtmlCodeWriter.WriteImport(builder, imported, import);

        if (document.Imports.Count > 0) builder.AppendLine();

        // SHA-256 of the template as read from disk, when the caller has it.
        ExternalSourceWriter.WriteChecksum(builder, filePath, checksum);

        // None for a component beside the project file: it belongs to the
        // root namespace, which Visual Basic supplies itself.
        var hasNamespace = !string.IsNullOrWhiteSpace(namespaceName);

        if (hasNamespace)
        {
            builder.AppendLine($"Namespace {namespaceName}");
            builder.AppendLine();
        }

        // The route the template declared with @Page, unless the caller named
        // one. A component without either is only reachable by being placed
        // inside another, which is not an error.
        route ??= document.PageRoute;

        if (!string.IsNullOrWhiteSpace(route))
            builder.AppendLine($"    <Global.Microsoft.AspNetCore.Components.Route(\"{route}\")>");

        // @Layout "MainLayout": a component's layout is a type, given through
        // Blazor's attribute. It was parsed and then ignored.
        if (!string.IsNullOrWhiteSpace(document.Layout))
        {
            builder.AppendLine(
                $"    <Global.Microsoft.AspNetCore.Components.Layout(GetType({document.Layout}))>");
        }

        // @Attribute, as on a C# component: <StreamRendering>, <Authorize>.
        foreach (var attribute in document.Attributes)
            builder.AppendLine($"    <{attribute.Trim('<', '>')}>");

        if (!string.IsNullOrWhiteSpace(document.RenderMode))
            builder.AppendLine($"    <{ViewNaming.Escape(className)}.{RenderModeAttributeName}>");

        var baseType = string.IsNullOrWhiteSpace(document.Inherits)
            ? ComponentBaseTypeName
            : VbHtmlCodeWriter.Qualify(document.Inherits!);

        // Partial, so a code-behind Counter.vbrazor.vb declaring Partial Class
        // Counter adds to the same class, the way Counter.razor.cs does.
        builder.AppendLine($"    Partial Public Class {ViewNaming.Escape(className)}");
        builder.AppendLine($"        Inherits {baseType}");

        foreach (var contract in document.Implements)
            builder.AppendLine($"        Implements {contract}");

        builder.AppendLine();

        // Injected services are properties, not constructor arguments: Blazor
        // creates a component with the parameterless constructor and fills
        // these in afterwards.
        foreach (var injected in document.Injected)
        {
            builder.AppendLine("        <Global.Microsoft.AspNetCore.Components.Inject>");
            builder.AppendLine(
                $"        Protected Property {injected.Name} As {VbHtmlCodeWriter.Qualify(injected.Type)}");
            builder.AppendLine();
        }

        builder.AppendLine(
            "        Protected Overrides Sub BuildRenderTree(" +
            "__builder As Global.Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder)");

        // A @Code block at the top of a component that declares members —
        // Private count As Integer, Sub Increment() — is written into the
        // class, as @code is in a .razor. One that runs statements (Dim x = ...,
        // For Each around markup, a one-line @If) stays in BuildRenderTree:
        // moved into the class it no longer compiled, and a Dim became a
        // field evaluated before the parameters were set.
        var members = MemberBlocks(document.Nodes);
        var markup = document.Nodes.Where(node => node is not StatementNode statement || !members.Contains(statement)).ToList();

        WriteNodes(builder, markup, ref sequence, indent: 12, mappings, filePath);

        builder.AppendLine("        End Sub");

        // @functions and @code both land here: methods, fields and properties
        // on the component itself.
        //
        // Mapped line for line, like a view's @Functions, so a breakpoint in
        // an event handler declared here binds to the template.
        foreach (var functions in VbHtmlCodeWriter.FunctionsIn(document.Nodes))
        {
            builder.AppendLine();

            ExternalSourceWriter.WriteMapped(builder, mappings, filePath,
                functions.BodyPosition, functions.Code.Length, functions.BodyLine, () =>
                {
                    foreach (var line in ExternalSourceWriter.LinesOf(functions.Code))
                        builder.AppendLine($"        {line}");
                });
        }

        foreach (var block in document.Nodes.OfType<StatementNode>().Where(members.Contains))
        {
            builder.AppendLine();

            ExternalSourceWriter.WriteMapped(builder, mappings, filePath,
                block.BodyPosition, block.Code.Length, block.BodyLine, () =>
                {
                    foreach (var line in ExternalSourceWriter.LinesOf(block.Code))
                        builder.AppendLine($"        {line}");
                });
        }

        WriteBindExpressionHelper(builder);

        WriteRenderMode(builder, mappings, filePath, document);

        builder.AppendLine("    End Class");

        if (hasNamespace) builder.AppendLine("End Namespace");

        return new Generated(builder.ToString(), new SourceMap(mappings));
    }

    /// <summary>
    /// Writes a run of nodes, joining the ones that belong together.
    /// </summary>
    /// <remarks>
    /// A tag whose attributes hold expressions arrives in pieces — the markup
    /// up to name=", the expression, the markup from the closing quote on —
    /// because the parser splits on the @ before the tag is ever whole. The
    /// pieces are put back together into one tag before anything is written,
    /// so each attribute is read with the others around it: a binding with its
    /// modifiers, a value mixing text and expressions, the end of the tag
    /// wherever it falls. Written a piece at a time, the tag came out as text
    /// with the expression stranded in the middle, or with its child content
    /// opened before its last attribute.
    /// </remarks>
    private void WriteNodes(
        StringBuilder builder, IReadOnlyList<VbHtmlNode> nodes, ref int sequence, int indent,
        List<SourceMapping> mappings, string? filePath)
    {
        var pad = new string(' ', indent);

        // Runs of markup are joined first. The parser ends a node wherever it
        // saw an "@", so "<input @bind=" arrives as two markup nodes and the
        // tag cannot be read as a whole.
        nodes = JoinMarkup(nodes);

        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is HtmlNode html &&
                i + 1 < nodes.Count &&
                nodes[i + 1] is ExpressionNode or HtmlNode &&
                OpenTagAt(html.Text) is { } tagStart &&
                AssembleTag(nodes, i, tagStart) is { } assembled)
            {
                WriteMarkup(builder, html.Text.Substring(0, tagStart), html.Position, html.Line,
                    ref sequence, pad, mappings, filePath);

                WriteTag(builder, assembled.Tag, ref sequence, pad, mappings, filePath);

                // What follows the tag in its last piece is ordinary markup,
                // written on the next pass.
                nodes = Replace(nodes, assembled.LastIndex, assembled.Rest);
                i = assembled.LastIndex - 1;
                continue;
            }

            WriteNode(builder, nodes[i], ref sequence, indent, mappings, filePath);
        }
    }

    /// <summary>
    /// Where a run of markup opens a tag it does not finish, if it does.
    /// </summary>
    private static int? OpenTagAt(string markup)
    {
        for (var at = markup.LastIndexOf('<'); at >= 0; at = at == 0 ? -1 : markup.LastIndexOf('<', at - 1))
        {
            if (at + 1 >= markup.Length || !char.IsLetter(markup[at + 1])) continue;

            return TagEnd(markup.Substring(at)) < 0 ? at : null;
        }

        return null;
    }

    /// <summary>A tag put back together, and the markup its last piece goes on with.</summary>
    private sealed record AssembledTag(Tag Tag, int LastIndex, HtmlNode Rest);

    /// <summary>
    /// Joins the pieces of a tag, from the markup that opens it to the markup
    /// that ends it, with the expressions between.
    /// </summary>
    /// <returns>
    /// Nothing when a node other than markup or an expression comes first: a
    /// tag cut by a code block is written as it always was.
    /// </returns>
    private static AssembledTag? AssembleTag(IReadOnlyList<VbHtmlNode> nodes, int first, int tagStart)
    {
        var opening = (HtmlNode)nodes[first];
        var tag = new Tag();

        tag.AddLiteral(opening.Text.Substring(tagStart), opening.Position + tagStart, LineAt(opening, tagStart));

        for (var index = first + 1; index < nodes.Count; index++)
        {
            if (nodes[index] is ExpressionNode expression)
            {
                tag.AddExpression(expression);
                continue;
            }

            if (nodes[index] is not HtmlNode piece) return null;

            var before = tag.Length;

            tag.AddLiteral(piece.Text, piece.Position, piece.Line);

            var end = TagEnd(tag.Text);

            if (end < 0) continue;

            var used = end + 1 - before;

            tag.Truncate(end + 1);

            var rest = new HtmlNode(piece.Text.Substring(used), piece.Position + used, LineAt(piece, used));

            return new AssembledTag(tag, index, rest);
        }

        return null;
    }

    /// <summary>The line an offset into a markup node falls on.</summary>
    private static int LineAt(HtmlNode node, int offset) => LineAt(node.Text, node.Line, offset);

    private static int LineAt(string text, int firstLine, int offset)
    {
        var line = firstLine;

        for (var index = 0; index < offset && index < text.Length; index++)
        {
            if (text[index] == '\n') line++;
        }

        return line;
    }

    /// <summary>
    /// Where the tag being read ends: the first ">" outside a quoted value,
    /// or -1 when the text stops inside the tag.
    /// </summary>
    private static int TagEnd(string text)
    {
        char? quote = null;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (quote is not null)
            {
                if (current == quote) quote = null;
                continue;
            }

            if (current is '"' or '\'') quote = current;
            else if (current == '>') return index;
        }

        return -1;
    }

    /// <summary>
    /// One tag's text, its expressions held as placeholders, and where each
    /// literal character came from in the template.
    /// </summary>
    private sealed class Tag
    {
        private readonly StringBuilder _text = new();

        private readonly List<(int Start, int Length, int Position, int Line, string Text)> _literals = [];

        public List<ExpressionNode> Expressions { get; } = [];

        public string Text => _text.ToString();

        public int Length => _text.Length;

        public void AddLiteral(string text, int position, int line)
        {
            _literals.Add((_text.Length, text.Length, position, line, text));
            _text.Append(text);
        }

        public void AddExpression(ExpressionNode expression)
        {
            _text.Append(Placeholder(Expressions.Count));
            Expressions.Add(expression);
        }

        public void Truncate(int length) => _text.Length = length;

        /// <summary>Stands for the expression at an index; no markup contains it.</summary>
        public static string Placeholder(int index) => "\u0001" + index + "\u0002";

        /// <summary>Where a literal character of the tag sits in the template.</summary>
        public (int Position, int Line)? Origin(int index)
        {
            foreach (var literal in _literals)
            {
                if (index < literal.Start || index >= literal.Start + literal.Length) continue;

                var offset = index - literal.Start;

                return (literal.Position + offset, LineAt(literal.Text, literal.Line, offset));
            }

            return null;
        }
    }

    /// <summary>One attribute as written, its value still with placeholders.</summary>
    /// <param name="Value">Null for a bare attribute: disabled, @onclick:preventDefault.</param>
    /// <param name="ValueStart">Where the value starts in the tag's text.</param>
    private sealed record TagAttribute(string Name, string? Value, int ValueStart);

    /// <summary>
    /// An attribute's value, ready to be written: Visual Basic, with where it
    /// came from when it is the template's own code.
    /// </summary>
    /// <param name="Lead">Written before the mapped code and not mapped: "Await ".</param>
    private sealed record AttributeValue(string Written, bool IsCode, bool IsMapped, int Position, int Line, string Lead = "")
    {
        public static AttributeValue Literal(string written) => new(written, false, false, 0, 0);

        public static AttributeValue Code(string code) => new(code, true, false, 0, 0);
    }

    /// <summary>
    /// Opens the element or component a tag names and writes its attributes.
    /// </summary>
    private void WriteTag(
        StringBuilder builder, Tag tag, ref int sequence, string pad,
        List<SourceMapping> mappings, string? filePath)
    {
        var text = tag.Text;
        var inner = text.Substring(1, text.Length - 2);
        var trimmed = inner.TrimEnd();
        var selfClosing = trimmed.EndsWith("/", StringComparison.Ordinal);
        var name = Name(inner);
        var attributesEnd = 1 + (selfClosing ? trimmed.Length - 1 : inner.Length);
        var attributes = ParseAttributes(text, 1 + name.Length, attributesEnd);

        // <Header> inside <Card>, when Card has a RenderFragment parameter
        // called Header: that parameter's content, not a component.
        if (FragmentParameter(name) is { } fragment)
        {
            OpenFragment(builder, fragment, attributes, selfClosing, ref sequence, pad);
            return;
        }

        BeforeContent(builder, pad, ref sequence, whitespaceOnly: false);

        // A capitalised tag is another component, the way it is in Razor:
        // <Greeting Name="x" /> written out as an element sent the browser an
        // invented tag and dropped the parameter on the floor.
        var isComponent = IsComponentName(name);
        var (baseName, typeArguments) = SplitTypeArguments(name);
        var shape = isComponent ? catalog_?.Find(baseName, typeArguments.Count) : null;

        // A component is named unqualified, so Visual Basic resolves it the way
        // it resolves any name in the file's own namespace, RootNamespace
        // included — which the generator deliberately does not write.
        builder.AppendLine(isComponent
            ? $"{pad}{Builder}.OpenComponent(Of {ComponentType(name)})({sequence++})"
            : $"{pad}{Builder}.OpenElement({sequence++}, \"{name}\")");

        var deferred = WriteTagAttributes(builder, tag, attributes, name, isComponent, shape, typeArguments,
            ref sequence, pad, mappings, filePath);

        if (!isComponent)
        {
            // An element's reference and key come before its children, which
            // are frames of their own rather than an attribute.
            WriteDeferred(builder, deferred, ref sequence, pad, mappings, filePath);

            // A void element has no closing tag to wait for: left open, every
            // later sibling nested inside the <input>.
            if (selfClosing || IsVoid(name))
                builder.AppendLine($"{pad}{Builder}.CloseElement()");
            else
                open_.Push(new Frame(FrameKind.Element));

            return;
        }

        if (selfClosing)
        {
            WriteDeferred(builder, deferred, ref sequence, pad, mappings, filePath);
            builder.AppendLine($"{pad}{Builder}.CloseComponent()");
            return;
        }

        // What sits between the tags becomes ChildContent, or the named
        // RenderFragment parameters it holds; which one is known only once the
        // content starts (see BeforeContent). The deferred frames wait for the
        // closing tag: Blazor takes an attribute only straight after the
        // component's frame or another attribute, and <Panel @ref="p">text</Panel>
        // failed to render with them before ChildContent.
        open_.Push(new Frame(FrameKind.Component)
        {
            Shape = shape,
            TypeArguments = typeArguments,
            Context = attributes.FirstOrDefault(a => a.Name == "Context")?.Value,
            Deferred = deferred,
        });
    }

    /// <summary>
    /// Called before anything is written into the content of a component: the
    /// first real content opens its ChildContent lambda. White space before
    /// it, or between named fragment parameters, is dropped.
    /// </summary>
    /// <returns>Whether to go on writing what was about to be written.</returns>
    private bool BeforeContent(StringBuilder builder, string pad, ref int sequence, bool whitespaceOnly)
    {
        if (open_.Count == 0 || open_.Peek() is not { Kind: FrameKind.Component, ContentClose: null } component)
            return true;

        if (whitespaceOnly) return false;

        var parameter = component.Shape?.Parameter("ChildContent");
        var (open, close) = FragmentLambda(parameter, component, component.Context);

        builder.AppendLine($"{pad}{Builder}.AddAttribute({sequence++}, \"ChildContent\", {open}");

        component.ContentClose = close;
        lambdas_++;

        return true;
    }

    /// <summary>
    /// The RenderFragment parameter a tag directly inside a component names,
    /// when the build knows that component's parameters.
    /// </summary>
    private ComponentParameter? FragmentParameter(string tagName)
    {
        if (open_.Count == 0 || open_.Peek() is not { Kind: FrameKind.Component, ContentClose: null } component)
            return null;

        // Named exactly, case and all, as C# matches it: <header> inside a
        // component with a Header parameter is still an HTML element, and
        // read as the parameter it lost its attributes.
        return component.Shape?.Parameter(tagName) is { Kind: ParameterKind.RenderFragment or ParameterKind.RenderFragmentOf } parameter &&
               string.Equals(parameter.Name, tagName, StringComparison.Ordinal)
            ? parameter
            : null;
    }

    /// <summary>Opens a named RenderFragment parameter for the content that follows.</summary>
    private void OpenFragment(
        StringBuilder builder, ComponentParameter parameter, List<TagAttribute> attributes, bool selfClosing,
        ref int sequence, string pad)
    {
        var component = open_.Peek();
        var context = attributes.FirstOrDefault(a => a.Name == "Context")?.Value ?? component.Context;
        var (open, close) = FragmentLambda(parameter, component, context);

        builder.AppendLine($"{pad}{Builder}.AddAttribute({sequence++}, \"{parameter.Name}\", {open}");

        if (selfClosing)
        {
            builder.AppendLine($"{pad}{close}");
            return;
        }

        open_.Push(new Frame(FrameKind.Fragment) { ContentClose = close });
        lambdas_++;
    }

    /// <summary>
    /// The opening and closing text of a content lambda: a RenderFragment, or
    /// a RenderFragment(Of T) whose value the content calls context, or what
    /// Context="item" names it.
    /// </summary>
    private (string Open, string Close) FragmentLambda(ComponentParameter? parameter, Frame component, string? context)
    {
        const string Fragment = "Global.Microsoft.AspNetCore.Components.RenderFragment";
        const string TreeBuilder = "Global.Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder";

        var child = ChildBuilder(lambdas_ + 1);

        if (parameter is { Kind: ParameterKind.RenderFragmentOf, ArgumentType: { } argument })
        {
            var type = Substitute(argument, component);
            // A name, or context: Context="@x" held an expression, which a
            // lambda parameter cannot be.
            var name = context is { } written && IsIdentifier(written.Trim()) ? written.Trim() : "context";

            return ($"CType(Function({name} As {type}) Sub({child} As {TreeBuilder})",
                $"End Sub, {Fragment}(Of {type})))");
        }

        return ($"CType(Sub({child} As {TreeBuilder})", $"End Sub, {Fragment}))");
    }

    /// <summary>
    /// A parameter type with the component's type parameters replaced by the
    /// arguments the tag wrote: RenderFragment(Of TItem) in &lt;Grid(Of Person)&gt;
    /// takes a Person.
    /// </summary>
    private static string Substitute(string type, Frame component)
    {
        if (component.Shape is not { } shape || shape.TypeParameters.Count == 0) return type;

        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < shape.TypeParameters.Count && index < component.TypeArguments.Count; index++)
            arguments[shape.TypeParameters[index]] = component.TypeArguments[index];

        // All at once: one after another, <Grid(Of TValue, TKey)> in a generic
        // parent turned TKey into TValue and then both into TKey.
        return System.Text.RegularExpressions.Regex.Replace(type, @"\b\w+\b",
            match => arguments.TryGetValue(match.Value, out var argument) ? argument : match.Value);
    }

    private static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_') && text.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>A tag's name and the type arguments it wrote: Grid(Of Person, Integer).</summary>
    private static (string Name, IReadOnlyList<string> TypeArguments) SplitTypeArguments(string name)
    {
        var open = name.IndexOf("(Of ", StringComparison.OrdinalIgnoreCase);

        if (open < 0 || !name.EndsWith(")", StringComparison.Ordinal)) return (name, []);

        var arguments = new List<string>();
        var depth = 0;
        var start = open + 4;

        for (var index = start; index < name.Length - 1; index++)
        {
            if (name[index] == '(') depth++;
            else if (name[index] == ')') depth--;
            else if (name[index] == ',' && depth == 0)
            {
                arguments.Add(name.Substring(start, index - start).Trim());
                start = index + 1;
            }
        }

        arguments.Add(name.Substring(start, name.Length - 1 - start).Trim());

        return (name.Substring(0, open), arguments);
    }


    /// <summary>The attributes between a tag's name and its end.</summary>
    /// <remarks>
    /// Read a name at a time: "disabled class=..." split at the first "="
    /// was one attribute called "disabled class".
    /// </remarks>
    private static List<TagAttribute> ParseAttributes(string text, int from, int to)
    {
        var found = new List<TagAttribute>();
        var at = from;

        while (at < to)
        {
            while (at < to && char.IsWhiteSpace(text[at])) at++;

            if (at >= to) break;

            var nameStart = at;

            while (at < to && !char.IsWhiteSpace(text[at]) && text[at] != '=') at++;

            var name = text.Substring(nameStart, at - nameStart);
            var afterName = at;

            while (at < to && char.IsWhiteSpace(text[at])) at++;

            if (at >= to || text[at] != '=')
            {
                found.Add(new TagAttribute(name, null, -1));
                at = afterName;
                continue;
            }

            at++;

            while (at < to && char.IsWhiteSpace(text[at])) at++;

            if (at >= to)
            {
                found.Add(new TagAttribute(name, "", at));
                break;
            }

            if (text[at] is '"' or '\'')
            {
                var quote = text[at];
                var end = text.IndexOf(quote, at + 1);

                if (end < 0 || end > to) end = to;

                found.Add(new TagAttribute(name, text.Substring(at + 1, end - at - 1), at + 1));
                at = end + 1;
            }
            else
            {
                var start = at;

                while (at < to && !char.IsWhiteSpace(text[at])) at++;

                found.Add(new TagAttribute(name, text.Substring(start, at - start), start));
            }
        }

        return found;
    }

    /// <summary>
    /// What an attribute's value is: text, the template's own Visual Basic,
    /// or text and expressions together.
    /// </summary>
    /// <param name="isCode">
    /// Whether the value is Visual Basic even without an "@": the value of
    /// @onclick, @bind, @ref and the rest is code, as it is in C#.
    /// </param>
    private static AttributeValue ValueOf(Tag tag, TagAttribute attribute, bool isCode)
    {
        if (attribute.Value is null) return AttributeValue.Code("True");

        var value = attribute.Value;
        var leading = value.Length - value.TrimStart().Length;
        var body = value.Trim();

        // Exactly one expression: the value is that expression, mapped where
        // the parser found it.
        if (tag.Expressions.Count > 0 && body == Tag.Placeholder(IndexOfPlaceholder(body)))
        {
            var expression = tag.Expressions[IndexOfPlaceholder(body)];

            return new AttributeValue(expression.Expression, true, true, expression.ExpressionPosition, expression.Line,
                expression.IsAwaited ? "Await " : "");
        }

        if (body.IndexOf('\u0001') >= 0) return Mixed(tag, value);

        // "@Handler" written into a literal value, or the value of a
        // directive attribute: the template's own code.
        var marked = body.StartsWith("@", StringComparison.Ordinal);

        if (!marked && !isCode) return AttributeValue.Literal(Quoted(value));

        var skip = leading + (marked ? 1 : 0);
        var remainder = value.Substring(skip);
        var code = remainder.Trim();

        skip += remainder.Length - remainder.TrimStart().Length;

        if (code.Length == 0) return AttributeValue.Literal(Quoted(value));

        return tag.Origin(attribute.ValueStart + skip) is { } origin
            ? new AttributeValue(code, true, true, origin.Position, origin.Line)
            : AttributeValue.Code(code);
    }

    private static int IndexOfPlaceholder(string text)
    {
        var start = text.IndexOf('\u0001');
        var end = text.IndexOf('\u0002');

        return start >= 0 && end > start && int.TryParse(text.Substring(start + 1, end - start - 1), out var index)
            ? index
            : -1;
    }

    /// <summary>
    /// A value of text and expressions, class="box @kind", as one string.
    /// </summary>
    /// <remarks>
    /// The pieces were written one after another into the tag, so the tag
    /// came out as text with the expression in the middle of it.
    /// </remarks>
    private static AttributeValue Mixed(Tag tag, string value)
    {
        var pieces = new List<string>();
        var at = 0;

        while (at < value.Length)
        {
            var start = value.IndexOf('\u0001', at);

            if (start < 0)
            {
                pieces.Add(Quoted(value.Substring(at)));
                break;
            }

            if (start > at) pieces.Add(Quoted(value.Substring(at, start - at)));

            var end = value.IndexOf('\u0002', start);
            var index = int.Parse(value.Substring(start + 1, end - start - 1));

            pieces.Add($"Global.System.Convert.ToString({tag.Expressions[index].Expression})");
            at = end + 1;
        }

        return AttributeValue.Code(string.Join(" & ", pieces));
    }

    /// <summary>
    /// Writes one call whose last argument is an attribute's value, mapped to
    /// the template when the value is the template's own code.
    /// </summary>
    private static void WriteValueCall(
        StringBuilder builder, string pad, string before, AttributeValue value, string after,
        List<SourceMapping> mappings, string? filePath)
    {
        var line = pad + before + value.Lead + value.Written + after;

        if (!value.IsMapped || filePath is null)
        {
            builder.AppendLine(line);
            return;
        }

        ExternalSourceWriter.WriteMapped(builder, mappings, filePath,
            value.Position, value.Written.Length, value.Line,
            () => builder.AppendLine(line),
            offset: pad.Length + before.Length + value.Lead.Length);
    }

    /// <summary>A @bind and the modifiers written beside it.</summary>
    private sealed class Binding(string name)
    {
        public string Name { get; } = name;

        public AttributeValue? Value { get; set; }

        public AttributeValue? Get { get; set; }

        public AttributeValue? Set { get; set; }

        public AttributeValue? After { get; set; }

        public AttributeValue? Culture { get; set; }

        public string? Format { get; set; }

        public string? Event { get; set; }
    }

    /// <summary>
    /// Writes a tag's attributes, the directive ones as the frames Blazor
    /// expects rather than as markup.
    /// </summary>
    /// <remarks>
    /// @key, @ref, @formname and @rendermode are written after the others:
    /// they are not attributes, and Blazor takes attributes only straight
    /// after the element or component they belong to.
    /// </remarks>
    private DeferredFrames WriteTagAttributes(
        StringBuilder builder, Tag tag, List<TagAttribute> attributes, string name, bool isComponent,
        ComponentShape? shape, IReadOnlyList<string> typeArguments,
        ref int sequence, string pad, List<SourceMapping> mappings, string? filePath)
    {
        var bindings = new List<Binding>();
        AttributeValue? key = null;
        AttributeValue? reference = null;
        AttributeValue? formName = null;
        AttributeValue? renderMode = null;

        var isCheckbox = !isComponent && attributes.Any(attribute =>
            attribute.Name.Equals("type", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(attribute.Value?.Trim(), "checkbox", StringComparison.OrdinalIgnoreCase));

        foreach (var attribute in attributes)
        {
            if (!attribute.Name.StartsWith("@", StringComparison.Ordinal))
            {
                // Context="item" names the value of the component's content,
                // it is not a parameter: C# reads it the same way.
                if (isComponent && attribute.Name == "Context" && shape?.Parameter("Context") is null) continue;

                var parameter = isComponent ? shape?.Parameter(attribute.Name) : null;

                WritePlainAttribute(builder, tag, attribute, isComponent, parameter, shape, typeArguments,
                    ref sequence, pad, mappings, filePath);
                continue;
            }

            var directive = attribute.Name.Substring(1);
            var colon = directive.IndexOf(':');
            var baseName = colon < 0 ? directive : directive.Substring(0, colon);
            var modifier = colon < 0 ? null : directive.Substring(colon + 1);

            if (baseName.Equals("bind", StringComparison.OrdinalIgnoreCase) ||
                baseName.StartsWith("bind-", StringComparison.OrdinalIgnoreCase))
            {
                var binding = bindings.FirstOrDefault(b => b.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));

                if (binding is null)
                {
                    binding = new Binding(baseName);
                    bindings.Add(binding);
                }

                ReadBindingPart(binding, modifier, tag, attribute);
                continue;
            }

            if (baseName.StartsWith("on", StringComparison.Ordinal))
            {
                WriteEvent(builder, tag, attribute, baseName, modifier, ref sequence, pad, mappings, filePath);
                continue;
            }

            switch (baseName)
            {
                case "key":
                    key = ValueOf(tag, attribute, isCode: true);
                    break;

                case "ref":
                    reference = ValueOf(tag, attribute, isCode: true);
                    break;

                case "formname":
                    formName = ValueOf(tag, attribute, isCode: false);
                    break;

                case "rendermode":
                    renderMode = ValueOf(tag, attribute, isCode: true);
                    break;

                case "attributes":
                    // Splatting: every pair in a dictionary becomes an attribute.
                    WriteValueCall(builder, pad, $"{Builder}.AddMultipleAttributes({sequence++}, ",
                        ValueOf(tag, attribute, isCode: true), ")", mappings, filePath);
                    break;

                default:
                    // A directive attribute this writer does not know: kept
                    // as an attribute without its marker rather than dropped.
                    WritePlainAttribute(builder, tag, attribute with { Name = directive }, isComponent, null, shape, typeArguments,
                        ref sequence, pad, mappings, filePath);
                    break;
            }
        }

        foreach (var binding in bindings)
            WriteBinding(builder, binding, isComponent, isCheckbox, ref sequence, pad, mappings, filePath);

        return new DeferredFrames(key, reference, formName, renderMode, name, isComponent);
    }

    /// <summary>
    /// @key, @ref, @formname and @rendermode, which follow every attribute —
    /// ChildContent included, on a component with content.
    /// </summary>
    private sealed record DeferredFrames(
        AttributeValue? Key, AttributeValue? Reference, AttributeValue? FormName, AttributeValue? RenderMode,
        string Name, bool IsComponent);


    /// <summary>Writes the frames that have to come after a tag's attributes.</summary>
    private void WriteDeferred(
        StringBuilder builder, DeferredFrames frames, ref int sequence, string pad,
        List<SourceMapping> mappings, string? filePath)
    {
        var (key, reference, formName, renderMode, name, isComponent) = frames;

        if (key is not null)
            WriteValueCall(builder, pad, $"{Builder}.SetKey(", key, ")", mappings, filePath);

        if (reference is not null)
        {
            // Assigned when the element or component exists, as C# does: an
            // ElementReference for an element, the instance for a component.
            var capture = isComponent
                ? $"{Builder}.AddComponentReferenceCapture({sequence++}, Sub(__value) "
                : $"{Builder}.AddElementReferenceCapture({sequence++}, Sub(__value) ";

            var assigned = isComponent ? $" = CType(__value, {ComponentType(name)}))" : " = __value)";

            WriteValueCall(builder, pad, capture, reference, assigned, mappings, filePath);
        }

        // The name a form posts under, which enhanced navigation and
        // [SupplyParameterFromForm] find it by.
        if (formName is not null)
            builder.AppendLine($"{pad}{Builder}.AddNamedEvent(\"onsubmit\", {formName.Written})");

        if (renderMode is not null && isComponent)
            WriteValueCall(builder, pad, $"{Builder}.AddComponentRenderMode(", renderMode, ")", mappings, filePath);
    }

    /// <summary>An attribute written as the template has it.</summary>
    private void WritePlainAttribute(
        StringBuilder builder, Tag tag, TagAttribute attribute, bool isComponent,
        ComponentParameter? parameter, ComponentShape? shape, IReadOnlyList<string> typeArguments,
        ref int sequence, string pad, List<SourceMapping> mappings, string? filePath)
    {
        // AddComponentParameter on a component: a parameter is set on the
        // component object, not written into the markup.
        var call = isComponent ? "AddComponentParameter" : "AddAttribute";

        if (IsMixed(attribute))
        {
            WriteMixedAttribute(builder, tag, attribute, call, ref sequence, pad, mappings, filePath);
            return;
        }

        // The parameter's own name, as declared: Blazor matches it without
        // regard to case, but the frame carries it as written here.
        var name = parameter?.Name ?? attribute.Name;
        var before = $"{Builder}.{call}({sequence++}, \"{name}\", ";

        // Typed by the parameter, as C# types it: a literal is text only for a
        // String or Object parameter. Count="5" for an Integer was the text
        // "5", and failed at render with an invalid cast.
        var value = ValueOf(tag, attribute, isCode: parameter is { Kind: not ParameterKind.Text });

        // OnSave="Sub() saved = True" for an EventCallback parameter: wrapped
        // in one, typed by the callback's argument, as C# wraps it. A value
        // that is already an EventCallback goes through the same overloads.
        if (parameter is { Kind: ParameterKind.EventCallback or ParameterKind.EventCallbackOf } && value.IsCode)
        {
            var argument = parameter.ArgumentType is { } type
                ? $"(Of {SubstituteTypeParameters(type, shape, typeArguments)})"
                : "";

            before += $"Global.Microsoft.AspNetCore.Components.EventCallback.Factory.Create{argument}(Me, ";
            WriteValueCall(builder, pad, before, value, "))", mappings, filePath);
            return;
        }

        // onclick="@AddressOf Go": a method reference is wrapped in an
        // EventCallback, since AddAttribute has no overload taking a bare
        // delegate and the call did not resolve.
        if (value.IsCode && value.Written.StartsWith("AddressOf", StringComparison.Ordinal))
        {
            before += "Global.Microsoft.AspNetCore.Components.EventCallback.Factory.Create(Me, ";
            WriteValueCall(builder, pad, before, value, "))", mappings, filePath);
            return;
        }

        WriteValueCall(builder, pad, before, value, ")", mappings, filePath);
    }

    /// <summary>A type with a component's type parameters replaced by the tag's arguments.</summary>
    private static string SubstituteTypeParameters(string type, ComponentShape? shape, IReadOnlyList<string> typeArguments) =>
        Substitute(type, new Frame(FrameKind.Component) { Shape = shape, TypeArguments = typeArguments });

    /// <summary>Whether a value holds text and expressions together.</summary>
    private static bool IsMixed(TagAttribute attribute)
    {
        var body = attribute.Value?.Trim() ?? "";

        return body.IndexOf('') >= 0 && body != Tag.Placeholder(IndexOfPlaceholder(body));
    }

    /// <summary>
    /// class="box @kind wide": each expression into a local of its own, mapped
    /// as an expression in the content is, then the attribute as one string.
    /// </summary>
    /// <remarks>
    /// Mapped this way because #ExternalSource covers a whole line, and one
    /// line holding two expressions can map only one of them.
    /// </remarks>
    private void WriteMixedAttribute(
        StringBuilder builder, Tag tag, TagAttribute attribute, string call,
        ref int sequence, string pad, List<SourceMapping> mappings, string? filePath)
    {
        var value = attribute.Value!;
        var pieces = new List<string>();
        var at = 0;

        while (at < value.Length)
        {
            var start = value.IndexOf('', at);

            if (start < 0)
            {
                pieces.Add(Quoted(value.Substring(at)));
                break;
            }

            if (start > at) pieces.Add(Quoted(value.Substring(at, start - at)));

            var end = value.IndexOf('', start);
            var expression = tag.Expressions[int.Parse(value.Substring(start + 1, end - start - 1))];
            var local = $"__a{locals_++}";
            var awaited = expression.IsAwaited ? "Await " : "";

            VbHtmlCodeWriter.WriteExpressionMapped(builder, mappings, filePath, expression,
                $"{pad}Dim {local} = ", $"{pad}Dim {local} = {awaited}{expression.Expression}");

            pieces.Add($"Global.System.Convert.ToString({local})");
            at = end + 1;
        }

        builder.AppendLine($"{pad}{Builder}.{call}({sequence++}, \"{attribute.Name}\", {string.Join(" & ", pieces)})");
    }

    /// <summary>Counts the locals mixed attribute values are written into.</summary>
    private int locals_;

    /// <summary>
    /// @onclick and every other event: the handler, or one of its flags.
    /// </summary>
    /// <remarks>
    /// The handler is wrapped in an EventCallback typed by the event's
    /// arguments, as the C# compiler types it from the event's registration:
    /// a lambda taking one argument is then a MouseEventArgs handler for
    /// @onclick without saying so, and one taking none, or a Task-returning
    /// method, picks its own overload.
    /// </remarks>
    private void WriteEvent(
        StringBuilder builder, Tag tag, TagAttribute attribute, string eventName, string? modifier,
        ref int sequence, string pad, List<SourceMapping> mappings, string? filePath)
    {
        if (modifier is null)
        {
            var before = $"{Builder}.AddAttribute({sequence++}, \"{eventName}\", " +
                $"Global.Microsoft.AspNetCore.Components.EventCallback.Factory.Create(Of {EventArgsFor(eventName)})(Me, ";

            WriteValueCall(builder, pad, before, ValueOf(tag, attribute, isCode: true), "))", mappings, filePath);
            return;
        }

        var flag = modifier switch
        {
            "preventDefault" => "AddEventPreventDefaultAttribute",
            "stopPropagation" => "AddEventStopPropagationAttribute",
            _ => null
        };

        if (flag is null) return;

        WriteValueCall(builder, pad, $"{Builder}.{flag}({sequence++}, \"{eventName}\", ",
            ValueOf(tag, attribute, isCode: true), ")", mappings, filePath);
    }

    /// <summary>The argument type Blazor passes an event's handler.</summary>
    private static string EventArgsFor(string eventName)
    {
        const string Web = "Global.Microsoft.AspNetCore.Components.Web.";

        var name = eventName.ToLowerInvariant();

        if (name is "onwheel" or "onmousewheel") return Web + "WheelEventArgs";
        if (name is "onclick" or "ondblclick" or "oncontextmenu" || name.StartsWith("onmouse", StringComparison.Ordinal)) return Web + "MouseEventArgs";
        if (name.StartsWith("onkey", StringComparison.Ordinal)) return Web + "KeyboardEventArgs";
        if (name is "onfocus" or "onblur" or "onfocusin" or "onfocusout") return Web + "FocusEventArgs";
        if (name is "onchange" or "oninput") return "Global.Microsoft.AspNetCore.Components.ChangeEventArgs";
        if (name.StartsWith("onpointer", StringComparison.Ordinal) || name is "ongotpointercapture" or "onlostpointercapture") return Web + "PointerEventArgs";
        if (name.StartsWith("ondrag", StringComparison.Ordinal) || name == "ondrop") return Web + "DragEventArgs";
        if (name.StartsWith("ontouch", StringComparison.Ordinal)) return Web + "TouchEventArgs";
        if (name is "oncopy" or "oncut" or "onpaste") return Web + "ClipboardEventArgs";
        if (name is "onloadstart" or "onprogress" or "onload" or "onloadend" or "onabort" or "ontimeout") return Web + "ProgressEventArgs";
        if (name == "onerror") return Web + "ErrorEventArgs";

        return "Global.System.EventArgs";
    }

    /// <summary>Reads @bind, @bind:event and the other parts of one binding.</summary>
    private static void ReadBindingPart(Binding binding, string? modifier, Tag tag, TagAttribute attribute)
    {
        switch (modifier)
        {
            case null:
                binding.Value = ValueOf(tag, attribute, isCode: true);
                break;

            case "get":
                binding.Get = ValueOf(tag, attribute, isCode: true);
                break;

            case "set":
                binding.Set = ValueOf(tag, attribute, isCode: true);
                break;

            case "after":
                binding.After = ValueOf(tag, attribute, isCode: true);
                break;

            case "culture":
                binding.Culture = ValueOf(tag, attribute, isCode: true);
                break;

            case "format":
                binding.Format = attribute.Value;
                break;

            case "event":
                binding.Event = attribute.Value?.Trim();
                break;
        }
    }

    /// <summary>
    /// Writes a two-way binding as the pair of attributes it really is.
    /// </summary>
    /// <remarks>
    /// The value is formatted by BindConverter, which knows how each type is
    /// written into an attribute, and read back by CreateBinder, which parses
    /// it into the target's type: done by hand it would be a parse per type
    /// here, disagreeing with the framework's own as soon as either changed.
    ///
    /// On a component, @bind-Value sets Value, ValueChanged and
    /// ValueExpression; InputText and the other form inputs throw without the
    /// last, which names the field a validation message belongs to.
    /// </remarks>
    private void WriteBinding(
        StringBuilder builder, Binding binding, bool isComponent, bool isCheckbox,
        ref int sequence, string pad, List<SourceMapping> mappings, string? filePath)
    {
        var target = binding.Get ?? binding.Value;

        if (target is null) return;

        var named = binding.Name.StartsWith("bind-", StringComparison.OrdinalIgnoreCase)
            ? binding.Name.Substring("bind-".Length)
            : null;

        var extra = "";

        if (binding.Format is not null) extra += $", format:={Quoted(binding.Format)}";
        if (binding.Culture is not null) extra += $", culture:={binding.Culture.Written}";

        var setter = Setter(binding, target, pad);

        if (isComponent)
        {
            var parameter = named ?? "Value";

            WriteValueCall(builder, pad, $"{Builder}.AddComponentParameter({sequence++}, \"{parameter}\", ",
                target, ")", mappings, filePath);

            builder.AppendLine(
                $"{pad}{Builder}.AddComponentParameter({sequence++}, \"{binding.Event ?? parameter + "Changed"}\", " +
                "Global.Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers.CreateInferredEventCallback(" +
                $"Me, {setter}, {target.Written}))");

            builder.AppendLine(
                $"{pad}{Builder}.AddComponentParameter({sequence++}, \"{parameter}Expression\", " +
                $"{BindExpressionHelper}({target.Written}, Function() {target.Written}))");

            usesBindExpression_ = true;
            return;
        }

        var attribute = named ?? (isCheckbox ? "checked" : "value");
        var changed = binding.Event ?? "onchange";

        WriteValueCall(builder, pad,
            $"{Builder}.AddAttribute({sequence++}, \"{attribute}\", Global.Microsoft.AspNetCore.Components.BindConverter.FormatValue(",
            target, $"{extra}))", mappings, filePath);

        builder.AppendLine(
            $"{pad}{Builder}.AddAttribute({sequence++}, \"{changed}\", " +
            // Called as the shared method it really is. CreateBinder is an
            // extension, and Visual Basic does not apply extensions to a
            // fully qualified chain: written as
            // Global.….EventCallback.Factory.CreateBinder(…) it failed to
            // resolve even with the namespace imported.
            "Global.Microsoft.AspNetCore.Components." +
            "EventCallbackFactoryBinderExtensions.CreateBinder(" +
            "Global.Microsoft.AspNetCore.Components.EventCallback.Factory, " +
            $"Me, {setter}, {target.Written}{extra}))");

        // Tells Blazor which attribute the binding updates, so it keeps the
        // element's live value in step as C# does.
        builder.AppendLine($"{pad}{Builder}.SetUpdatesAttributeName(\"{attribute}\")");
    }

    /// <summary>
    /// What a binding does with a value coming back: @bind:set, or an
    /// assignment followed by @bind:after.
    /// </summary>
    private static string Setter(Binding binding, AttributeValue target, string pad)
    {
        if (binding.Set is not null) return binding.Set.Written;

        if (binding.After is null) return $"Sub(__value) {target.Written} = __value";

        // Multi-line: a single-line lambda holds one statement. @bind:after is
        // started rather than awaited; the EventCallback re-renders when it
        // completes either way.
        return "Sub(__value)\n" +
            $"{pad}    {target.Written} = __value\n" +
            $"{pad}    Global.Microsoft.AspNetCore.Components.EventCallback.Factory.Create(Me, {binding.After.Written}).InvokeAsync()\n" +
            $"{pad}End Sub";
    }

    /// <summary>The generated helper that turns a bound value into its expression.</summary>
    private const string BindExpressionHelper = "__BindExpression";

    /// <summary>Whether a component binding needs <see cref="BindExpressionHelper"/>.</summary>
    private bool usesBindExpression_;

    /// <summary>
    /// A helper on the class that returns a lambda as an expression tree of
    /// the bound value's type. ValueExpression is typed Expression(Of
    /// Func(Of T)), and AddComponentParameter takes Object, so the lambda has
    /// nothing to be converted to without it; the value supplies T.
    /// </summary>
    private void WriteBindExpressionHelper(StringBuilder builder)
    {
        if (!usesBindExpression_) return;

        const string Expression = "Global.System.Linq.Expressions.Expression(Of Global.System.Func(Of T))";

        builder.AppendLine();
        builder.AppendLine(
            $"        Private Shared Function {BindExpressionHelper}(Of T)(value As T, expression As {Expression}) As {Expression}");
        builder.AppendLine("            Return expression");
        builder.AppendLine("        End Function");
    }

    /// <summary>
    /// Markup nodes that sit next to each other, as one node.
    /// </summary>
    private IReadOnlyList<VbHtmlNode> JoinMarkup(IReadOnlyList<VbHtmlNode> nodes)
    {
        var joined = new List<VbHtmlNode>(nodes.Count);

        foreach (var node in nodes)
        {
            // Only markup that sits side by side in the template: positions in
            // the joined text are read back as template positions, and "@@",
            // a comment or a member @Code block between two runs would shift
            // every mapping after it.
            if (node is HtmlNode html
                && joined.Count > 0
                && joined[joined.Count - 1] is HtmlNode previous
                && previous.Position + previous.Text.Length == html.Position)
            {
                joined[joined.Count - 1] = new HtmlNode(
                    previous.Text + html.Text, previous.Position, previous.Line);

                continue;
            }

            joined.Add(node);
        }

        return joined;
    }

    private IReadOnlyList<VbHtmlNode> Replace(
        IReadOnlyList<VbHtmlNode> nodes, int at, VbHtmlNode with)
    {
        var copy = nodes.ToList();
        copy[at] = with;

        return copy;
    }

    private void WriteNode(
        StringBuilder builder, VbHtmlNode node, ref int sequence, int indent,
        List<SourceMapping> mappings, string? filePath)
    {
        var pad = new string(' ', indent);

        switch (node)
        {
            case HtmlNode html:
                WriteMarkup(builder, html.Text, html.Position, html.Line, ref sequence, pad, mappings, filePath);
                break;

            case ExpressionNode expression:
                BeforeContent(builder, pad, ref sequence, whitespaceOnly: false);

                // AddContent encodes what it is given, which is what an
                // implicit expression means. Raw output goes through
                // MarkupString, the way it does in C#.
                var value = expression.IsAwaited
                    ? $"Await {expression.Expression}"
                    : expression.Expression;

                // Raw output goes through MarkupString, the way @Html.Raw does
                // in a view: AddContent encodes anything else, which is what an
                // implicit expression means.
                var wrapper = expression.IsRaw
                    ? "New Global.Microsoft.AspNetCore.Components.MarkupString(Global.System.Convert.ToString("
                    : "";

                if (expression.IsRaw) value = $"{wrapper}{value}))";

                var seq = sequence++;

                // Through a local on a line of its own, so the mapped region
                // begins where the expression begins. #ExternalSource has to
                // start its own line, so the expression cannot be mapped
                // inside the AddContent call — and mapping the whole line
                // instead put the caret before "__builder", far enough out
                // that Roslyn was asked about the wrong token.
                //
                // The view writer maps whole lines and gets away with it only
                // because "Write(" is short enough that the drift still lands
                // inside the expression.
                var local = $"__v{seq}";

                // The mapping starts at the template's own text in the line,
                // past "Dim __v0 = " and any MarkupString around it; counted,
                // not searched for, since a one-letter name is also in "Dim".
                var before = $"{pad}Dim {local} = {wrapper}";
                var line = $"{pad}Dim {local} = {value}";

                VbHtmlCodeWriter.WriteExpressionMapped(builder, mappings, filePath, expression, before, line);

                builder.AppendLine($"{pad}{Builder}.AddContent({seq}, {local})");
                break;

            case StatementNode statement:
                BeforeContent(builder, pad, ref sequence, whitespaceOnly: false);

                // From the body, line for line, as a view does: the whole block
                // written as one line put its second line at the left margin
                // and anchored the pragma at "@Code" rather than at the first
                // statement, a line early.
                WriteMapped(builder, mappings, filePath,
                    statement.BodyPosition, statement.Code.Length, statement.BodyLine,
                    () =>
                    {
                        foreach (var line in ExternalSourceWriter.LinesOf(statement.Code))
                            builder.AppendLine($"{pad}{line}");
                    });
                break;

            case BlockNode block:
                BeforeContent(builder, pad, ref sequence, whitespaceOnly: false);

                // The opening clause carries the interesting expression — the
                // condition of an If, the source of a For Each — so a caret
                // there must reach Roslyn. Mapping only the body left every
                // question asked inside a condition unanswered.
                WriteMapped(builder, mappings, filePath,
                    block.Position + 1, block.Opening.Length, block.Line,
                    () => builder.AppendLine($"{pad}{block.Opening}"));

                // Through WriteNodes, as at the top: a tag with directive
                // attributes inside an @If or a loop was written as text, and
                // its closing tag closed an element never opened.
                WriteNodes(builder, VbHtmlCodeWriter.BodyOf(block).ToList(), ref sequence, indent + 4, mappings, filePath);

                foreach (var clause in block.Clauses)
                {
                    VbHtmlCodeWriter.WriteClosingOrClause(builder, mappings, filePath, pad,
                        clause.Keyword, clause.Position, clause.Line);

                    WriteNodes(builder, clause.Body.ToList(), ref sequence, indent + 4, mappings, filePath);
                }

                VbHtmlCodeWriter.WriteClosingOrClause(builder, mappings, filePath, pad,
                    block.Closing, block.ClosingPosition, block.ClosingLine);
                break;

            case FunctionsNode:
            case DirectiveNode:
                // Written outside BuildRenderTree, or consumed by the header.
                break;

            case SectionNode:
                // Sections belong to a layout, which a component does not
                // have: its equivalent is a RenderFragment parameter, and
                // silently emitting nothing is better than emitting a section
                // the runtime will never look for.
                break;
        }
    }

    /// <summary>
    /// The top-level @Code blocks that declare members rather than run.
    /// </summary>
    /// <remarks>
    /// Decided by the first word, which is where Visual Basic itself tells a
    /// declaration from a statement: Private, Sub, an attribute... "Dim" is
    /// left to the statements, since that is what it means in every view. A
    /// block the parser split around markup is statements by construction.
    /// </remarks>
    private static HashSet<StatementNode> MemberBlocks(IReadOnlyList<VbHtmlNode> nodes)
    {
        var blocks = new HashSet<StatementNode>();

        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is not StatementNode { IsContinuation: false } statement) continue;
            if (!StartsWithDeclaration(statement.Code)) continue;

            var nextStatement = nodes.Skip(index + 1).OfType<StatementNode>().FirstOrDefault();

            if (nextStatement is { IsContinuation: true }) continue;

            blocks.Add(statement);
        }

        return blocks;
    }

    private static readonly string[] DeclarationWords =
    [
        "Private", "Public", "Protected", "Friend", "Shared", "Overrides", "Overridable",
        "NotOverridable", "MustOverride", "Overloads", "Shadows", "ReadOnly", "WriteOnly",
        "WithEvents", "Async", "Iterator", "Partial", "Sub", "Function", "Property", "Event",
        "Const", "Enum", "Class", "Structure", "Interface", "Delegate", "Default", "Custom",
        "Operator", "Widening", "Narrowing", "MustInherit", "NotInheritable", "Declare",
    ];

    private static bool StartsWithDeclaration(string code)
    {
        var text = FirstLineOfCode(code);

        // An attribute: <Parameter> Public Property Title As String.
        if (text.StartsWith("<", StringComparison.Ordinal)) return true;

        var length = 0;

        while (length < text.Length && (char.IsLetterOrDigit(text[length]) || text[length] == '_')) length++;

        var word = text.Substring(0, length);

        return DeclarationWords.Any(keyword => string.Equals(keyword, word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The first line that is code: comments, Rem, #Region and blank lines
    /// say nothing about whether a block declares or runs. A member block
    /// opening with "' state" stayed in BuildRenderTree and did not compile.
    /// </summary>
    private static string FirstLineOfCode(string code)
    {
        foreach (var line in code.Split('\n'))
        {
            var text = line.Trim();

            if (text.Length == 0) continue;
            if (text.StartsWith("'", StringComparison.Ordinal)) continue;
            if (text.StartsWith("#", StringComparison.Ordinal)) continue;
            if (text.Equals("Rem", StringComparison.OrdinalIgnoreCase)) continue;
            if (text.StartsWith("Rem ", StringComparison.OrdinalIgnoreCase)) continue;

            return text;
        }

        return "";
    }


    /// <summary>The nested attribute class that carries a component's @rendermode.</summary>
    private const string RenderModeAttributeName = "__PrivateComponentRenderModeAttribute";

    /// <summary>
    /// @rendermode, written the way the C# compiler writes it: a nested
    /// RenderModeAttribute whose Mode is the template's expression, put on
    /// the class. Blazor reads the attribute; nothing else is needed.
    /// </summary>
    /// <remarks>
    /// Friend rather than Private: Visual Basic binds an attribute on a class
    /// from outside it, where a Private nested type is not accessible.
    /// </remarks>
    private void WriteRenderMode(
        StringBuilder builder, List<SourceMapping> mappings, string? filePath, VbHtmlDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.RenderMode)) return;

        const string ModeType = "Global.Microsoft.AspNetCore.Components.IComponentRenderMode";
        const string Prefix = "                    Return ";

        builder.AppendLine();
        builder.AppendLine($"        Friend NotInheritable Class {RenderModeAttributeName}");
        builder.AppendLine("            Inherits Global.Microsoft.AspNetCore.Components.RenderModeAttribute");
        builder.AppendLine();
        builder.AppendLine($"            Public Overrides ReadOnly Property Mode As {ModeType}");
        builder.AppendLine("                Get");

        ExternalSourceWriter.WriteMapped(builder, mappings, filePath,
            document.RenderModePosition, document.RenderMode!.Length, document.RenderModeLine,
            () => builder.AppendLine(Prefix + document.RenderMode),
            offset: Prefix.Length);

        builder.AppendLine("                End Get");
        builder.AppendLine("            End Property");
        builder.AppendLine("        End Class");
    }

    /// <summary>
    /// Writes one fragment, recording where it came from. See
    /// <see cref="ExternalSourceWriter"/>, which views share.
    /// </summary>
    private void WriteMapped(
        StringBuilder builder,
        List<SourceMapping> mappings,
        string? filePath,
        int originalPosition,
        int originalLength,
        int originalLine,
        Action write,
        int offset = 0) =>
        ExternalSourceWriter.WriteMapped(
            builder, mappings, filePath, originalPosition, originalLength, originalLine, write, offset);


    /// <summary>
    /// Writes a run of markup as element calls.
    /// </summary>
    /// <remarks>
    /// Element by element rather than as one blob of markup: Blazor can only
    /// diff what it can see the shape of, and a whole page handed over as text
    /// is replaced wholesale on every change rather than patched.
    ///
    /// <paramref name="position"/> and <paramref name="line"/> say where the
    /// markup starts in the template, so a directive attribute's code in it —
    /// @onclick="Sub() count += 1" — is mapped back to where it was written.
    /// </remarks>
    private void WriteMarkup(
        StringBuilder builder, string markup, int position, int line, ref int sequence, string pad,
        List<SourceMapping> mappings, string? filePath)
    {
        var at = 0;

        while (at < markup.Length)
        {
            var open = markup.IndexOf('<', at);

            if (open < 0)
            {
                AddText(builder, markup.Substring(at), ref sequence, pad);
                break;
            }

            if (open > at) AddText(builder, markup.Substring(at, open - at), ref sequence, pad);

            // A comment is dropped, as Blazor drops it; <!DOCTYPE html> is
            // written as it stands. Read as a tag, the doctype was opened as
            // an element called "!DOCTYPE" that nothing closed, and an App
            // component starting with it failed to render at all.
            if (markup.IndexOf("<!--", open, StringComparison.Ordinal) == open)
            {
                var commentEnd = markup.IndexOf("-->", open + 4, StringComparison.Ordinal);

                if (commentEnd < 0)
                {
                    // Split by an "@" inside it: left as text, as before.
                    AddText(builder, markup.Substring(open), ref sequence, pad);
                    break;
                }

                at = commentEnd + 3;
                continue;
            }

            if (open + 1 < markup.Length && markup[open + 1] == '!')
            {
                var declarationEnd = markup.IndexOf('>', open);

                if (declarationEnd < 0)
                {
                    AddText(builder, markup.Substring(open), ref sequence, pad);
                    break;
                }

                AddText(builder, markup.Substring(open, declarationEnd - open + 1), ref sequence, pad);

                at = declarationEnd + 1;
                continue;
            }

            // Outside quoted values: "Sub() If x > 0 Then ..." in an attribute
            // does not end the tag.
            var length = TagEnd(markup.Substring(open));

            if (length < 0)
            {
                // An unterminated tag is text, not a broken element: a
                // template being typed into is unparseable most of the time.
                AddText(builder, markup.Substring(open), ref sequence, pad);
                break;
            }

            var close = open + length;
            var tagText = markup.Substring(open, close - open + 1);

            if (tagText.Length > 1 && tagText[1] == '/')
            {
                // Which kind of thing is being closed has to be remembered:
                // a component is closed with CloseComponent and an element
                // with CloseElement, and calling the wrong one leaves the
                // render tree unbalanced for the rest of the component.
                var frame = open_.Count > 0 ? open_.Pop() : new Frame(FrameKind.Element);

                // The content lambda first — ChildContent or a named
                // fragment — then the component's deferred frames and the
                // component itself.
                if (frame.ContentClose is { } contentClose)
                {
                    builder.AppendLine($"{pad}{contentClose}");
                    lambdas_--;
                }

                if (frame.Kind == FrameKind.Component)
                {
                    if (frame.Deferred is { } deferred)
                        WriteDeferred(builder, deferred, ref sequence, pad, mappings, filePath);

                    builder.AppendLine($"{pad}{Builder}.CloseComponent()");
                }
                else if (frame.Kind == FrameKind.Element)
                {
                    builder.AppendLine($"{pad}{Builder}.CloseElement()");
                }

            }
            else
            {
                var tag = new Tag();

                tag.AddLiteral(tagText, position + open, LineAt(markup, line, open));

                WriteTag(builder, tag, ref sequence, pad, mappings, filePath);
            }

            at = close + 1;
        }
    }

    private void AddText(
        StringBuilder builder, string text, ref int sequence, string pad)
    {
        if (text.Length == 0) return;

        if (!BeforeContent(builder, pad, ref sequence, string.IsNullOrWhiteSpace(text))) return;

        // AddMarkupContent, not AddContent: literal markup from the template
        // is already markup, and AddContent encodes it — the line breaks
        // between elements came out as &#xA; in the page.
        builder.AppendLine($"{pad}{Builder}.AddMarkupContent({sequence++}, {Quoted(text)})");
    }

    /// <summary>
    /// Whether a tag names another component rather than an HTML element.
    /// </summary>
    /// <remarks>
    /// By its first letter, which is how Razor decides too: HTML element names
    /// are lower case by convention and a component is a class, so the case is
    /// the whole distinction. A lower-case component name is unreachable this
    /// way, which is the same limitation C# has.
    /// </remarks>
    private static bool IsComponentName(string name) =>
        name.Length > 0 && char.IsUpper(name[0]);

    /// <summary>
    /// The type a component tag names, ready to be written.
    /// </summary>
    /// <remarks>
    /// A sibling is named unqualified so Visual Basic finds it in the file's
    /// own namespace, and the compiler's RootNamespace is applied for us. The
    /// framework's own components are not siblings and have to be named in
    /// full, or CascadingValue resolves to nothing at all.
    ///
    /// CascadingValue is generic over what it carries, and the type has to
    /// match what the receiving side asks for: Of Object made the cascade
    /// carry an Object, which matched no parameter at all — the child rendered
    /// with its property left empty and nothing said why. Visual Basic will
    /// not infer it either, so String is written, which is what a cascade
    /// carries in practice. A cascade of another type needs the component
    /// written by hand for now.
    /// </remarks>
    private static string ComponentType(string name)
    {
        // A type argument written into the tag: <Elenco(Of String) … />. The
        // C# compiler infers this from the parameter values, which needs the
        // type system; here the template says it. Without a way to say it a
        // generic component could not be used at all — the call failed with
        // "too few type arguments", against generated code.
        var generic = name.IndexOf("(Of ", StringComparison.OrdinalIgnoreCase);

        if (generic > 0) return name;

        return Known(name);
    }

    private static string Known(string name) => name switch
    {
        "CascadingValue" =>
            "Global.Microsoft.AspNetCore.Components.CascadingValue(Of String)",
        "DynamicComponent" =>
            "Global.Microsoft.AspNetCore.Components.DynamicComponent",
        _ => name
    };

    /// <summary>The element name at the start of a tag.</summary>
    private static string Name(string tag)
    {
        // A type argument belongs to the name: <Elenco(Of String) …> read a
        // character at a time stops at the bracket and leaves "(Of String)" to
        // be taken for attributes, so the whole thing is claimed first.
        var generic = tag.IndexOf("(Of ", StringComparison.OrdinalIgnoreCase);

        if (generic > 0)
        {
            var close = tag.IndexOf(')', generic);

            if (close > 0 && tag.Substring(0, generic).IndexOf(' ') < 0)
                return tag.Substring(0, close + 1);
        }

        var end = 0;

        while (end < tag.Length && !char.IsWhiteSpace(tag[end]) && tag[end] != '/') end++;

        return tag.Substring(0, end);
    }

    /// <summary>
    /// Whether an element closes itself in HTML.
    /// </summary>
    /// <remarks>
    /// A void element has no closing tag to find, so without this the builder
    /// is left with an element open and everything after it nests inside a
    /// &lt;br&gt; — the tree stays unbalanced to the end of the component.
    /// </remarks>
    private static bool IsVoid(string name) => name.ToLowerInvariant() is
        "area" or "base" or "br" or "col" or "embed" or "hr" or "img" or
        "input" or "link" or "meta" or "source" or "track" or "wbr";

    /// <summary>
    /// A Visual Basic string literal.
    /// </summary>
    /// <remarks>
    /// Line breaks become vbLf rather than being written into the literal:
    /// Visual Basic has no multi-line string, so markup spanning lines — which
    /// is all markup — produced a literal broken across lines and a file that
    /// did not compile.
    /// </remarks>
    private static string Quoted(string text)
    {
        var parts = text.Replace("\r\n", "\n").Split('\n');
        var pieces = new List<string>();

        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) pieces.Add("Global.Microsoft.VisualBasic.Constants.vbLf");

            if (parts[i].Length > 0)
                pieces.Add("\"" + parts[i].Replace("\"", "\"\"") + "\"");
        }

        return pieces.Count == 0 ? "\"\"" : string.Join(" & ", pieces);
    }
}
