using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// A rebuild that fails in the middle of an undo, an edit or a language switch. The failure comes from a type the form names
/// that is not built in: the designer looks it up in the design context, and the test sets the context's
/// ResolutionFaultForTests so that lookup throws an IOException, as an unreadable build output does. (A deleted output
/// folder no longer makes the lookup fail: library loading tolerates it.) The session must keep the step, the document, the shown
/// preview and the resource files exactly as they were before the failed operation.
/// </summary>
/// <remarks>
/// Defects reintroduced once, each on its own: (1) RestoreAsync took the step off its stack and reverted nothing when the
/// rebuild failed: the undo tests failed on the resource source and on the undo depth. (2) ApplyAsync did not revert the
/// companion edit of a failed commit: the localized add test failed on the resource source. (3) SetCulture changed the
/// language before the rebuild and kept it when the rebuild failed: the language test failed on Culture.
/// </remarks>
public sealed class WinFormsDesignerRebuildRollbackTests
{
    private static string Localized => WinFormsDesignerLocalizationDocumentTests.Localized;

    private static string Neutral => WinFormsDesignerLocalizationSessionTests.NeutralResources;

    private static string Italian => WinFormsDesignerLocalizationSessionTests.ItalianResources;

    /// <summary>
    /// A design context over a build output folder that the test may remove. The context's project assembly is not in the
    /// folder, and the libraries are read when the context is opened, so removing the folder alone does not fail a lookup.
    /// Removing it also makes the context's next type lookup throw IOException, the failing rebuild these tests need.
    /// </summary>
    private sealed class OutputFolder : IDisposable
    {
        public OutputFolder()
        {
            Folder = Directory.CreateTempSubdirectory("basalt-rollback-").FullName;
            Context = new WinFormsDesignAssemblyContext(Path.Combine(Folder, "App.dll"));
        }

        public string Folder { get; }

        public WinFormsDesignAssemblyContext Context { get; }

        public void Remove()
        {
            Directory.Delete(Folder, recursive: true);
            Context.ResolutionFaultForTests = new IOException("The build output folder was removed on purpose.");
        }

        public void Dispose()
        {
            Context.Unload();
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
    }

    /// <summary>A form with a control of a type the output folder would hold, so every rebuild of it looks in the folder.</summary>
    private static async Task<WinFormsDesignerSession> FormWithAProjectControlAsync(OutputFolder output, string resources)
    {
        var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(Localized),
            new WinFormsResourceDocument(resources), designContext: output.Context);
        await session.ApplyCollectionAsync(document => document.AddControl("Thing1", "Global.App.Thing", "Me"));
        return session;
    }

    [Fact]
    public async Task AFailedRebuildOfAnUndoPutsTheResourceFilesBackAndKeepsTheStep()
    {
        using var output = new OutputFolder();
        using var session = await FormWithAProjectControlAsync(output, Neutral);
        await session.AddControlAsync(typeof(Forms.Button), "Me");
        var added = session.Document.Source;
        var addedResources = session.ResourceSource;
        Assert.Equal(2, session.UndoDepth);
        output.Remove();

        await Assert.ThrowsAnyAsync<IOException>(() => session.UndoAsync());

        Assert.Equal(added, session.Document.Source);
        Assert.Equal(addedResources, session.ResourceSource);
        Assert.NotNull(session.FindControl("Button2"));
        Assert.Equal(2, session.UndoDepth);
    }

    [Fact]
    public async Task AFailedRebuildOfAnUndoOfAPlainEditKeepsTheStep()
    {
        using var output = new OutputFolder();
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source),
            designContext: output.Context);
        await session.ApplyCollectionAsync(document => document.AddControl("Thing1", "Global.App.Thing", "Me"));
        await session.AddControlAsync(typeof(Forms.Button), "Me");
        var added = session.Document.Source;
        output.Remove();

        await Assert.ThrowsAnyAsync<IOException>(() => session.UndoAsync());

        Assert.Equal(added, session.Document.Source);
        Assert.NotNull(session.FindControl("Button2"));
        Assert.Equal(2, session.UndoDepth);
    }

    [Fact]
    public async Task AFailedRebuildOfALocalizedAddPutsTheResourceFilesBackAndAddsNoStep()
    {
        using var output = new OutputFolder();
        using var session = await FormWithAProjectControlAsync(output, Neutral);
        var before = session.Document.Source;
        var beforeResources = session.ResourceSource;
        output.Remove();

        await Assert.ThrowsAnyAsync<IOException>(() => session.AddControlAsync(typeof(Forms.Button), "Me"));

        Assert.Equal(before, session.Document.Source);
        Assert.Equal(beforeResources, session.ResourceSource);
        Assert.Null(session.FindControl("Button2"));
        Assert.Equal(1, session.UndoDepth);
    }

    [Fact]
    public async Task AFailedRebuildOfALanguageSwitchKeepsThePreviousLanguage()
    {
        using var output = new OutputFolder();
        using var session = await FormWithAProjectControlAsync(output, Neutral);
        output.Remove();

        Assert.ThrowsAny<IOException>(() => session.SetCulture("it", new WinFormsResourceDocument(Italian)));

        Assert.Null(session.Culture);
        Assert.Null(session.CultureResourceSource);
        Assert.Equal(1, session.UndoDepth);
    }
}
