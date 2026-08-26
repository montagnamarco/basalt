using Basalt.Razor.Vb.Hosting;

namespace Basalt.Tests;

/// <summary>
/// Recompiling a page while the site is running.
/// </summary>
/// <remarks>
/// What ASP and PHP had and ASP.NET Core does not: save the file, reload the
/// browser. The build-time generator stays right for production — errors
/// before deployment, no compiler shipped — but in development the wait
/// between a change and seeing it is the whole cost of working this way.
/// </remarks>
public sealed class VbPageHotReloadTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-hotreload-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void APageCompiles()
    {
        Write("Index.vbpage", "<h1>hello</h1>");

        using var compiler = new VbPageCompiler(_root, watch: false);

        var result = compiler.Load("Index.vbpage");

        Assert.True(result.Succeeded, result.Error ?? "no reason given");
    }

    [Fact]
    public void AChangedPageIsCompiledAgain()
    {
        // The whole point. Compiled once, edited, and the next request must
        // see the new text rather than the cached class.
        var path = Write("Index.vbpage", "<h1>first</h1>");

        using var compiler = new VbPageCompiler(_root, watch: false);

        var first = compiler.Load("Index.vbpage");
        Assert.True(first.Succeeded, first.Error ?? "no reason given");

        // A different last-write time, which is what the cache turns on.
        File.WriteAllText(path, "<h1>second</h1>");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));

        var second = compiler.Load("Index.vbpage");

        Assert.True(second.Succeeded, second.Error ?? "no reason given");
        Assert.NotSame(first.Type, second.Type);
    }

    [Fact]
    public void AnUnchangedPageIsNotCompiledTwice()
    {
        // Compiling on every request would make each page load take the best
        // part of a second, which is worse than the wait it replaces.
        Write("Index.vbpage", "<h1>hello</h1>");

        using var compiler = new VbPageCompiler(_root, watch: false);

        Assert.Same(compiler.Load("Index.vbpage").Type, compiler.Load("Index.vbpage").Type);
    }

    [Fact]
    public void AVisualBasicErrorIsReportedAgainstThePageLine()
    {
        // Against the .vbpage line, not the generated file: an error at "line
        // 34" of code the author never saw is no help at all.
        Write("Index.vbpage", "<h1>hi</h1>\n<p><%= missingName %></p>");

        using var compiler = new VbPageCompiler(_root, watch: false);

        var result = compiler.Load("Index.vbpage");

        Assert.False(result.Succeeded);
        Assert.Contains("line 2", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void AParseErrorIsReportedToo()
    {
        Write("Index.vbpage", "<p><% Dim x = 1");

        using var compiler = new VbPageCompiler(_root, watch: false);

        var result = compiler.Load("Index.vbpage");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void APageThatIsNotThereIsNotAnError()
    {
        // A missing page is a 404, which is a different thing from a page
        // that would not compile.
        using var compiler = new VbPageCompiler(_root, watch: false);

        var result = compiler.Load("Nowhere.vbpage");

        Assert.False(result.Succeeded);
        Assert.Null(result.Error);
    }

    [Fact]
    public void APathLeavingTheFolderIsRefused()
    {
        // Without this, "../../../etc/passwd.vbpage" is compiled and served.
        using var compiler = new VbPageCompiler(_root, watch: false);

        var result = compiler.Load(Path.Combine("..", "..", "Elsewhere.vbpage"));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void VbCrLfIsAvailableToAPage()
    {
        // Every generated page uses it, and nothing in an ASP.NET Core site
        // loads Microsoft.VisualBasic on its own: the first page served
        // failed to compile for want of a name every Visual Basic file has.
        Write("Index.vbpage", "<h1>line one</h1>\n<h2>line two</h2>");

        using var compiler = new VbPageCompiler(_root, watch: false);

        var result = compiler.Load("Index.vbpage");

        Assert.True(result.Succeeded, result.Error ?? "no reason given");
    }
}
