using System.Text.Json;

namespace Basalt.Tests;

/// <summary>
/// The templates a user gets from <c>dotnet new</c>.
/// </summary>
/// <remarks>
/// Checked as files rather than by running dotnet new: generating, restoring
/// and building three projects takes minutes and needs a package feed, which
/// is a job for the release pipeline. What is worth guarding here is that the
/// metadata stays valid and the templates keep declaring Visual Basic, since
/// a template that claims C# is offered to the wrong people and never appears
/// for the right ones.
/// </remarks>
public sealed class TemplateTests
{
    private static string TemplateRoot =>
        Path.Combine(RepositoryRoot(), "templates", "content");

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (directory is { Length: > 0 })
        {
            if (File.Exists(Path.Combine(directory, "Basalt.slnx"))) return directory;

            directory = Path.GetDirectoryName(directory)!;
        }

        throw new InvalidOperationException("the repository root was not found");
    }

    public static TheoryData<string> Templates()
    {
        var data = new TheoryData<string>();

        foreach (var directory in Directory.GetDirectories(TemplateRoot))
            data.Add(Path.GetFileName(directory));

        return data;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void TheMetadataIsValidJson(string name)
    {
        var path = Path.Combine(TemplateRoot, name, ".template.config", "template.json");

        Assert.True(File.Exists(path), $"{name} has no template.json");

        var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.True(document.RootElement.TryGetProperty("shortName", out _));
        Assert.True(document.RootElement.TryGetProperty("identity", out _));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryTemplateIsVisualBasic(string name)
    {
        // The whole point: dotnet new offers no Visual Basic web template at
        // all, and a template tagged C# would not close that gap.
        var path = Path.Combine(TemplateRoot, name, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        var language = document.RootElement
            .GetProperty("tags").GetProperty("language").GetString();

        Assert.Equal("VB", language);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void TheProjectFileMatchesTheSourceName(string name)
    {
        // sourceName is what dotnet new renames; if the .vbproj is named
        // anything else the generated project keeps the template's name.
        var path = Path.Combine(TemplateRoot, name, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        var sourceName = document.RootElement.GetProperty("sourceName").GetString();

        Assert.True(
            File.Exists(Path.Combine(TemplateRoot, name, $"{sourceName}.vbproj")),
            $"{name} has no {sourceName}.vbproj");
    }

    [Fact]
    public void ShortNamesAreDistinct()
    {
        var names = Directory.GetDirectories(TemplateRoot)
            .Select(d => Path.Combine(d, ".template.config", "template.json"))
            .Select(p => JsonDocument.Parse(File.ReadAllText(p)))
            .Select(d => d.RootElement.GetProperty("shortName").GetString())
            .ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void TheMvcTemplateUsesTheOneCallSetup()
    {
        // AddVbViews is the whole setup a user has to know about. If the
        // template drifts from it the site compiles and then serves nothing
        // but "view not found".
        var program = File.ReadAllText(
            Path.Combine(TemplateRoot, "BasaltVbMvc", "Program.vb"));

        Assert.Contains("AddVbViews()", program);
    }

    [Fact]
    public void TheMvcTemplateHasNoRawTildePaths()
    {
        // "~/css/site.css" is resolved by a tag helper in C# projects. We
        // have no tag helpers yet, so a raw tilde reaches the browser as a
        // literal path and the stylesheet silently fails to load.
        var layout = File.ReadAllText(Path.Combine(
            TemplateRoot, "BasaltVbMvc", "Views", "Shared", "_Layout.vbhtml"));

        Assert.DoesNotContain("href=\"~/", layout);
        Assert.DoesNotContain("src=\"~/", layout);
    }

    [Fact]
    public void TheRazorPagesTemplateMarksItsPages()
    {
        // A page without @Page is an MVC view: it compiles, and nothing ever
        // routes to it.
        var pages = Directory.GetFiles(
            Path.Combine(TemplateRoot, "BasaltVbRazorPages", "Pages"), "*.vbhtml");

        foreach (var page in pages)
        {
            // The shared files are not pages.
            if (Path.GetFileName(page).StartsWith('_')) continue;

            Assert.Contains("@Page", File.ReadAllText(page));
        }
    }

    [Fact]
    public void EveryTemplateThatServesViewsCallsAddVbViews()
    {
        foreach (var name in new[] { "BasaltVbMvc", "BasaltVbRazorPages" })
        {
            var program = File.ReadAllText(Path.Combine(TemplateRoot, name, "Program.vb"));

            Assert.Contains("AddVbViews()", program);
        }
    }

    [Fact]
    public void ThePagesTemplateServesFromDiskWhileDeveloping()
    {
        // The whole reason for this template: a file is a page, and saving it
        // is enough to see the change. Mapping the compiled pages as well
        // would defeat it — they answer first and the edited file is never
        // read.
        var program = File.ReadAllText(
            Path.Combine(TemplateRoot, "BasaltVbPages", "Program.vb"));

        Assert.Contains("MapVbPagesFromDisk", program, StringComparison.Ordinal);
        Assert.Contains("IsDevelopment", program, StringComparison.Ordinal);
        Assert.Contains("MapVbPages()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePagesTemplateKnowsWhereItIs()
    {
        // The generator turns a page's full path into its URL, and only
        // MSBuild knows where the project sits. Without this every page is
        // registered at the wrong address.
        var project = File.ReadAllText(
            Path.Combine(TemplateRoot, "BasaltVbPages", "BasaltVbPages.vbproj"));

        Assert.Contains("BasaltProjectDir", project, StringComparison.Ordinal);
        Assert.Contains("CompilerVisibleProperty", project, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageInThePagesTemplateIsAPage()
    {
        // A .vbp under Pages that the generator will not compile is a file
        // that silently answers nothing.
        var pages = Directory.GetFiles(
            Path.Combine(TemplateRoot, "BasaltVbPages", "Pages"), "*.vbp",
            SearchOption.AllDirectories);

        Assert.NotEmpty(pages);

        foreach (var page in pages)
            Assert.Contains("<html", File.ReadAllText(page), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("BasaltVbMvc", "mvc", "Microsoft.Web.Mvc")]
    [InlineData("BasaltVbRazorPages", "webapp", "Microsoft.Web.RazorPages")]
    [InlineData("BasaltVbWebApi", "webapi", "Microsoft.Web.WebApi")]
    [InlineData("BasaltVbWeb", "web", "Microsoft.Web.Empty")]
    public void TheWebTemplatesJoinTheOfficialGroups(
        string folder, string shortName, string group)
    {
        // How a language appears beside the others: C# and F# share a
        // groupIdentity, and "dotnet new mvc -lang VB" only works if VB is in
        // that same group. A template of its own called vbmvc works too, but
        // nobody looking for MVC in Visual Basic would ever find it.
        var path = Path.Combine(TemplateRoot, folder, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal(group, document.RootElement.GetProperty("groupIdentity").GetString());
        Assert.Equal(shortName, document.RootElement.GetProperty("shortName").GetString());
    }

    [Theory]
    [InlineData("BasaltVbMvc")]
    [InlineData("BasaltVbRazorPages")]
    [InlineData("BasaltVbWebApi")]
    [InlineData("BasaltVbWeb")]
    public void ALanguageInAGroupIsAPeerOfTheOthers(string folder)
    {
        // The .NET 10 SDK's own web templates sit at 10000, C# and F# alike:
        // a language in a group is a peer, not a fallback. Below their number
        // the tooling lists the group without it, which is what kept Visual
        // Basic out of Rider's New Solution dialog.
        //
        // 9900 is a number worth not mistaking for it: that is what the .NET
        // 9 templates Rider bundles use, and matching those instead left the
        // dialog exactly as it was.
        //
        // Equal rather than higher: at a tie the SDK still defaults to C#,
        // so "dotnet new mvc" with no language is unchanged — checked, not
        // assumed.
        var path = Path.Combine(TemplateRoot, folder, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        var precedence = int.Parse(document.RootElement.GetProperty("precedence").GetString()!);

        Assert.Equal(10000, precedence);
    }

    [Fact]
    public void ThePagesTemplateKeepsItsOwnGroup()
    {
        // No official counterpart to join: a site of .vbp pages is not a
        // Visual Basic spelling of anything Microsoft ships.
        var path = Path.Combine(
            TemplateRoot, "BasaltVbPages", ".template.config", "template.json");

        var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal("vbpages", document.RootElement.GetProperty("shortName").GetString());

        Assert.False(
            document.RootElement.TryGetProperty("groupIdentity", out var group)
            && group.GetString()?.StartsWith("Microsoft.", StringComparison.Ordinal) == true,
            "it joined a Microsoft group it has no counterpart in");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryTemplateOffersTheFrameworkRiderRunsOn(string name)
    {
        // Rider bundles its own .NET 9 and offers net9.0 in the New Solution
        // dialog. A template that only knows net10.0 is filtered out of that
        // list — which is why Visual Basic never appeared beside C# and F#,
        // however right the group and the precedence were.
        var path = Path.Combine(TemplateRoot, name, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        var choices = document.RootElement
            .GetProperty("symbols").GetProperty("Framework").GetProperty("choices")
            .EnumerateArray()
            .Select(c => c.GetProperty("choice").GetString())
            .ToList();

        Assert.Contains("net10.0", choices);
        Assert.Contains("net9.0", choices);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void TheNewestFrameworkIsTheDefault(string name)
    {
        var path = Path.Combine(TemplateRoot, name, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        var framework = document.RootElement.GetProperty("symbols").GetProperty("Framework");

        Assert.Equal("net10.0", framework.GetProperty("defaultValue").GetString());

        // And the project file has to say the same, or the substitution has
        // nothing to replace.
        var project = Directory
            .GetFiles(Path.Combine(TemplateRoot, name), "*.vbproj")
            .Single();

        Assert.Contains("net10.0", File.ReadAllText(project), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EachSaysWhatItProduced(string name)
    {
        // primaryOutputs is what an IDE reads to know which project to add to
        // the solution and which file to open. Microsoft's own Visual Basic
        // templates declare it; mine did not, and Rider's New Solution dialog
        // showed no VB at all — with the group, the precedence and the
        // frameworks all already right.
        var path = Path.Combine(TemplateRoot, name, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        var outputs = document.RootElement.GetProperty("primaryOutputs")
            .EnumerateArray()
            .Select(o => o.GetProperty("path").GetString()!)
            .ToList();

        Assert.NotEmpty(outputs);

        var project = document.RootElement.GetProperty("sourceName").GetString() + ".vbproj";

        Assert.Contains(project, outputs);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryOutputIsAFileTheTemplateHas(string name)
    {
        // A path that is not there means an IDE opening nothing, or worse
        // reporting that the template is broken after it has already
        // generated the project.
        var folder = Path.Combine(TemplateRoot, name);
        var path = Path.Combine(folder, ".template.config", "template.json");
        var document = JsonDocument.Parse(File.ReadAllText(path));

        var sourceName = document.RootElement.GetProperty("sourceName").GetString()!;

        foreach (var output in document.RootElement.GetProperty("primaryOutputs").EnumerateArray())
        {
            var relative = output.GetProperty("path").GetString()!;

            // The project file is named after sourceName, which is what
            // dotnet new renames; on disk it is still the template's own.
            var onDisk = relative.Replace(sourceName, sourceName, StringComparison.Ordinal);

            Assert.True(
                File.Exists(Path.Combine(folder, onDisk)),
                $"{name} promises {relative}, which is not in the template");
        }
    }
}
