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

        // The mapping starts at the "@", not at the expression after it: the
        // transition is where the construct begins, and an error belongs to
        // the whole of it rather than to the part after the sign.
        Assert.Equal(template.IndexOf('@'), mapping.Original.Start);

        var back = generated.Map.ToOriginal(mapping.Generated.Start);

        Assert.Equal(mapping.Original.Start, back);
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
