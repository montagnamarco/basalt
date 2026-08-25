namespace Basalt.Tests;

/// <summary>
/// What a macOS bundle needs to carry the name and the icon.
///
/// Without an Info.plist a .app has neither: the menu bar shows the process
/// name — "Avalonia Application" — and the dock shows a blank sheet.
/// </summary>
public class MacBundleTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Basalt.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            return "";
        }
    }

    [Fact]
    public void ThereIsAnInfoPlist()
    {
        Assert.SkipWhen(RepositoryRoot.Length == 0, "The repository root was not found.");

        Assert.True(File.Exists(
            Path.Combine(RepositoryRoot, "src", "Basalt.Shell", "Info.plist")));
    }

    [Fact]
    public void ItNamesTheApplicationAndItsIcon()
    {
        Assert.SkipWhen(RepositoryRoot.Length == 0, "The repository root was not found.");

        var text = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "Basalt.Shell", "Info.plist"));

        Assert.Contains("<string>Basalt</string>", text, StringComparison.Ordinal);
        Assert.Contains("CFBundleIconFile", text, StringComparison.Ordinal);
        Assert.Contains("basalt.icns", text, StringComparison.Ordinal);

        // Half resolution on a retina screen without it.
        Assert.Contains("NSHighResolutionCapable", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ItSaysWhichFilesBasaltOpens()
    {
        Assert.SkipWhen(RepositoryRoot.Length == 0, "The repository root was not found.");

        var text = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "Basalt.Shell", "Info.plist"));

        foreach (var extension in new[] { "vb", "bas", "vbhtml", "slnx" })
            Assert.Contains($"<string>{extension}</string>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThereIsAScriptThatAssemblesTheBundle()
    {
        Assert.SkipWhen(RepositoryRoot.Length == 0, "The repository root was not found.");

        var script = Path.Combine(RepositoryRoot, "build", "bundle-macos.sh");

        Assert.True(File.Exists(script));

        var text = File.ReadAllText(script);

        // Signed last: anything written into a bundle afterwards breaks the
        // signature.
        var signs = text.IndexOf("codesign --force", StringComparison.Ordinal);
        var copies = text.IndexOf("cp -R", StringComparison.Ordinal);

        Assert.True(copies < signs, "the bundle is signed before it is filled");
    }
}
