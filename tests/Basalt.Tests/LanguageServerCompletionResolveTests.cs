using System.Text.Json;
using System.Text.Json.Nodes;
using Basalt.Razor.Vb.LanguageServer;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspHarness = Basalt.Tests.LanguageServerProtocolTests.LspHarness;

namespace Basalt.Tests;

public sealed class LanguageServerCompletionResolveTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-lsp-resolve-" + Guid.NewGuid().ToString("N"));

    public LanguageServerCompletionResolveTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "Site.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Site</RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), """
            Imports Microsoft.AspNetCore.Builder
            Public Module Program
                Public Sub Main(args As String())
                    WebApplication.CreateBuilder(args).Build().Run()
                End Sub
            End Module
            Public Class Customer
                ''' <summary>Formats the customer greeting.</summary>
                Public Function FormatGreeting(prefix As String) As String
                    Return prefix
                End Function
            End Class
            Public Class Invoice
                ''' <summary>Formats the invoice count.</summary>
                Public Function FormatGreeting(count As Integer) As Integer
                    Return count
                End Function
            End Class
            """);
    }

    public void Dispose() => ScratchFolder.Delete(_root);

    private string UriFor(string name) => new Uri(Path.Combine(_root, name)).AbsoluteUri;

    [Fact]
    public async Task ConcurrentProtocolCompletionsResolveTheirOwnMappedMethodAndPreserveFields()
    {
        await using var client = new LspHarness();
        var initialized = await client.InitializeAsync(_root);
        Assert.True(initialized.GetProperty("capabilities").GetProperty("completionProvider")
            .GetProperty("resolveProvider").GetBoolean());
        await client.InitializedAsync();

        var customerUri = UriFor("Customer.vbhtml");
        var invoiceUri = UriFor("Invoice.vbhtml");
        // The astral character ahead of the expression counts as two UTF-16
        // units. The real method reached through this mapping proves the caret.
        const string line = "<p>😀 @Model.For</p>";
        await client.DidOpenAsync(customerUri, "@ModelType Global.Site.Customer\n" + line);
        await client.DidOpenAsync(invoiceUri, "@ModelType Global.Site.Invoice\n" + line);
        var column = line.IndexOf("Model.For", StringComparison.Ordinal) + "Model.For".Length;
        var items = await Task.WhenAll(
            MethodItemAsync(client, customerUri, column),
            MethodItemAsync(client, invoiceUri, column));

        var requests = items.Select(item => JsonNode.Parse(item.GetRawText())!.AsObject()).ToArray();
        foreach (var request in requests)
        {
            request["sortText"] = "0001";
            request["filterText"] = "greeting";
            request["textEdit"] = JsonSerializer.SerializeToNode(new
            {
                range = new { start = new { line = 1, character = column }, end = new { line = 1, character = column } },
                newText = "FormatGreeting"
            });
        }
        var resolved = await Task.WhenAll(requests.Select(request =>
            client.RequestAsync("completionItem/resolve", request)));
        Assert.Contains("prefix As String", resolved[0].GetProperty("detail").GetString());
        Assert.Contains("count As Integer", resolved[1].GetProperty("detail").GetString());
        Assert.Contains("Formats the customer greeting.", Documentation(resolved[0]));
        Assert.Contains("Formats the invoice count.", Documentation(resolved[1]));
        for (var index = 0; index < resolved.Length; index++)
            foreach (var field in new[] { "label", "insertText", "kind", "data", "sortText", "filterText", "textEdit" })
                Assert.True(JsonNode.DeepEquals(requests[index][field],
                    JsonNode.Parse(resolved[index].GetProperty(field).GetRawText())), field);

        var unknown = JsonNode.Parse(items[0].GetRawText())!.AsObject();
        unknown["data"]!.AsObject()["batch"] = "unknown";
        var unchanged = await client.RequestAsync("completionItem/resolve", unknown);
        Assert.False(unchanged.TryGetProperty("detail", out _));
        Assert.True(JsonNode.DeepEquals(unknown, JsonNode.Parse(unchanged.GetRawText())));

        // Eight retained batches is the upper bound. A ninth new request
        // evicts the original item, which must remain unresolved afterward.
        for (var index = 0; index < 9; index++)
            await MethodItemAsync(client, customerUri, column);
        var stale = await client.RequestAsync("completionItem/resolve", items[0]);
        Assert.False(stale.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task ChangedClosedAndMalformedItemsDoNotResolveAndCancellationIsObserved()
    {
        using var compilation = new ProjectCompilation();
        compilation.StartLoading(_root);
        await compilation.Loaded;
        Assert.True(compilation.IsReady, compilation.Problem);
        var documents = new DocumentStore();
        var documentUri = DocumentUri.From(UriFor("Test.vbhtml"));
        var uri = documentUri.ToString();
        const string text = "@ModelType Global.Site.Customer\n<p>@Model.For</p>";
        documents.Update(uri, text, 1);
        var handler = new VbHtmlCompletionHandler(documents, compilation);
        var parameters = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier(documentUri),
            Position = new Position(1, 13)
        };
        var initialItems = await handler.Handle(parameters, default);
        Assert.Contains(initialItems, item => item.Label == "FormatGreeting");
        var item = initialItems.Single(item => item.Label == "FormatGreeting");
        documents.Update(uri, text, 2);
        Assert.Same(item, await handler.Handle(item, default));
        Assert.Null(item.Detail);

        item = (await handler.Handle(parameters, default)).Single(item => item.Label == "FormatGreeting");
        await compilation.FileChangedAsync(Path.Combine(_root, "Program.vb"));
        Assert.Same(item, await handler.Handle(item, default));
        Assert.Null(item.Detail);

        item = (await handler.Handle(parameters, default)).Single(item => item.Label == "FormatGreeting");
        documents.Remove(uri);
        Assert.Same(item, await handler.Handle(item, default));
        Assert.Null(item.Detail);
        item = item with
        {
            Data = new JObject { ["batch"] = "unknown", ["index"] = JToken.Parse("9999999999999999999999999999") }
        };
        Assert.Same(item, await handler.Handle(item, default));
        item = item with { Data = new JArray("malformed") };
        Assert.Same(item, await handler.Handle(item, default));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Handle(item, canceled.Token));
    }

    private static string Documentation(JsonElement item)
    {
        var documentation = item.GetProperty("documentation");
        return documentation.ValueKind == JsonValueKind.String
            ? documentation.GetString()! : documentation.GetProperty("value").GetString()!;
    }

    private static async Task<JsonElement> MethodItemAsync(LspHarness client, string uri, int column)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var completion = await client.RequestAsync("textDocument/completion", new
            {
                textDocument = new { uri }, position = new { line = 1, character = column }
            });
            var items = completion.ValueKind == JsonValueKind.Array ? completion : completion.GetProperty("items");
            var method = items.EnumerateArray().FirstOrDefault(item => item.GetProperty("label").GetString() == "FormatGreeting");
            if (method.ValueKind != JsonValueKind.Undefined) return method.Clone();
            Assert.True(DateTime.UtcNow < deadline, "The real VB model method was not offered after loading.");
            await Task.Delay(100);
        }
    }
}
