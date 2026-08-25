using System.Reflection;
using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

/// <summary>
/// That the language server offers what it is supposed to.
///
/// Removing a handler used to break nothing: the server still started, the
/// editor simply stopped offering the feature, and no test noticed. This
/// pins the set so a deletion has to be deliberate.
/// </summary>
public class LanguageServerHandlerTests
{
    /// <summary>
    /// The handlers the server registers, found by looking at the assembly
    /// rather than by reading Program.cs — a handler that exists but is not
    /// registered is the failure worth catching, and only the registration
    /// list can show it.
    /// </summary>
    private static IReadOnlyList<string> Registered()
    {
        var source = ProgramSource();

        var names = new List<string>();

        var at = 0;

        while (true)
        {
            const string marker = ".WithHandler<";

            var start = source.IndexOf(marker, at, StringComparison.Ordinal);

            if (start < 0) break;

            start += marker.Length;

            var end = source.IndexOf('>', start);

            if (end < 0) break;

            names.Add(source[start..end]);
            at = end;
        }

        return names;
    }

    private static string ProgramSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src", "Basalt.Razor.Vb.LanguageServer", "Program.cs");

            if (File.Exists(candidate)) return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The language server's Program.cs was not found.");
    }

    [Fact]
    public void RegistersEveryHandlerTheServerIsMeantToOffer()
    {
        string[] expected =
        [
            "VbHtmlTextDocumentHandler",
            "VbHtmlCompletionHandler",
            "VbHtmlSemanticTokensHandler",
            "VbHtmlDefinitionHandler",
            "VbHtmlSignatureHelpHandler",
            "VbHtmlDocumentHighlightHandler",
            "VbHtmlLinkedEditingRangeHandler",
            "VbHtmlDocumentSymbolHandler",
            "VbHtmlFoldingRangeHandler",
        ];

        var registered = Registered();

        foreach (var handler in expected)
            Assert.Contains(handler, registered);
    }

    [Fact]
    public void RegistersEveryHandlerTheAssemblyDefines()
    {
        // The other direction: a handler written and then never wired up is
        // dead code that looks like a feature.
        var defined = typeof(VbHtmlDefinitionHandler).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true }
                     && t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(t => t.Name)
            .ToList();

        var registered = Registered();

        foreach (var handler in defined)
            Assert.Contains(handler, registered);
    }
}
