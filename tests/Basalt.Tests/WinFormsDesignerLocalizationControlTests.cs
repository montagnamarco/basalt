using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Adding, pasting, deleting and renaming a control in a Localizable form, driven through the session. The
/// neutral .resx and the culture .resx files are the fixtures of <see cref="WinFormsDesignerLocalizationSessionTests"/>;
/// the culture files live in memory here, as the host's buffers hold them in the application.
/// </summary>
/// <remarks>
/// Defects reintroduced once, each run on its own, to prove these tests catch them:
/// (1) the add skipped the move (no neutral entries, no ApplyResources call): six tests failed, the add, layout, drop,
/// paste, and the undo of the add and of the paste. (2) the delete skipped the culture files: four tests failed, the
/// delete, group delete, undo of the delete, and the view test. (3) the rename skipped the culture files: the rename
/// test and the undo-of-rename test failed. (4) undo skipped the neutral
/// resx: four tests failed, the undo of the add, paste, delete and rename. (5) the view stopped binding the culture hooks
/// at session creation: the view delete test failed.
/// </remarks>
public sealed class WinFormsDesignerLocalizationControlTests
{
    private static string Localized => WinFormsDesignerLocalizationDocumentTests.Localized;

    private static string Neutral => WinFormsDesignerLocalizationSessionTests.NeutralResources;

    private static string Italian => WinFormsDesignerLocalizationSessionTests.ItalianResources;

    /// <summary>A French file that overrides only the form's Text, so it holds no Button1 entries to remove.</summary>
    private static readonly string French = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
          <data name="$this.Text" xml:space="preserve">
            <value>Formulaire principal</value>
          </data>
        </root>
        """.ReplaceLineEndings("\n");

    /// <summary>The culture .resx files a test form has on disk, held the way the host's buffers hold them.</summary>
    private sealed class CultureFiles
    {
        public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);

        /// <summary>The cultures whose file a commit wrote, in order, so a test can prove an untouched file stayed untouched.</summary>
        public List<string> Writes { get; } = [];

        public void Attach(WinFormsDesignerSession session)
        {
            session.ListCultureResources = () => Texts.Keys.ToArray();
            session.LoadCultureResources = culture => Task.FromResult<WinFormsResourceDocument?>(
                Texts.TryGetValue(culture, out var text) ? new WinFormsResourceDocument(text) : null);
            session.PrepareCultureResourceEdit = (culture, expected, replacement) =>
            {
                if (!Texts.TryGetValue(culture, out var current)) Texts[culture] = replacement;
                else if (current != expected && current != replacement)
                    throw new InvalidOperationException("The culture file changed.");
                return Task.CompletedTask;
            };
            session.VerifyCultureResourceSource = (culture, expected, replacement) =>
            {
                if (Texts[culture] != expected && Texts[culture] != replacement)
                    throw new InvalidOperationException("The culture file changed.");
            };
            session.CommitCultureResourceSource = (culture, expected, replacement) =>
            {
                if (Texts[culture] == replacement) return;
                if (Texts[culture] != expected) throw new InvalidOperationException("The culture file changed.");
                Texts[culture] = replacement;
                Writes.Add(culture);
            };
        }
    }

    private static WinFormsDesignerSession LocalizedSession(CultureFiles? cultures = null)
    {
        var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(Localized),
            new WinFormsResourceDocument(Neutral));
        cultures?.Attach(session);
        return session;
    }

    private static CultureFiles WithItalianAndFrench()
    {
        var cultures = new CultureFiles();
        cultures.Texts["it"] = Italian;
        cultures.Texts["fr"] = French;
        return cultures;
    }

    private static IEnumerable<string> EntryNames(string resources) =>
        new WinFormsResourceDocument(resources).Entries.Select(entry => entry.Name);

    [Fact]
    public async Task AddingAControlMovesItsValuesToTheNeutralResxAndCallsApplyResources()
    {
        using var session = LocalizedSession();

        await session.AddControlAsync(typeof(Forms.Button), "Me");

        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal("20, 20", resources.TryGet("Button2.Location")!.Value);
        Assert.Equal("System.Drawing.Point, System.Drawing", resources.TryGet("Button2.Location")!.TypeName);
        Assert.Equal("100, 30", resources.TryGet("Button2.Size")!.Value);
        Assert.Equal("Button2", resources.TryGet("Button2.Text")!.Value);
        // The name stays in code, as it does for every control Visual Studio localizes.
        Assert.Null(resources.TryGet("Button2.Name"));
        Assert.Contains("        resources.ApplyResources(Me.Button2, \"Button2\")\n", session.Document.Source);
        Assert.DoesNotContain("Me.Button2.Location = ", session.Document.Source);
        Assert.DoesNotContain("Me.Button2.Size = ", session.Document.Source);
        Assert.DoesNotContain("Me.Button2.Text = ", session.Document.Source);
        // The surface reads the value back from the resx, so the new control looks the same after the move.
        Assert.Equal("Button2", session.FindControl("Button2")!.Text);
        // The existing control's entries are untouched.
        Assert.Equal("40, 50", resources.TryGet("Button1.Location")!.Value);
    }

    [Fact]
    public async Task AddedControlLayoutFollowsTheVisualStudioPlacement()
    {
        using var session = LocalizedSession();

        await session.AddControlAsync(typeof(Forms.Button), "Me");

        // The ApplyResources call goes where Visual Studio puts it: in front of the control's Name assignment,
        // inside its own section, with the literal assignments gone.
        Assert.Contains(string.Join("\n",
            "        Me.Button2 = New Global.System.Windows.Forms.Button()",
            "        resources.ApplyResources(Me.Button2, \"Button2\")",
            "        Me.Button2.Name = \"Button2\""), session.Document.Source);
    }

    [Fact]
    public async Task DroppingAControlOnAFormMovesItsValuesToTheNeutralResx()
    {
        using var session = LocalizedSession();

        // The drop writes the dropped Location as a literal in the same edit as the add, so the move takes it too.
        await session.AddControlAtAsync(typeof(Forms.Button), "Me", new System.Drawing.Point(70, 80));

        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal("70, 80", resources.TryGet("Button2.Location")!.Value);
        Assert.Equal("100, 30", resources.TryGet("Button2.Size")!.Value);
        Assert.Equal("Button2", resources.TryGet("Button2.Text")!.Value);
        Assert.Contains("        resources.ApplyResources(Me.Button2, \"Button2\")\n", session.Document.Source);
        Assert.DoesNotContain("Me.Button2.Location = ", session.Document.Source);
        Assert.Equal(new System.Drawing.Point(70, 80), session.FindControl("Button2")!.Location);
    }

    [Fact]
    public async Task UndoingTheAddRestoresTheNeutralResxAndTheCode()
    {
        using var session = LocalizedSession();
        await session.AddControlAsync(typeof(Forms.Button), "Me");
        Assert.Contains("Button2", session.ResourceSource!);

        await session.UndoAsync();

        Assert.Equal(Localized, session.Document.Source);
        Assert.Equal(Neutral, session.ResourceSource);
        Assert.Null(session.FindControl("Button2"));
    }

    [Fact]
    public async Task PastingAControlMovesItsValuesToTheNeutralResxAndCallsApplyResources()
    {
        using var source = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized), null);
        var clipboard = source.CopyControls([source.FindControl("Button1")!]);
        using var session = LocalizedSession();

        var names = await session.PasteControlsAsync(clipboard, session.Form);

        Assert.Equal(["Button2"], names);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        // The pasted values are the copied control's own: its Location, Size and Text as the source form wrote them.
        Assert.Equal("40, 50", resources.TryGet("Button2.Location")!.Value);
        Assert.Equal("120, 35", resources.TryGet("Button2.Size")!.Value);
        Assert.Equal("Button", resources.TryGet("Button2.Text")!.Value);
        Assert.Contains("        resources.ApplyResources(Me.Button2, \"Button2\")\n", session.Document.Source);
        Assert.DoesNotContain("Me.Button2.Location = ", session.Document.Source);
        Assert.Equal("Button", session.FindControl("Button2")!.Text);
    }

    [Fact]
    public async Task UndoingThePasteRestoresTheNeutralResxAndTheCode()
    {
        using var source = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized), null);
        var clipboard = source.CopyControls([source.FindControl("Button1")!]);
        using var session = LocalizedSession();
        await session.PasteControlsAsync(clipboard, session.Form);
        Assert.Contains("Button2", session.ResourceSource!);

        await session.UndoAsync();

        Assert.Equal(Localized, session.Document.Source);
        Assert.Equal(Neutral, session.ResourceSource);
    }

    [Fact]
    public async Task DeletingAControlRemovesItsEntriesFromTheNeutralAndEveryCultureResx()
    {
        var cultures = WithItalianAndFrench();
        using var session = LocalizedSession(cultures);

        await session.DeleteAsync(session.FindControl("Button1")!);

        Assert.DoesNotContain("Button1", session.Document.Source);
        Assert.Equal(["$this.Text"], EntryNames(session.ResourceSource!));
        Assert.Equal(["$this.Text"], EntryNames(cultures.Texts["it"]));
        Assert.Equal(["$this.Text"], EntryNames(cultures.Texts["fr"]));
        // The French file held nothing under Button1, so the delete did not write it.
        Assert.Equal(["it"], cultures.Writes);
    }

    [Fact]
    public async Task DeletingControlsAsAGroupRemovesTheirEntriesFromEveryResxFile()
    {
        var cultures = WithItalianAndFrench();
        using var session = LocalizedSession(cultures);

        await session.DeleteControlsAsync([session.FindControl("Button1")!]);

        Assert.Equal(["$this.Text"], EntryNames(session.ResourceSource!));
        Assert.Equal(["$this.Text"], EntryNames(cultures.Texts["it"]));
    }

    [Fact]
    public async Task UndoingTheDeleteRestoresTheNeutralResxAndEveryCultureResx()
    {
        var cultures = WithItalianAndFrench();
        using var session = LocalizedSession(cultures);
        await session.DeleteAsync(session.FindControl("Button1")!);
        Assert.Equal(["$this.Text"], EntryNames(cultures.Texts["it"]));

        await session.UndoAsync();

        Assert.Equal(Localized, session.Document.Source);
        Assert.Equal(Neutral, session.ResourceSource);
        Assert.Equal(Italian, cultures.Texts["it"]);
        Assert.Equal(French, cultures.Texts["fr"]);
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
    }

    [Fact]
    public async Task RenamingAControlRenamesItsKeysInTheNeutralAndEveryCultureResxAndItsApplyResourcesCall()
    {
        var cultures = WithItalianAndFrench();
        using var session = LocalizedSession(cultures);
        var renamed = Localized.Replace("Button1", "Button9", StringComparison.Ordinal);

        await session.ApplyFieldRenameAsync(renamed, "Button9", () => { }, () => { }, () => { });

        Assert.Equal("Button9", session.FindControl("Button9")!.Name);
        Assert.Contains("        resources.ApplyResources(Me.Button9, \"Button9\")\n", session.Document.Source);
        Assert.DoesNotContain("Button1", session.Document.Source);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal(["$this.Text", "Button9.Location", "Button9.Size", "Button9.Text"], EntryNames(session.ResourceSource!));
        Assert.Equal("40, 50", resources.TryGet("Button9.Location")!.Value);
        Assert.Equal("Pulsante", new WinFormsResourceDocument(cultures.Texts["it"]).TryGet("Button9.Text")!.Value);
        Assert.DoesNotContain("Button1", cultures.Texts["it"]);
        Assert.Equal(["it"], cultures.Writes);
    }

    [Fact]
    public async Task UndoingTheRenameRestoresEveryResxFileAndTheOldName()
    {
        var cultures = WithItalianAndFrench();
        using var session = LocalizedSession(cultures);
        var commits = 0;
        var undos = 0;
        await session.ApplyFieldRenameAsync(Localized.Replace("Button1", "Button9", StringComparison.Ordinal), "Button9",
            () => commits++, () => undos++, () => commits++);
        Assert.Equal(["$this.Text", "Button9.Location", "Button9.Size", "Button9.Text"], EntryNames(session.ResourceSource!));
        Assert.Equal(["$this.Text", "Button9.Text"], EntryNames(cultures.Texts["it"]));

        await session.UndoAsync();

        Assert.Equal(1, commits);
        Assert.Equal(1, undos);
        Assert.Equal(Localized, session.Document.Source);
        Assert.Equal(Neutral, session.ResourceSource);
        Assert.Equal(Italian, cultures.Texts["it"]);
        Assert.NotNull(session.FindControl("Button1"));
    }

    [Fact]
    public async Task AFormThatIsNotLocalizableGetsNoResourceChangeOnAddOrDelete()
    {
        var neutral = new WinFormsResourceDocument(WinFormsDesignerLocalizationSessionTests.EmptyResources);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized), neutral);

        await session.AddControlAsync(typeof(Forms.Button), "Me");
        await session.DeleteAsync(session.FindControl("Button1")!);

        Assert.Equal(WinFormsDesignerLocalizationSessionTests.EmptyResources, session.ResourceSource);
        Assert.DoesNotContain("ApplyResources", session.Document.Source);
    }

    /// <summary>
    /// The rename as the editor's Roslyn rename leaves it: the field and its references change, while the resource keys
    /// (the ApplyResources string and the "Button1." prefixes) keep the old name for the session to rewrite.
    /// </summary>
    private static string FieldRenamed => Localized
        .Replace("Me.Button1", "Me.Button9", StringComparison.Ordinal)
        .Replace("Friend WithEvents Button1", "Friend WithEvents Button9", StringComparison.Ordinal);

    /// <remarks>
    /// Defect reintroduced once (the rename skipped RenameResourceKeys, so the editor's text kept the old key): the test failed
    /// on the ApplyResources line, which still read "Button1".
    /// </remarks>
    [Fact]
    public async Task ARenameWritesTheApplyResourcesKeyThatTheKeyRewriteGives()
    {
        var cultures = WithItalianAndFrench();
        using var session = LocalizedSession(cultures);

        await session.ApplyFieldRenameAsync(FieldRenamed, "Button9", () => { }, () => { }, () => { });

        Assert.Contains("        resources.ApplyResources(Me.Button9, \"Button9\")\n", session.Document.Source);
        Assert.Contains("Me.Button9.Name = \"Button9\"", session.Document.Source);
        Assert.DoesNotContain("ApplyResources(Me.Button1", session.Document.Source);
        Assert.Equal(["$this.Text", "Button9.Location", "Button9.Size", "Button9.Text"], EntryNames(session.ResourceSource!));
        Assert.Equal("Pulsante", new WinFormsResourceDocument(cultures.Texts["it"]).TryGet("Button9.Text")!.Value);
    }

    /// <remarks>
    /// Defect reintroduced once (the undo wrote the neutral and culture resx before its companion edit): the companion edit
    /// threw, and the neutral resx had already been rewritten, so the test failed on the neutral source.
    /// </remarks>
    [Fact]
    public async Task AnUndoWhoseCompanionEditThrowsWritesNoResxFileAndKeepsItsStep()
    {
        var cultures = WithItalianAndFrench();
        using var session = LocalizedSession(cultures);
        var companionFails = false;
        await session.ApplyFieldRenameAsync(FieldRenamed, "Button9", () => { }, () =>
        {
            if (companionFails) throw new InvalidOperationException("The form code changed after this operation.");
        }, () => { });
        var renamedDocument = session.Document.Source;
        var renamedNeutral = session.ResourceSource;
        var renamedItalian = cultures.Texts["it"];
        var writes = cultures.Writes.Count;

        companionFails = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.UndoAsync());

        Assert.Equal(renamedDocument, session.Document.Source);
        Assert.Equal(renamedNeutral, session.ResourceSource);
        Assert.Equal(renamedItalian, cultures.Texts["it"]);
        Assert.Equal(writes, cultures.Writes.Count);
        Assert.Equal(1, session.UndoDepth);

        companionFails = false;
        await session.UndoAsync();

        Assert.Equal(Localized, session.Document.Source);
        Assert.Equal(Neutral, session.ResourceSource);
        Assert.Equal(Italian, cultures.Texts["it"]);
    }
}
