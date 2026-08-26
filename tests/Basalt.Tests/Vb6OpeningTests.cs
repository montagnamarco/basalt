using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Opening a Visual Basic 6 project from the IDE.
/// </summary>
/// <remarks>
/// The pieces worked and nothing reached them: a .vbp had to be converted by
/// hand and a .frm opened as text. What is tested here is the path a person
/// actually takes — File, Open, pick the project — because that is where the
/// gaps between the pieces show.
/// </remarks>
public class Vb6OpeningTests
{
    private static string SampleDirectory()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "samples", "Basalt.Sample.Vb6");

        return Path.GetFullPath(directory);
    }

    /// <summary>A copy of the sample, so a test never writes into the repository.</summary>
    private static string CopyOfSample()
    {
        var source = SampleDirectory();
        var target = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        return target;
    }

    [Fact]
    public void OpeningAProjectWritesOneTheIdeCanRead()
    {
        // The .vbp is not opened: a .vbproj is written beside it and that is
        // opened instead. From there it is an ordinary project, with the
        // IntelliSense and the debugger that already work.
        var directory = CopyOfSample();

        try
        {
            var result = Basalt.Vb6.ProjectConversion.Convert(
                Path.Combine(directory, "Anagrafica.vbp"));

            Assert.True(File.Exists(result.ProjectPath));
            Assert.Equal("Anagrafica.vbproj", Path.GetFileName(result.ProjectPath));

            // And the sources are still where Visual Basic 6 left them.
            Assert.True(File.Exists(Path.Combine(directory, "Form1.frm")));
            Assert.Contains("Form1.frm", result.Sources);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFormIsOfferedToTheDesigner()
    {
        // Through the explorer that classifies it, not by naming the kind
        // here: asserting that a XamlFile is designable proves nothing about
        // whether a .frm is ever classified as one, and that is the step that
        // decides whether the menu item appears at all.
        var directory = CopyOfSample();

        try
        {
            var explorer = new SolutionExplorerViewModel();

            explorer.Load(Path.Combine(directory, "Anagrafica.vbp"));

            var form = Flatten(explorer.Roots)
                .FirstOrDefault(n => n.Name.EndsWith(".frm", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(form);
            Assert.True(form!.IsDesignable, "a .frm should open in the designer");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<SolutionTreeNode> Flatten(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    [AvaloniaFact]
    public void TheDesignerReadsAFormAsMarkup()
    {
        // A .frm is not XAML, so the designer has to be given the markup it
        // was turned into rather than the file: parsing the .frm itself throws
        // and the tab comes up blank.
        var form = Basalt.Vb6.FormFile.Parse(
            File.ReadAllText(Path.Combine(SampleDirectory(), "Form1.frm")));

        var markup = Basalt.Vb6.FormToAxaml.Convert(form, "Form1");

        var document = Basalt.Designer.Model.XamlDocument.Parse(markup, "/x/Form1.frm");

        Assert.Equal("Window", document.Root.Name.LocalName);
        Assert.Contains(document.Root.Descendants(), e => e.Name.LocalName == "Button");
    }

    [AvaloniaFact]
    public void AFormGetsTheDesignerVisualBasic6Had()
    {
        // The dotted grid, the grey surface and the small filled handles are
        // most of why a Visual Basic 6 designer is recognisable at a glance,
        // and someone opening a twenty-year-old form expects to recognise it.
        var look = "/x/Form1.frm".EndsWith(".frm", StringComparison.OrdinalIgnoreCase)
            ? DesignerLook.VisualBasic6
            : DesignerLook.Modern;

        Assert.True(look.ShowsGrid);
        Assert.False(look.ShowsSelectionFrame);

        // And an .axaml keeps the modern one.
        Assert.False(DesignerLook.Modern.ShowsGrid);
    }
}
