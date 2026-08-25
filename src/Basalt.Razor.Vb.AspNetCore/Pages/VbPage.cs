using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;

namespace Basalt.Web;

/// <summary>
/// A page written the way Classic ASP and PHP are: markup with code in it.
/// </summary>
/// <remarks>
/// One file, one URL, read top to bottom. No controller to route through and
/// no model to declare — the thing that made those two easy to begin with, and
/// that ASP.NET Core has no equivalent for.
///
/// What is deliberately not carried over: Response.Write does not skip
/// encoding, there is no ambient Session or Application, and Option Strict
/// stays on. The generated page reaches everything through properties, so
/// what a page touches can be read off its own source.
/// </remarks>
public abstract class VbPage
{
    private HttpContext _context = null!;

    /// <summary>The request being answered.</summary>
    public HttpRequest Request => _context.Request;

    /// <summary>The response being written.</summary>
    public HttpResponse Response => _context.Response;

    /// <summary>Everything ASP.NET Core knows about this request.</summary>
    public HttpContext Context => _context;

    /// <summary>Where the output goes.</summary>
    protected TextWriter Output { get; private set; } = TextWriter.Null;

    /// <summary>Writes the page. Generated from the .vbp file.</summary>
    public abstract Task RenderAsync();

    /// <summary>Runs the page against a request.</summary>
    public async Task ExecuteAsync(HttpContext context, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        _context = context;
        Output = output;

        await RenderAsync().ConfigureAwait(false);
    }

    /// <summary>Writes markup exactly as the page wrote it.</summary>
    protected void WriteLiteral(string text) => Output.Write(text);

    /// <summary>
    /// Writes a value, HTML-encoded.
    /// </summary>
    /// <remarks>
    /// Encoded by default, which is where this parts company with Classic
    /// ASP: there, Response.Write of a query string parameter was a
    /// cross-site scripting hole, and it was the normal way to write a page.
    /// </remarks>
    protected void Write(object? value)
    {
        if (value is null) return;

        Output.Write(HtmlEncoder.Default.Encode(AsText(value)));
    }

    /// <summary>
    /// A value as text, in a form a browser reads the same everywhere.
    /// </summary>
    /// <remarks>
    /// Invariant culture, not the server's: a price written on a machine set
    /// to Italian came out as "14,49", which a script reading the page parses
    /// as a different number, and which a form posts back unparseable. A page
    /// that wants a local format asks for one.
    /// </remarks>
    private static string AsText(object value) => value switch
    {
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>
    /// Writes a value without encoding it, for markup the page built itself.
    /// </summary>
    protected void WriteRaw(object? value)
    {
        if (value is null) return;

        Output.Write(AsText(value));
    }

    /// <summary>Sends the browser somewhere else.</summary>
    protected void Redirect(string location) => Response.Redirect(location);
}
