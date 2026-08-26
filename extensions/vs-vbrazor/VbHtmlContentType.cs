using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace Basalt.Razor.Vb.VisualStudio;

/// <summary>
/// Tells Visual Studio that .vbhtml views and .vbp pages are a language of
/// their own.
///
/// Without a content type the editor treats them as plain text, and no
/// colouring, completion or diagnostics can attach to them.
/// </summary>
public static class VbHtmlContentDefinition
{
    public const string ContentTypeName = "vbhtml";

    [Export]
    [Name(ContentTypeName)]

    // Based on "code" rather than "text": it brings the editor features a
    // language file is expected to have, such as an outline and brace
    // matching.
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    internal static ContentTypeDefinition? VbHtmlContentType { get; set; }

    [Export]
    [FileExtension(".vbhtml")]
    [ContentType(ContentTypeName)]
    internal static FileExtensionToContentTypeDefinition? VbHtmlFileExtension { get; set; }

    // .vbp pages, on the same content type: the server answers both from the
    // same parser, and registering only the view left pages as plain text in
    // Visual Studio — no colouring, no completion, and no formatting — while
    // the other three editors handled them.
    // Blazor components: the same syntax as a view, compiled into a different
    // class, so they share the content type and the server behind it.
    [Export]
    [FileExtension(".vbrazor")]
    [ContentType(ContentTypeName)]
    internal static FileExtensionToContentTypeDefinition? VbComponentFileExtension { get; set; }

    [Export]
    [FileExtension(".vbp")]
    [ContentType(ContentTypeName)]
    internal static FileExtensionToContentTypeDefinition? VbPageFileExtension { get; set; }
}
