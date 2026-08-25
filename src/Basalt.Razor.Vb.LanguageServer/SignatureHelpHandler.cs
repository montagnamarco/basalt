using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// What the helpers available inside an expression expect.
///
/// A view has no compilation to ask, so this describes what the runtime
/// itself provides — the calls a template actually makes — rather than
/// pretending to know the model's methods.
/// </summary>
public sealed class VbHtmlSignatureHelpHandler : SignatureHelpHandlerBase
{
    private readonly DocumentStore _documents;

    public VbHtmlSignatureHelpHandler(DocumentStore documents) => _documents = documents;

    public override Task<SignatureHelp?> Handle(
        SignatureHelpParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return Task.FromResult<SignatureHelp?>(null);

        var offset = VbHtmlCompletionHandler.OffsetOf(document.Text, request.Position);

        return Task.FromResult(Describe(document.Text, offset));
    }

    /// <summary>The call the caret is inside, if it is one this knows.</summary>
    internal static SignatureHelp? Describe(string text, int offset)
    {
        var caret = Math.Clamp(offset, 0, text.Length);
        var before = text[..caret];

        var open = before.LastIndexOf('(');
        if (open < 0) return null;

        // A closed call before the caret means the caret is past it.
        if (before.LastIndexOf(')') > open) return null;

        var name = NameBefore(before, open);

        if (Known.TryGetValue(name, out var signature) is false) return null;

        return new SignatureHelp
        {
            Signatures = new Container<SignatureInformation>(signature),
            ActiveSignature = 0,
            ActiveParameter = before[open..].Count(c => c == ',')
        };
    }

    /// <summary>
    /// The helpers a template can call.
    ///
    /// Kept short deliberately: naming a call this server cannot check would
    /// be worse than naming none.
    /// </summary>
    private static readonly Dictionary<string, SignatureInformation> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Html.Raw"] = Signature(
                "Html.Raw(value As String) As String",
                "Writes the value without HTML-encoding it.",
                ("value", "The markup to write as it stands.")),

            ["Html.Encode"] = Signature(
                "Html.Encode(value As String) As String",
                "Encodes the value so it appears as text rather than markup.",
                ("value", "The text to encode.")),

            ["String.Format"] = Signature(
                "String.Format(format As String, ParamArray args() As Object) As String",
                "Formats a string with the given values.",
                ("format", "The format string."),
                ("args", "The values to insert."))
        };

    private static SignatureInformation Signature(
        string label, string documentation, params (string Name, string Description)[] parameters) =>
        new()
        {
            Label = label,
            Documentation = new StringOrMarkupContent(documentation),
            Parameters = new Container<ParameterInformation>(
                parameters.Select(p => new ParameterInformation
                {
                    Label = new ParameterInformationLabel(p.Name),
                    Documentation = new StringOrMarkupContent(p.Description)
                }))
        };

    /// <summary>The dotted name written just before a bracket.</summary>
    private static string NameBefore(string text, int open)
    {
        var end = open;
        while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;

        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1])
                          || text[start - 1] is '_' or '.'))
            start--;

        return text[start..end];
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = Selector.ForVbHtml,
            TriggerCharacters = new Container<string>("(", ",")
        };
}
