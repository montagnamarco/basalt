namespace Basalt.Tests;

/// <summary>
/// What the extension READMEs tell someone to run.
/// </summary>
/// <remarks>
/// Build instructions rot quietly: the Rider README said to run ./gradlew for
/// months while no wrapper was ever committed, and the only way to find out
/// was to try it on a machine that had never built the plugin. These check
/// the claims against the folder.
/// </remarks>
public sealed class ExtensionReadmeTests
{
    private static string Root()
    {
        var directory = AppContext.BaseDirectory;

        while (directory is { Length: > 0 })
        {
            if (File.Exists(Path.Combine(directory, "Basalt.slnx")))
                return Path.Combine(directory, "extensions");

            directory = Path.GetDirectoryName(directory)!;
        }

        throw new InvalidOperationException("the repository root was not found");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Root(), .. parts]));

    public static TheoryData<string> Extensions() =>
        ["vscode-vbrazor", "rider-vbrazor", "vs-vbrazor"];

    [Theory]
    [MemberData(nameof(Extensions))]
    public void EachHasAReadme(string extension)
    {
        Assert.True(File.Exists(Path.Combine(Root(), extension, "README.md")),
            $"{extension} has no README");
    }

    [Theory]
    [MemberData(nameof(Extensions))]
    public void EachSaysWhatToInstallFirst(string extension)
    {
        // Someone who has never built this before is the reader: a build
        // command with no prerequisites is a command that fails on the first
        // machine that lacks the tool.
        var readme = Read(extension, "README.md");

        Assert.Contains("What you need first", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRiderReadmeDoesNotPromiseAWrapperThatIsNotThere()
    {
        // It did, for months. ./gradlew is the first thing anyone types.
        var wrapper = Path.Combine(Root(), "rider-vbrazor", "gradlew");
        var readme = Read("rider-vbrazor", "README.md");

        if (File.Exists(wrapper)) return;

        Assert.Contains("no `gradlew` wrapper", readme, StringComparison.Ordinal);
        Assert.Contains("gradle wrapper --gradle-version", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVisualStudioReadmeSaysHowToMakeTheMissingProject()
    {
        // The project file is deliberately absent — it cannot be restored off
        // Windows — so the README has to say how to create one.
        var readme = Read("vs-vbrazor", "README.md");

        Assert.Contains("dotnet new vsix", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOverviewAgreesWithTheFolders()
    {
        var overview = Read("README.md");

        foreach (var extension in new[] { "vscode-vbrazor", "rider-vbrazor", "vs-vbrazor" })
            Assert.True(Directory.Exists(Path.Combine(Root(), extension)),
                $"the overview names {extension}, which is not there");

        // The server is what all three need and none of them ships.
        Assert.Contains("dotnet publish", overview, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Extensions))]
    public void NoPlaceholderIsLeftForTheReaderToGuess(string extension)
    {
        // "-r <runtime-identifier>" tells nobody what to type there. Either
        // name a real one, as the Visual Studio README does with win-x64
        // because that is the only machine it runs on, or list the choices.
        var readme = Read(extension, "README.md");

        if (!readme.Contains("dotnet publish", StringComparison.Ordinal)) return;

        var explained = readme.Contains("osx-arm64", StringComparison.Ordinal)
                     || !readme.Contains("<runtime-identifier>", StringComparison.Ordinal);

        Assert.True(explained, $"{extension} asks for a runtime identifier without saying which");
    }

    [Fact]
    public void TheRiderPluginHasItsWrapper()
    {
        // ./gradlew is the first thing anyone types, and for months it was
        // not there.
        Assert.True(
            File.Exists(Path.Combine(Root(), "rider-vbrazor", "gradlew")),
            "the Rider README says ./gradlew, and there is no wrapper");
    }

    [Fact]
    public void TheRiderPluginAndItsPlatformAgreeOnKotlin()
    {
        // Rider 2025.2 is built with Kotlin 2.2, and compiling against it
        // with an older compiler fails on every module the platform ships:
        // "binary version of its metadata is 2.2.0, expected 2.0.0". The two
        // versions have to move together.
        var build = Read("rider-vbrazor", "build.gradle.kts");

        var kotlin = System.Text.RegularExpressions.Regex
            .Match(build, @"kotlin\.jvm""\) version ""(\d+\.\d+)").Groups[1].Value;

        var platform = System.Text.RegularExpressions.Regex
            .Match(build, @"rider\(""(\d{4})\.").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(kotlin), "no Kotlin version in build.gradle.kts");
        Assert.False(string.IsNullOrEmpty(platform), "no Rider version in build.gradle.kts");

        // 2025 platforms need Kotlin 2.2 or later; anything older is the
        // failure above.
        if (int.Parse(platform) >= 2025)
            Assert.True(string.CompareOrdinal(kotlin, "2.2") >= 0,
                $"Rider {platform} needs Kotlin 2.2 or later, not {kotlin}");
    }

    [Fact]
    public void TheRiderWrapperIsNewEnoughForItsPlugin()
    {
        // The IntelliJ Platform plugin from 2.18 needs Gradle 9, and the
        // failure names neither the plugin nor what to do about it.
        var properties = Read("rider-vbrazor", "gradle", "wrapper", "gradle-wrapper.properties");

        var gradle = System.Text.RegularExpressions.Regex
            .Match(properties, @"gradle-(\d+)\.").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(gradle), "no Gradle version in the wrapper");
        Assert.True(int.Parse(gradle) >= 9, $"Gradle {gradle} is too old for the platform plugin");
    }

    [Fact]
    public void TheRiderPluginAsksForColouring()
    {
        // The platform requests semantic tokens only if the descriptor says
        // it wants them. Without this the file type is registered, the server
        // runs and answers, and the text stays black — a plugin that installs
        // cleanly and appears to do nothing.
        var descriptor = Read(
            "rider-vbrazor", "src", "main", "kotlin", "com", "basalt", "vbrazor",
            "VbHtmlLspServerSupportProvider.kt");

        Assert.Contains("lspSemanticTokensSupport", descriptor, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRiderPluginRegistersTheFileTypeWithoutANamedField()
    {
        // A Kotlin object cannot declare an INSTANCE of its own — the
        // compiler generates one — so plugin.xml must not name a fieldName.
        // It did, pointing at a field that had been removed to make the
        // Kotlin compile: the file type would not have registered at all.
        var manifest = Read(
            "rider-vbrazor", "src", "main", "resources", "META-INF", "plugin.xml");

        Assert.Contains("VbHtmlFileType", manifest, StringComparison.Ordinal);

        // The attribute, not the word: the comment above it explains why it
        // is absent, and a test that cannot tell the two apart fails on its
        // own documentation.
        Assert.DoesNotContain("fieldName=", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRiderPluginShipsTheServerBesideTheJarNotInsideIt()
    {
        // Anything under src/main/resources is packed into the jar, and a
        // file inside an archive cannot be executed. The plugin looked for
        // lib/server/…, found nothing, and started no server: it installed
        // cleanly, coloured nothing, and said nothing about why.
        var resources = Path.Combine(
            Root(), "rider-vbrazor", "src", "main", "resources", "server");

        Assert.False(Directory.Exists(resources),
            "the server is under resources, so it ends up inside the jar");

        var build = Read("rider-vbrazor", "build.gradle.kts");

        Assert.Contains("prepareSandbox", build, StringComparison.Ordinal);
        Assert.Contains("lib/server", build, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRiderPluginMakesTheServerRunnable()
    {
        // A zip does not reliably carry the executable bit: the file arrives
        // read-only, the plugin finds it and cannot start it, and the symptom
        // is identical to the server being absent.
        var provider = Read(
            "rider-vbrazor", "src", "main", "kotlin", "com", "basalt", "vbrazor",
            "VbHtmlLspServerSupportProvider.kt");

        Assert.Contains("setExecutable", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRiderPluginFindsItselfThroughThePlatform()
    {
        // Not through the jar's own CodeSource: Rider loads plugins with a
        // class loader whose getLocation() returns null, so the lookup threw
        // a NullPointerException before any file was looked for — on every
        // .vbhtml opened, with the whole feature failing silently.
        var provider = Read(
            "rider-vbrazor", "src", "main", "kotlin", "com", "basalt", "vbrazor",
            "VbHtmlLspServerSupportProvider.kt");

        Assert.Contains("PluginManagerCore", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("protectionDomain", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginIdIsTheOneTheManifestDeclares()
    {
        // The lookup above asks for the plugin by id, and a mismatch would
        // fail exactly like the plugin not being installed.
        var provider = Read(
            "rider-vbrazor", "src", "main", "kotlin", "com", "basalt", "vbrazor",
            "VbHtmlLspServerSupportProvider.kt");

        var manifest = Read(
            "rider-vbrazor", "src", "main", "resources", "META-INF", "plugin.xml");

        var id = System.Text.RegularExpressions.Regex
            .Match(manifest, @"<id>([^<]+)</id>").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(id), "the manifest declares no id");
        Assert.Contains($"\"{id}\"", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void RiderGetsTheSameGrammarAsVsCode()
    {
        // The server colours the Visual Basic and says nothing about HTML,
        // deliberately: a grammar already does that, and two answers to one
        // question can disagree. VS Code had a grammar and Rider had none, so
        // in Rider the markup showed black — fixed by shipping the same one,
        // not by making the server answer twice.
        var vsCode = Path.Combine(
            Root(), "vscode-vbrazor", "syntaxes", "vbhtml.tmLanguage.json");

        var rider = Path.Combine(
            Root(), "rider-vbrazor", "textmate", "vbhtml", "vbhtml.tmLanguage.json");

        Assert.True(File.Exists(rider), "Rider has no grammar, so its markup stays black");

        // The same file, so a view reads the same in both editors.
        Assert.Equal(File.ReadAllText(vsCode), File.ReadAllText(rider));
    }

    [Fact]
    public void TheGrammarTravelsBesideTheJar()
    {
        // Same trap as the server: anything under resources is packed inside
        // the jar, where the provider's path cannot reach it.
        var inResources = Path.Combine(
            Root(), "rider-vbrazor", "src", "main", "resources", "textmate");

        Assert.False(Directory.Exists(inResources),
            "the grammar is under resources, so it ends up inside the jar");

        Assert.Contains("lib/textmate", Read("rider-vbrazor", "build.gradle.kts"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BothFileTypesReachTheServer()
    {
        // A .vbp page has its own file type and its own grammar; without the
        // guard admitting it too, it had both and no server behind them —
        // colouring from the grammar, and nothing else at all.
        var provider = Read(
            "rider-vbrazor", "src", "main", "kotlin", "com", "basalt", "vbrazor",
            "VbHtmlLspServerSupportProvider.kt");

        Assert.Contains("\"vbp\"", provider, StringComparison.Ordinal);
        Assert.Contains("\"vbhtml\"", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void BothEditorsKnowBothFileTypes()
    {
        var riderManifest = Read(
            "rider-vbrazor", "src", "main", "resources", "META-INF", "plugin.xml");

        Assert.Contains("extensions=\"vbp\"", riderManifest, StringComparison.Ordinal);

        var vsCode = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Root(), "vscode-vbrazor", "package.json")));

        var languages = vsCode.RootElement
            .GetProperty("contributes").GetProperty("languages")
            .EnumerateArray()
            .Select(l => l.GetProperty("id").GetString())
            .ToList();

        Assert.Contains("vbhtml", languages);
        Assert.Contains("vbp", languages);
    }
}
