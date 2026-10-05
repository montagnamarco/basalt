using System.Text.Json;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>Evaluates small DOM operations against one loaded native document.</summary>
internal sealed class NativeDomBridge(WebBrowser browser, long generation)
{
    private bool _executing;
    private bool _timedOut;

    internal static string Literal(string? value) => JsonSerializer.Serialize(value);

    internal JsonElement Evaluate(string expression)
    {
        EnsureCurrent();
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("HTML DOM calls must run on the browser's UI thread.");
        if (_executing) throw new InvalidOperationException("A DOM call cannot be nested inside another DOM call.");

        // JSON gives the same result shape on WebKit, WebView2 and WebKitGTK,
        // including null and arrays. Arguments are always serialized as JS
        // literals, never interpolated as executable source.
        var script = $"JSON.stringify((()=>{{return ({expression});}})())";
        _executing = true;
        try
        {
            var result = SynchronousWait.For(browser.EvaluateDomScript(script).WaitAsync(browser.DomScriptTimeout));
            EnsureCurrent();
            if (result is null) return JsonDocument.Parse("null").RootElement.Clone();
            using var parsed = JsonDocument.Parse(result);
            return parsed.RootElement.Clone();
        }
        catch (TimeoutException)
        {
            // The platform API has no cancellation hook. The timed-out script
            // may still finish, so do not start another call on this document.
            _timedOut = true;
            throw;
        }
        finally
        {
            _executing = false;
        }
    }

    internal string? String(string expression)
    {
        var result = Evaluate(expression);
        return result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : result.GetString();
    }

    internal int Register(string expression) => Evaluate($"(()=>{{const n={expression};if(!n)return 0;" +
        "const d=globalThis.__basaltDom??=(function(){let next=1;const refs=new Map();return {put(n){const id=next++;refs.set(id,typeof WeakRef==='function'?new WeakRef(n):{deref:()=>n});return id},get(id){return refs.get(id)?.deref()}}})();" +
        "return d.put(n)})()").GetInt32();

    internal string Node(int id) =>
        $"(()=>{{const n=globalThis.__basaltDom?.get({id});if(!n)throw new Error('HTML element is no longer available');return n}})()";

    internal void EnsureCurrent()
    {
        if (browser.IsDisposed) throw new ObjectDisposedException(nameof(WebBrowser));
        if (_timedOut) throw new InvalidOperationException("The previous DOM call timed out; reload the document before using it again.");
        if (browser.DomGeneration != generation || browser.Document is null)
            throw new InvalidOperationException("The HTML document is no longer current.");
    }
}
