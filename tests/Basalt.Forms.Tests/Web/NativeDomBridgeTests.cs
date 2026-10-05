using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Web;

public class NativeDomBridgeTests
{
    [AvaloniaFact]
    public void Common_document_and_element_calls_escape_arguments_as_json()
    {
        using var browser = new WebBrowser();
        var scripts = new List<string>();
        browser.CompleteDomDocumentForTests(script =>
        {
            scripts.Add(script);
            if (script.Contains("d.put(n)", StringComparison.Ordinal)) return Task.FromResult<string?>("7");
            if (script.Contains(".length", StringComparison.Ordinal)) return Task.FromResult<string?>("1");
            return Task.FromResult<string?>("\"sample\"");
        });

        var document = Assert.IsType<HtmlDocument>(browser.Document);
        Assert.Equal("sample", document.Title);
        document.Title = "a\"b\\c";
        var element = Assert.IsType<HtmlElement>(document.GetElementById("a\"b\\c"));
        Assert.Equal("sample", element.InnerText);
        element.SetAttribute("data-x", "a\"b\\c");
        Assert.Equal("sample", document.InvokeScript("f\"n", ["a\"b\\c"]));
        Assert.Single(document.GetElementsByTagName("span").Cast<HtmlElement>());

        var encoded = System.Text.Json.JsonSerializer.Serialize("a\"b\\c");
        Assert.Contains(scripts, script => script.Contains($"document.getElementById({encoded})", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains($".setAttribute(\"data-x\",{encoded})", StringComparison.Ordinal));
        Assert.Contains(scripts, script => script.Contains($"window[{System.Text.Json.JsonSerializer.Serialize("f\"n")}];", StringComparison.Ordinal)
                                           && script.Contains($"f.apply(window,[{encoded}])", StringComparison.Ordinal));
        Assert.DoesNotContain(scripts, script => script.Contains("document.getElementById(\"a\"b", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Document_wrappers_expire_when_same_html_is_loaded_again()
    {
        using var browser = new WebBrowser();
        browser.CompleteDomDocumentForTests(script => Task.FromResult<string?>(
            script.Contains("d.put(n)", StringComparison.Ordinal) ? "1" : "\"before\""));
        var oldDocument = browser.Document!;
        var oldElement = oldDocument.Body!;
        browser.DocumentText = "<p>same URL</p>";
        Assert.Null(browser.Document);
        browser.CompleteDomDocumentForTests(_ => Task.FromResult<string?>("\"after\""));

        Assert.Throws<InvalidOperationException>(() => _ = oldDocument.Title);
        Assert.Throws<InvalidOperationException>(() => _ = oldElement.InnerText);
        Assert.Equal("after", browser.Document!.Title);
    }

    [AvaloniaFact]
    public void Navigation_during_script_completion_invalidates_result()
    {
        using var browser = new WebBrowser();
        browser.CompleteDomDocumentForTests(_ =>
        {
            browser.DocumentText = "<p>next document</p>";
            return Task.FromResult<string?>("\"stale\"");
        });

        Assert.Throws<InvalidOperationException>(() => _ = browser.Document!.Title);
    }

    [AvaloniaFact]
    public void Nested_DOM_call_is_rejected_instead_of_reentering_script_engine()
    {
        using var browser = new WebBrowser();
        browser.CompleteDomDocumentForTests(_ =>
        {
            Assert.Throws<InvalidOperationException>(() => _ = browser.Document!.Title);
            return Task.FromResult<string?>("\"outer\"");
        });

        Assert.Equal("outer", browser.Document!.Title);
    }

    [AvaloniaFact]
    public void Script_call_has_a_bounded_timeout()
    {
        using var browser = new WebBrowser { DomScriptTimeout = TimeSpan.FromMilliseconds(25) };
        browser.CompleteDomDocumentForTests(_ => new TaskCompletionSource<string?>().Task);

        Assert.Throws<TimeoutException>(() => _ = browser.Document!.Title);
        Assert.Throws<InvalidOperationException>(() => _ = browser.Document!.Title);
    }
}
