using Basalt.Shell.Controls;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The Localizable switch and the design-time Language, driven through the session the way the
/// property panel drives it. The Designer.vb and neutral .resx fixtures are hand-written in the layout
/// Visual Studio writes; the document-level layout is covered by WinFormsDesignerLocalizationDocumentTests.
/// </summary>
/// <remarks>
/// Defect reintroduced once to prove these tests catch it: the culture write was changed to store every
/// value (no comparison with the neutral one), and the culture preview was changed to ignore the culture
/// resource. Each change failed the tests named for that behaviour.
/// </remarks>
public sealed class WinFormsDesignerLocalizationSessionTests
{
    /// <summary>The neutral MainForm.resx Visual Studio writes for <see cref="WinFormsDesignerLocalizationDocumentTests.Localized"/>.</summary>
    internal static readonly string NeutralResources = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
          <resheader name="reader">System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</resheader>
          <resheader name="writer">System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</resheader>
          <data name="$this.Text" xml:space="preserve">
            <value>Main form</value>
          </data>
          <data name="Button1.Location" type="System.Drawing.Point, System.Drawing">
            <value>40, 50</value>
          </data>
          <data name="Button1.Size" type="System.Drawing.Size, System.Drawing">
            <value>120, 35</value>
          </data>
          <data name="Button1.Text" xml:space="preserve">
            <value>Button</value>
          </data>
        </root>
        """.ReplaceLineEndings("\n");

    /// <summary>The Italian MainForm.it.resx: it holds only the values that differ from the neutral ones.</summary>
    internal static readonly string ItalianResources = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
          <data name="$this.Text" xml:space="preserve">
            <value>Form principale</value>
          </data>
          <data name="Button1.Text" xml:space="preserve">
            <value>Pulsante</value>
          </data>
        </root>
        """.ReplaceLineEndings("\n");

    internal static readonly string EmptyResources = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype">text/microsoft-resx</resheader>
          <resheader name="version">2.0</resheader>
        </root>
        """.ReplaceLineEndings("\n");

    [Fact]
    public async Task LocalizableTrueMovesTheCodeValuesIntoTheNeutralResx()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(EmptyResources));

        await session.SetLocalizableAsync(true);

        Assert.True(session.IsLocalizable);
        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
        var resources = new WinFormsResourceDocument(session.ResourceSource!);
        Assert.Equal("40, 50", resources.TryGet("Button1.Location")!.Value);
        Assert.Equal("System.Drawing.Point, System.Drawing", resources.TryGet("Button1.Location")!.TypeName);
        Assert.Equal("Button", resources.TryGet("Button1.Text")!.Value);
        Assert.Equal("Main form", resources.TryGet("$this.Text")!.Value);
        // The surface shows what the resx now holds, so the form looks the same after the switch.
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
    }

    [Fact]
    public async Task LocalizableFalseWritesTheNeutralValuesBackAndRemovesTheirEntries()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));

        await session.SetLocalizableAsync(false);

        Assert.False(session.IsLocalizable);
        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.DisabledFromLocalized, session.Document.Source);
        Assert.Empty(new WinFormsResourceDocument(session.ResourceSource!).Entries);
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
        Assert.Equal("Main form", session.Form.Text);
    }

    [Fact]
    public async Task LocalizableSwitchUndoesAndRedoesAsOneStepEach()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(EmptyResources));

        await session.SetLocalizableAsync(true);
        await session.UndoAsync();

        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.NotLocalized, session.Document.Source);
        Assert.Empty(new WinFormsResourceDocument(session.ResourceSource!).Entries);
        await session.RedoAsync();
        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
    }

    [Fact]
    public async Task LocalizableTrueCreatesTheNeutralResxWhenTheFormHasNone()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized));
        session.CreateResourceDocument = () => Task.FromResult(WinFormsResourceDocumentTemplate.Empty);

        await session.SetLocalizableAsync(true);

        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
        Assert.Equal("Main form", new WinFormsResourceDocument(session.ResourceSource!).TryGet("$this.Text")!.Value);
    }

    [Fact]
    public void ReadingALocalizedFormShowsTheResxValuesAndKeepsTheFileUnchanged()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));

        Assert.True(session.IsLocalizable);
        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
        Assert.Equal("Main form", session.Form.Text);
    }

    [Fact]
    public void LanguageShowsTheCultureValueAndTheNeutralValueForEverythingElse()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));

        session.SetCulture("it", new WinFormsResourceDocument(ItalianResources));

        Assert.Equal("it", session.Culture);
        Assert.Equal("Pulsante", session.FindControl("Button1")!.Text);
        Assert.Equal("Form principale", session.Form.Text);

        session.SetCulture(null, null);

        Assert.Null(session.Culture);
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
        Assert.Equal("Main form", session.Form.Text);
    }

    [Fact]
    public async Task EditsInALanguageGoToTheCultureResxAndAMatchingValueRemovesTheOverride()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));
        session.SetCulture("it", new WinFormsResourceDocument(EmptyResources));

        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Pulsante");

        // The code and the neutral resx are untouched; only the culture file grows.
        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
        Assert.Equal(NeutralResources, session.ResourceSource);
        Assert.Equal("Pulsante", new WinFormsResourceDocument(session.CultureResourceSource!).TryGet("Button1.Text")!.Value);
        Assert.Equal("Pulsante", session.FindControl("Button1")!.Text);

        // Typing the neutral value again removes the override instead of storing a copy of it.
        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Button");

        Assert.Null(new WinFormsResourceDocument(session.CultureResourceSource!).TryGet("Button1.Text"));
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
    }

    [Fact]
    public async Task ResetInALanguageRemovesOnlyTheCultureOverride()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));
        session.SetCulture("it", new WinFormsResourceDocument(ItalianResources));
        Assert.True(session.CanResetResourceProperty(session.FindControl("Button1")!, "Text"));

        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", null);

        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
        Assert.Equal(NeutralResources, session.ResourceSource);
        Assert.Null(new WinFormsResourceDocument(session.CultureResourceSource!).TryGet("Button1.Text"));
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
        Assert.False(session.CanResetResourceProperty(session.FindControl("Button1")!, "Text"));
    }

    [Fact]
    public async Task EditsInALanguageAreUndoneInTheCultureResxOnly()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));
        session.SetCulture("it", new WinFormsResourceDocument(EmptyResources));

        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Pulsante");
        await session.UndoAsync();

        Assert.Equal(NeutralResources, session.ResourceSource);
        Assert.Null(new WinFormsResourceDocument(session.CultureResourceSource!).TryGet("Button1.Text"));
        Assert.Equal("Button", session.FindControl("Button1")!.Text);
    }

    [Fact]
    public async Task LocalizableCannotChangeWhileALanguageIsShown()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));
        session.SetCulture("it", new WinFormsResourceDocument(ItalianResources));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetLocalizableAsync(false));
    }

    [Fact]
    public async Task ValuesThatTheResxCannotHoldStayReferencedThroughGetObject()
    {
        // A font is not a literal the codec can write, so it is restored as a GetObject cast and its
        // entry stays in the resx, rather than being dropped with the ApplyResources call.
        var resources = NeutralResources.Replace("<data name=\"Button1.Location\"",
            "<data name=\"$this.Font\" type=\"System.Drawing.Font, System.Drawing\"><value>Microsoft Sans Serif, 8.25pt</value></data>\n  "
            + "<data name=\"Button1.Location\"", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(resources));

        await session.SetLocalizableAsync(false);

        Assert.Contains("Me.Font = CType(resources.GetObject(\"$this.Font\"), System.Drawing.Font)",
            session.Document.Source, StringComparison.Ordinal);
        Assert.NotNull(new WinFormsResourceDocument(session.ResourceSource!).TryGet("$this.Font"));
        Assert.Contains("Dim resources As System.ComponentModel.ComponentResourceManager",
            session.Document.Source, StringComparison.Ordinal);
    }

    private const string FlagName = "$this.Localizable";
    private const string FlagType = "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    /// <remarks>
    /// Defect reintroduced once (Localizable True did not write the flag): the test failed at its NotNull check on the flag. The element and type are the owner's knowledge of Visual Studio output, not checked against a Visual Studio file here.
    /// </remarks>
    [Fact]
    public async Task LocalizableWritesTheFlagTheNeutralResxKeepsAndRemovesItWhenItIsOff()
    {
        // The flag is the owner's knowledge of Visual Studio output (see WinFormsDesignerSession.Localization.cs),
        // not a file Visual Studio wrote for this change.
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(EmptyResources));

        await session.SetLocalizableAsync(true);

        var flag = new WinFormsResourceDocument(session.ResourceSource!).TryGetMetadata(FlagName);
        Assert.NotNull(flag);
        Assert.Equal("True", flag.Value);
        Assert.Equal(FlagType, flag.TypeName);
        Assert.Null(new WinFormsResourceDocument(session.ResourceSource!).TryGet(FlagName));

        await session.SetLocalizableAsync(false);

        Assert.Null(new WinFormsResourceDocument(session.ResourceSource!).TryGetMetadata(FlagName));
    }

    /// <remarks>
    /// Defect reintroduced once (IsLocalizable read only the Designer.vb): the test failed with Expected True, Actual False.
    /// </remarks>
    [Fact]
    public void TheNeutralFlagDecidesWhetherTheFormIsLocalizableWithoutApplyResources()
    {
        var flagged = new WinFormsResourceDocument(EmptyResources).SetMetadata(FlagName, "True", FlagType);
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(flagged));

        Assert.True(session.IsLocalizable);
    }

    /// <remarks>
    /// Defect reintroduced once (restore skipped properties Basalt.Forms does not mark [Localizable]): the test failed with Sub-string not found for Me.ClientSize. The $this.ClientSize entry is the owner's knowledge of a Visual Studio resx, not checked against a Visual Studio file here.
    /// </remarks>
    [Fact]
    public async Task LocalizableFalseMovesAValueBasaltFormsDoesNotMarkLocalizableBackToCode()
    {
        // Visual Studio keeps $this.ClientSize in the resx. Basalt.Forms does not mark ClientSize [Localizable],
        // and the value must still come back to code rather than be dropped with its ApplyResources call.
        var code = WinFormsDesignerLocalizationDocumentTests.Localized.Replace(
            "        Me.ClientSize = New System.Drawing.Size(300, 200)\n", "", StringComparison.Ordinal);
        var resources = NeutralResources.Replace("  <data name=\"$this.Text\"",
            "  <data name=\"$this.ClientSize\" type=\"System.Drawing.Size, System.Drawing\">\n    <value>300, 200</value>\n  </data>\n"
            + "  <data name=\"$this.Text\"", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(code), new WinFormsResourceDocument(resources));

        await session.SetLocalizableAsync(false);

        Assert.Contains("Me.ClientSize = New System.Drawing.Size(300, 200)", session.Document.Source, StringComparison.Ordinal);
        Assert.Null(new WinFormsResourceDocument(session.ResourceSource!).TryGet("$this.ClientSize"));
        Assert.Equal("Main form", session.Form.Text);
    }

    /// <remarks>
    /// Defect reintroduced once (restore used only a live control's property type): the test failed with Sub-string not found for Me.Gauge1.Caption = "Speed".
    /// </remarks>
    [Fact]
    public async Task LocalizableFalseRestoresAControlTheDesignerCannotCreateFromTheEntryType()
    {
        // Gauge1 is a project control the designer cannot instantiate. Its Caption is restored from the
        // entry's own type (a string), not from a live instance.
        var gaugeCode = """
            Partial Class MainForm
                Inherits System.Windows.Forms.Form
                Private Sub InitializeComponent()
                    Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                    Me.Gauge1 = New Global.Acme.Controls.Gauge()
                    Me.SuspendLayout()
                    '
                    'Gauge1
                    '
                    resources.ApplyResources(Me.Gauge1, "Gauge1")
                    Me.Gauge1.Name = "Gauge1"
                    '
                    'MainForm
                    '
                    resources.ApplyResources(Me, "$this")
                    Me.Controls.Add(Me.Gauge1)
                    Me.Name = "MainForm"
                    Me.ResumeLayout(False)
                End Sub
                Friend WithEvents Gauge1 As Global.Acme.Controls.Gauge
            End Class
            """.ReplaceLineEndings("\n");
        var gaugeResources = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <resheader name="resmimetype">text/microsoft-resx</resheader>
              <resheader name="version">2.0</resheader>
              <data name="Gauge1.Caption" xml:space="preserve">
                <value>Speed</value>
              </data>
            </root>
            """.ReplaceLineEndings("\n");
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(gaugeCode), new WinFormsResourceDocument(gaugeResources));

        await session.SetLocalizableAsync(false);

        Assert.Contains("Me.Gauge1.Caption = \"Speed\"", session.Document.Source, StringComparison.Ordinal);
        Assert.Null(new WinFormsResourceDocument(session.ResourceSource!).TryGet("Gauge1.Caption"));
    }

    /// <remarks>
    /// Defect reintroduced once (a picture entry was decoded and serialized as a value): the test failed with NotSupportedException from the value serializer. The one-pixel PNG decodes, so the failure is the code path and not undecodable bytes.
    /// </remarks>
    [Fact]
    public async Task LocalizableFalseKeepsAPictureInTheResxAndReadsItBackThroughGetObject()
    {
        // A valid one-pixel PNG, so the picture decodes: the code path must not depend on the bytes failing to decode.
        const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        var resources = NeutralResources.Replace("  <data name=\"Button1.Location\"",
            "  <data name=\"Button1.Image\" type=\"System.Drawing.Image, System.Drawing\" mimetype=\"application/x-microsoft.net.object.bytearray.base64\">\n"
            + "    <value>" + Pixel + "</value>\n  </data>\n  <data name=\"Button1.Location\"", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(resources));

        await session.SetLocalizableAsync(false);

        Assert.Contains("Me.Button1.Image = CType(resources.GetObject(\"Button1.Image\"), System.Drawing.Image)",
            session.Document.Source, StringComparison.Ordinal);
        Assert.NotNull(new WinFormsResourceDocument(session.ResourceSource!).TryGet("Button1.Image"));
        Assert.Contains("Dim resources As System.ComponentModel.ComponentResourceManager",
            session.Document.Source, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Defect reintroduced once (no refusal for a missing neutral resx): the test failed with NullReferenceException instead of InvalidOperationException.
    /// </remarks>
    [Fact]
    public async Task LocalizableFalseRefusesBeforeWritingWhenTheNeutralResxIsUnreadable()
    {
        // No readable resx: the ApplyResources calls would lose every value they read, so the switch is refused.
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized));
        var before = session.Document.Source;

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetLocalizableAsync(false));

        Assert.Equal(before, session.Document.Source);
    }

    /// <remarks>
    /// Defect reintroduced once (the check that refuses a Designer.vb change inside a culture removed): the test failed with No exception was thrown for the Anchor edit.
    /// </remarks>
    [Fact]
    public async Task ALanguageRefusesEditsThatWouldChangeDesignerVbAndStillWritesTheCultureValues()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));
        session.SetCulture("it", new WinFormsResourceDocument(ItalianResources));
        var button = session.FindControl("Button1")!;
        var cultureBefore = session.CultureResourceSource;

        // Anchor is stored in Designer.vb, so it cannot differ between languages: the edit is refused whole.
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetPropertyAsync(button, "Anchor", "Bottom, Right"));
        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
        Assert.Equal(cultureBefore, session.CultureResourceSource);

        // A localizable value in the same language still reaches the culture file, and only that file.
        await session.SetPropertyAsync(button, "Text", "Pulsante OK");
        Assert.Equal(WinFormsDesignerLocalizationDocumentTests.Localized, session.Document.Source);
        Assert.Equal("Pulsante OK", new WinFormsResourceDocument(session.CultureResourceSource!).TryGet("Button1.Text")!.Value);
        Assert.Equal(NeutralResources, session.ResourceSource);
    }

    /// <remarks>
    /// Defect reintroduced once (the undo of Localizable True did not show (Default)): the test failed with Culture still "it".
    /// </remarks>
    [Fact]
    public async Task UndoingLocalizableTrueShowsTheDefaultLanguageSoTheUserCanGetBack()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.NotLocalized),
            new WinFormsResourceDocument(EmptyResources));
        await session.SetLocalizableAsync(true);
        session.SetCulture("it", new WinFormsResourceDocument(ItalianResources));

        await session.UndoAsync();

        // The Language row is frozen while Localizable is off, so the undo itself must leave the culture.
        Assert.Null(session.Culture);
        Assert.False(session.IsLocalizable);
        Assert.Equal("Button", session.FindControl("Button1")!.Text);

        await session.RedoAsync();
        Assert.True(session.IsLocalizable);
        Assert.Null(session.Culture);
    }

    /// <remarks>
    /// Defect reintroduced once (the undo checked and stored the culture on screen instead of the one the edit was made in): the undo threw 'The form code changed' and the test failed.
    /// Second reintroduction (the undo applied the edit without showing the language it was made in): the Culture
    /// assertion failed with Expected "it".
    /// </remarks>
    [Fact]
    public async Task AnUndoAfterASwitchShowsTheCultureItWasMadeInAndWritesItsFile()
    {
        var prepared = new List<string>();
        var committed = new List<(string Culture, string Text)>();
        var italianFile = ItalianResources;
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));
        session.PrepareCultureResourceEdit = (culture, _, _) =>
        {
            prepared.Add(culture);
            return Task.CompletedTask;
        };
        session.CommitCultureResourceSource = (culture, _, replacement) =>
        {
            committed.Add((culture, replacement));
            italianFile = replacement;
        };
        session.LoadCultureResources = culture => Task.FromResult<WinFormsResourceDocument?>(
            culture == "it" ? new WinFormsResourceDocument(italianFile) : null);
        session.SetCulture("it", new WinFormsResourceDocument(ItalianResources));
        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Pulsante OK");

        // (Default) is shown when the undo runs. The undo must show "it" again, the language the edit was made in,
        // and revert the edit in the Italian file.
        session.SetCulture(null, null);
        await session.UndoAsync();

        Assert.Equal(["it", "it"], prepared);
        Assert.Equal(["it", "it"], committed.Select(write => write.Culture));
        Assert.Equal(ItalianResources, committed[1].Text);
        Assert.Equal("it", session.Culture);
        Assert.Equal("Pulsante", session.FindControl("Button1")!.Text);
    }

    /// <remarks>
    /// Defect reintroduced once (the guard that keeps the shown copy out of a commit made for another language removed):
    /// the (Default) session took the edited Italian text, and the null assertion failed with a non-null CultureResourceSource.
    /// </remarks>
    [Fact]
    public async Task ACommitAfterALanguageSwitchWritesTheCultureFileAndLeavesTheShownCopyAlone()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(WinFormsDesignerLocalizationDocumentTests.Localized),
            new WinFormsResourceDocument(NeutralResources));
        var committed = new List<(string Culture, string Text)>();
        session.PrepareCultureResourceEdit = (_, _, _) => Task.CompletedTask;
        session.CommitCultureResourceSource = (culture, _, replacement) => committed.Add((culture, replacement));
        session.SetCulture("it", new WinFormsResourceDocument(ItalianResources));
        // The language moves to (Default) after the edit has been parsed, while its commit is still pending.
        session.SourceChanging += () => session.SetCulture(null, null);

        await session.SetPropertyAsync(session.FindControl("Button1")!, "Text", "Pulsante OK");

        var write = Assert.Single(committed);
        Assert.Equal("it", write.Culture);
        Assert.Contains("Pulsante OK", write.Text, StringComparison.Ordinal);
        Assert.Null(session.Culture);
        Assert.Null(session.CultureResourceSource);
    }

    /// <remarks>
    /// Defect reintroduced once (a multi-line Text not evaluated): NullReferenceException on the resx value. Second reintroduction (the writer normalized the carriage return, so the value read back as LF): Strings differ at position 5.
    /// </remarks>
    [Fact]
    public async Task LocalizableTrueMovesAMultiLineTextAsOneResxValue()
    {
        // Visual Studio writes a multi-line Text as concatenated literals and ChrW codes.
        var code = WinFormsDesignerLocalizationDocumentTests.NotLocalized.Replace(
            "        Me.Button1.Text = \"Button\"\n",
            "        Me.Button1.Text = \"First\" & Global.Microsoft.VisualBasic.ChrW(13) & Global.Microsoft.VisualBasic.ChrW(10) & \"Second\"\n",
            StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(code), new WinFormsResourceDocument(EmptyResources));

        await session.SetLocalizableAsync(true);

        Assert.Equal("First\r\nSecond", new WinFormsResourceDocument(session.ResourceSource!).TryGet("Button1.Text")!.Value);
        Assert.Equal("First\r\nSecond", session.FindControl("Button1")!.Text);
        Assert.DoesNotContain("Me.Button1.Text = ", session.Document.Source, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Defect reintroduced once (an evaluation failure not caught, so it aborted the switch): the test failed with NotSupportedException from the evaluator.
    /// </remarks>
    [Fact]
    public async Task LocalizableTrueLeavesAValueFromMyResourcesInCodeInsteadOfFailing()
    {
        // Button.Text is [Localizable], so the switch tries to move it. The My.Resources expression cannot be
        // evaluated: it must stay in code, and the switch must still complete.
        var code = WinFormsDesignerLocalizationDocumentTests.NotLocalized.Replace(
            "        Me.Button1.Text = \"Button\"\n",
            "        Me.Button1.Text = Global.Sample.My.Resources.Resources.Caption\n",
            StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(code), new WinFormsResourceDocument(EmptyResources));

        await session.SetLocalizableAsync(true);

        Assert.True(session.IsLocalizable);
        Assert.Contains("Me.Button1.Text = Global.Sample.My.Resources.Resources.Caption", session.Document.Source,
            StringComparison.Ordinal);
    }
}
