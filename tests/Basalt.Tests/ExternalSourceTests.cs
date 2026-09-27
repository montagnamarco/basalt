using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Errors that point at the template rather than at generated code.
///
/// Without this, a mistake in a .vbhtml is reported against a Visual Basic
/// file the user never wrote and cannot open — the failure the whole mapping
/// bridge exists to prevent.
/// </summary>
public sealed class ExternalSourceTests
{
    private static VbHtmlCodeWriter.Generated Generate(string template, string? path = "Views/Page.vbhtml") =>
        VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Page", "App.Views", path);

    [Theory]
    [InlineData("<p>@i</p>\n", "i")]
    [InlineData("<p>@e</p>\n", "e")]
    [InlineData("<p>@Html.Raw(a)</p>\n", "a")]
    [InlineData("<p>@(  t  )</p>\n", "t")]
    public void AOneLetterExpressionMapsToItselfNotToTheCallAroundIt(string template, string name)
    {
        // The offset was found by searching the generated line, and "i" is
        // also in "Write": hover on @i described Write.
        foreach (var code in new[]
        {
            VbHtmlCodeWriter.WriteWithMap(VbHtmlParser.Parse(template), "Page", "App.Views", "Views/Page.vbhtml"),
            new VbHtmlCodeWriter.Generated(
                VbComponentWriter.WriteWithMap(VbHtmlParser.Parse(template), "C", "App", "C.vbrazor").Code,
                VbComponentWriter.WriteWithMap(VbHtmlParser.Parse(template), "C", "App", "C.vbrazor").Map),
        })
        {
            // The name itself, not the same letter in "Html.Raw".
            var caret = template.LastIndexOf(name, StringComparison.Ordinal);
            var at = code.Map.ToGenerated(caret);

            Assert.NotNull(at);
            Assert.Equal(name[0], code.Code[at.Value]);
            Assert.False(char.IsLetterOrDigit(code.Code[at.Value + 1]), code.Code[(at.Value - 8)..(at.Value + 8)]);
        }
    }

    [Theory]
    [InlineData("<p>@Await  Foo()</p>\n", "Foo")]
    [InlineData("<button onclick=\"@AddressOf  Go\">x</button>\n", "Go")]
    [InlineData("<input disabled=\"@Model.Locked\" />\n", "Locked")]
    public void ExtraSpacesAndConditionalAttributesStillMapCharacterForCharacter(string template, string name)
    {
        // Two spaces after Await or AddressOf made the template side longer
        // than the generated one; a conditional attribute was anchored at its
        // "@" and at the start of the line.
        var generated = Generate(template);
        var caret = template.IndexOf(name, StringComparison.Ordinal);
        var at = generated.Map.ToGenerated(caret);

        Assert.NotNull(at);
        Assert.StartsWith(name, generated.Code[at.Value..], StringComparison.Ordinal);
    }

    [Fact]
    public void AnAwaitedConditionalAttributeIsAwaited()
    {
        // It wrote the Task itself.
        var generated = Generate("<input disabled=\"@Await IsLockedAsync()\" />\n");

        Assert.Contains("WriteAttribute(\"disabled\", Await IsLockedAsync())", generated.Code);
    }

    [Fact]
    public void AnErrorOnAwaitStillLandsOnTheTemplate()
    {
        // The mapping began after "Await ", so an error on the keyword itself
        // (Await in a Sub that is not Async) mapped nowhere and was dropped.
        const string template = "<p>@Await Html.PartialAsync(\"_X\")</p>\n";

        var generated = Generate(template);
        var awaitInCode = generated.Code.IndexOf("Await Html", StringComparison.Ordinal);

        Assert.Equal(template.IndexOf("Await", StringComparison.Ordinal), generated.Map.ToOriginal(awaitInCode));
    }

    [Fact]
    public void WrapsAnExpressionInAPragmaNamingTheTemplate()
    {
        var generated = Generate("<p>@Model.Name</p>");

        Assert.Contains("#ExternalSource(\"Views/Page.vbhtml\", 1)",
            generated.Code, StringComparison.Ordinal);
        Assert.Contains("#End ExternalSource", generated.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheLineTheExpressionWasWrittenOn()
    {
        var generated = Generate("<p>one</p>\n<p>two</p>\n<p>@Model.Name</p>");

        Assert.Contains("#ExternalSource(\"Views/Page.vbhtml\", 3)",
            generated.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOutThePragmasWhenThereIsNoFileToNameThem()
    {
        // A pragma naming no file would send the compiler nowhere.
        var generated = Generate("<p>@Model.Name</p>", path: null);

        Assert.DoesNotContain("#ExternalSource", generated.Code, StringComparison.Ordinal);
        Assert.Empty(generated.Map.Mappings);
    }

    [Fact]
    public void RecordsAMappingForEveryPragma()
    {
        // The two are written by one scope, so their counts must agree: a
        // mapping without a pragma, or the reverse, is the bug this guards.
        var generated = Generate("<p>@A</p><p>@B</p><p>@C</p>");

        var pragmas = generated.Code.Split("#ExternalSource").Length - 1;

        Assert.Equal(pragmas, generated.Map.Mappings.Count);
        Assert.Equal(3, pragmas);
    }

    [Fact]
    public void MapsAGeneratedPositionBackToTheTemplate()
    {
        const string template = "<p>@Model.Name</p>";

        var generated = Generate(template);

        var mapping = Assert.Single(generated.Map.Mappings);

        // The mapping starts at the expression's own text on both sides. It
        // used to start at the "@" on one side and at the start of the
        // generated line on the other, so every position inside drifted: a
        // caret on "Name" was asked about "Model", and go to definition
        // opened the wrong symbol.
        Assert.Equal(template.IndexOf("Model", StringComparison.Ordinal), mapping.Original.Start);
        Assert.StartsWith("Model.Name", generated.Code[mapping.Generated.Start..], StringComparison.Ordinal);

        var back = generated.Map.ToOriginal(mapping.Generated.Start);

        Assert.Equal(mapping.Original.Start, back);

        var caret = template.IndexOf("Name", StringComparison.Ordinal);

        Assert.StartsWith("Name", generated.Code[generated.Map.ToGenerated(caret)!.Value..], StringComparison.Ordinal);
    }

    [Fact]
    public void MapsStatementsInACodeBlockToo()
    {
        var generated = Generate("""
            @Code
                Dim x = 1
            End Code
            <p>@x</p>
            """);

        Assert.NotEmpty(generated.Map.Mappings);
        Assert.Contains("#ExternalSource", generated.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesTheClassHeaderOutsideAnyPragma()
    {
        // The header belongs to nobody: an error there must not be blamed on
        // a template line.
        var generated = Generate("<p>@Model.Name</p>");

        var header = generated.Code[..generated.Code.IndexOf("#ExternalSource", StringComparison.Ordinal)];

        Assert.Contains("Partial Public Class Page", header, StringComparison.Ordinal);
        Assert.Contains("Inherits", header, StringComparison.Ordinal);
    }

    [Fact]
    public void StillProducesTheSameCodeThroughTheOlderEntryPoint()
    {
        // Write() is what most callers use, and it must keep working.
        var document = VbHtmlParser.Parse("<p>@Model.Name</p>");

        var plain = VbHtmlCodeWriter.Write(document, "Page", "App.Views");

        Assert.Contains("Write(Model.Name)", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("#ExternalSource", plain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheVbCompilerReportsErrorsOnTheTemplateLine()
    {
        // The end of the whole exercise: a mistake in a .vbhtml has to be
        // reported against the .vbhtml, not against generated code the user
        // never wrote and cannot open.
        var root = Path.Combine(Path.GetTempPath(), $"basalt-extsrc-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(root, "Views"));

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.vbproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Library</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace></RootNamespace>
                  </PropertyGroup>
                </Project>
                """);

            // Line 3 names something that does not exist.
            const string template = "<p>one</p>\n<p>two</p>\n<p>@NoSuchThing.AtAll</p>";

            await File.WriteAllTextAsync(Path.Combine(root, "Views", "Page.vbhtml"), template);

            var generated = VbHtmlCodeWriter.WriteWithMap(
                VbHtmlParser.Parse(template), "Page", "App.Views", "Views/Page.vbhtml");

            // Without the runtime base class the view cannot inherit it, so
            // the generated code is trimmed to what the test is about.
            var code = generated.Code
                .Replace($"        Inherits {VbHtmlCodeWriter.BaseTypeName}", "")
                .Replace("Public Overrides Sub Execute()", "Public Sub Execute()")
                .Replace("WriteLiteral(", "Ignore(")
                .Replace("Write(", "Ignore(")
                .Replace("WriteRaw(", "Ignore(")
                .Replace("    End Class",
                    "        Private Sub Ignore(value As Object)\n        End Sub\n    End Class");

            await File.WriteAllTextAsync(Path.Combine(root, "Generated.vb"), code);

            var build = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                ArgumentList = { "build", root, "-v", "q", "--nologo" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;

            var output = await build.StandardOutput.ReadToEndAsync();

            await build.WaitForExitAsync();

            // The error names the template and its line, not Generated.vb.
            Assert.Contains("Page.vbhtml(3", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Generated.vb(", output, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task TheVbCompilerReportsAnErrorInACodeBlockOnTheRightLine()
    {
        // The pragma used to name the line "@Code" is on, not the line the
        // statement is on, so every error inside a block was reported one
        // line high — and it pointed at the keyword, which is never wrong.
        var root = Path.Combine(Path.GetTempPath(), $"basalt-extsrc-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(root, "Views"));

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Probe.vbproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Library</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RootNamespace></RootNamespace>
                  </PropertyGroup>
                </Project>
                """);

            // "@Code" is line 1; the mistake is on line 3.
            const string template =
                "@Code\n    Dim ok = 1\n    Dim bad = NoSuchThing\nEnd Code\n<p>@ok</p>";

            await File.WriteAllTextAsync(Path.Combine(root, "Views", "Page.vbhtml"), template);

            var generated = VbHtmlCodeWriter.WriteWithMap(
                VbHtmlParser.Parse(template), "Page", "App.Views", "Views/Page.vbhtml");

            var code = generated.Code
                .Replace($"        Inherits {VbHtmlCodeWriter.BaseTypeName}", "")
                .Replace("Public Overrides Sub Execute()", "Public Sub Execute()")
                .Replace("WriteLiteral(", "Ignore(")
                .Replace("Write(", "Ignore(")
                .Replace("WriteRaw(", "Ignore(")
                .Replace("    End Class",
                    "        Private Sub Ignore(value As Object)\n        End Sub\n    End Class");

            await File.WriteAllTextAsync(Path.Combine(root, "Generated.vb"), code);

            var build = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                ArgumentList = { "build", root, "-v", "q", "--nologo" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;

            var output = await build.StandardOutput.ReadToEndAsync();

            await build.WaitForExitAsync();

            Assert.Contains("Page.vbhtml(3", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Page.vbhtml(1", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Generated.vb(", output, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void KeepsBackslashesInAPathAsTheyAre()
    {
        // Razor has a known bug here because C# treats a backslash as an
        // escape. Visual Basic strings have no escape sequences at all, so a
        // Windows path passes through — checked against the real compiler,
        // which reported the error against "Views\\Windows\\Page.vbhtml".
        var generated = Generate("<p>@Model.Name</p>", path: @"Views\Windows\Page.vbhtml");

        Assert.Contains(@"#ExternalSource(""Views\Windows\Page.vbhtml""",
            generated.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void DoublesAQuoteInAFileName()
    {
        // A quote would end the string early and turn the rest of the
        // generated file into nonsense.
        var generated = Generate("<p>@Model.Name</p>", path: "Odd\"Name.vbhtml");

        Assert.Contains(@"""Odd""""Name.vbhtml""", generated.Code, StringComparison.Ordinal);
    }
}
