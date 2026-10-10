using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// An edit that changes Designer.vb or the neutral .resx while a culture is shown is refused whole, as Visual
/// Studio refuses it in a non-default Language: Designer.vb and the neutral .resx belong to every language. Each
/// test snapshots the three texts before the refused edit and proves they are unchanged after it, then proves the
/// same edit works once (Default) is shown again.
/// </summary>
/// <remarks>
/// Defect reintroduced once (the refusal in ApplyAsync removed): the add, delete and Timer.Interval edits all wrote
/// Designer.vb in Italian and their tests failed. Second reintroduction (the image refusal that runs before the
/// neutral .resx is created removed): the image test saw the neutral .resx created during the refused edit.
/// </remarks>
public sealed class WinFormsDesignerLocalizationRefusalTests
{
    /// <summary>
    /// A localized form with a Timer, so a component edit has something to change. The layout follows Visual
    /// Studio's Localizable = True output, as the other localization fixtures do.
    /// </summary>
    private static readonly string LocalizedWithTimer = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                Me.components = New System.ComponentModel.Container()
                Me.Timer1 = New Global.System.Windows.Forms.Timer(Me.components)
                Me.Button1 = New System.Windows.Forms.Button()
                Me.SuspendLayout()
                '
                'Button1
                '
                resources.ApplyResources(Me.Button1, "Button1")
                Me.Button1.Name = "Button1"
                Me.Button1.UseVisualStyleBackColor = True
                '
                'Timer1
                '
                Me.Timer1.Interval = 10
                '
                'MainForm
                '
                Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
                Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
                resources.ApplyResources(Me, "$this")
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Controls.Add(Me.Button1)
                Me.Name = "MainForm"
                Me.ResumeLayout(False)
            End Sub
            Private components As System.ComponentModel.IContainer
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Timer1 As System.Windows.Forms.Timer
        End Class
        """.ReplaceLineEndings("\n");

    /// <summary>
    /// A localized form with a Label that has no resource entries. One bounds edit that moves both the Button
    /// (through the neutral .resx) and the Label (through Designer.vb) is then a mixed change.
    /// </summary>
    private static readonly string LocalizedWithLabel = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(MainForm))
                Me.Button1 = New System.Windows.Forms.Button()
                Me.Label1 = New System.Windows.Forms.Label()
                Me.SuspendLayout()
                '
                'Button1
                '
                resources.ApplyResources(Me.Button1, "Button1")
                Me.Button1.Name = "Button1"
                '
                'Label1
                '
                Me.Label1.Location = New System.Drawing.Point(10, 10)
                Me.Label1.Name = "Label1"
                Me.Label1.Size = New System.Drawing.Size(60, 20)
                '
                'MainForm
                '
                resources.ApplyResources(Me, "$this")
                Me.ClientSize = New System.Drawing.Size(300, 200)
                Me.Controls.Add(Me.Label1)
                Me.Controls.Add(Me.Button1)
                Me.Name = "MainForm"
                Me.ResumeLayout(False)
            End Sub
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Label1 As System.Windows.Forms.Label
        End Class
        """.ReplaceLineEndings("\n");

    /// <summary>A PNG signature and a few bytes: the image is stored as raw bytes, so it need not be a whole image.</summary>
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static string NotInLanguage => Localizer.Get(StringKeys.WinFormsDesignerNotInLanguage);

    private static WinFormsDesignerSession NewSession() => new(
        new WinFormsDesignerDocument(LocalizedWithTimer),
        new WinFormsResourceDocument(WinFormsDesignerLocalizationSessionTests.NeutralResources));

    private static (string Designer, string? Neutral, string? Culture) Texts(WinFormsDesignerSession session) =>
        (session.Document.Source, session.ResourceSource, session.CultureResourceSource);

    private static void ShowItalian(WinFormsDesignerSession session) =>
        session.SetCulture("it", new WinFormsResourceDocument(WinFormsDesignerLocalizationSessionTests.ItalianResources));

    [Fact]
    public async Task AddingAControlIsRefusedInALanguageAndWorksInDefault()
    {
        using var session = NewSession();
        ShowItalian(session);
        var before = Texts(session);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => session.AddControlAsync(typeof(Forms.Button), "Me"));

        Assert.Equal(NotInLanguage, thrown.Message);
        Assert.Equal(before, Texts(session));
        Assert.Null(session.FindControl("Button2"));

        session.SetCulture(null, null);
        await session.AddControlAsync(typeof(Forms.Button), "Me");
        Assert.NotNull(session.FindControl("Button2"));
        Assert.Contains("Button2", session.Document.Source);
    }

    [Fact]
    public async Task DeletingAControlIsRefusedInALanguageAndWorksInDefault()
    {
        using var session = NewSession();
        ShowItalian(session);
        var before = Texts(session);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => session.DeleteAsync(session.FindControl("Button1")!));

        Assert.Equal(NotInLanguage, thrown.Message);
        Assert.Equal(before, Texts(session));
        Assert.NotNull(session.FindControl("Button1"));

        session.SetCulture(null, null);
        await session.DeleteAsync(session.FindControl("Button1")!);
        Assert.Null(session.FindControl("Button1"));
        Assert.DoesNotContain("Button1", session.Document.Source);
    }

    [Fact]
    public async Task EditingAComponentIsRefusedInALanguageAndWorksInDefault()
    {
        using var session = NewSession();
        ShowItalian(session);
        var before = Texts(session);
        var timer = session.FindComponent("Timer1")!;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SetComponentPropertyAsync(timer, "Interval", "25"));

        Assert.Equal(NotInLanguage, thrown.Message);
        Assert.Equal(before, Texts(session));
        Assert.Contains("Me.Timer1.Interval = 10", session.Document.Source);

        session.SetCulture(null, null);
        await session.SetComponentPropertyAsync(session.FindComponent("Timer1")!, "Interval", "25");
        Assert.Contains("Me.Timer1.Interval = 25", session.Document.Source);
    }

    [Fact]
    public async Task SettingAnImageIsRefusedInALanguageAndWorksInDefault()
    {
        using var session = NewSession();
        ShowItalian(session);
        var before = Texts(session);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SetImageResourcePropertyAsync(session.FindControl("Button1")!, "Image", PngBytes, isIcon: false));

        Assert.Equal(NotInLanguage, thrown.Message);
        Assert.Equal(before, Texts(session));
        Assert.DoesNotContain("Button1.Image", session.Document.Source);

        session.SetCulture(null, null);
        await session.SetImageResourcePropertyAsync(session.FindControl("Button1")!, "Image", PngBytes, isIcon: false);
        Assert.Contains("Button1.Image", session.Document.Source);
        Assert.Contains("Button1.Image", session.ResourceSource!);
    }

    /// <remarks>
    /// Defect reintroduced once (the culture prepare run before the Designer.vb refusal, as ApplyPropertyChangesAsync
    /// did before): the refused mixed bounds edit opened the Italian .resx first, and the count assertion failed.
    /// </remarks>
    [Fact]
    public async Task AnEditRefusedInALanguageOpensNoCultureResx()
    {
        using var session = new WinFormsDesignerSession(
            new WinFormsDesignerDocument(LocalizedWithLabel),
            new WinFormsResourceDocument(WinFormsDesignerLocalizationSessionTests.NeutralResources));
        var prepared = 0;
        session.PrepareCultureResourceEdit = (_, _, _) =>
        {
            ++prepared;
            return Task.CompletedTask;
        };
        ShowItalian(session);
        var before = Texts(session);
        var button = session.FindControl("Button1")!;
        var label = session.FindControl("Label1")!;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetBoundsBatchAsync(
        [
            new WinFormsBoundsEdit(button, button.Bounds, new System.Drawing.Rectangle(41, 51, 120, 35)),
            new WinFormsBoundsEdit(label, label.Bounds, new System.Drawing.Rectangle(12, 12, 60, 20)),
        ]));

        Assert.Equal(NotInLanguage, thrown.Message);
        Assert.Equal(0, prepared);
        Assert.Equal(before, Texts(session));
    }

    /// <remarks>
    /// Defect reintroduced once (the early image refusal removed, so the create-resx step ran first): the
    /// CreateResourceDocument callback was called and the neutral .resx was created during the refused edit.
    /// </remarks>
    [Fact]
    public async Task SettingAnImageInALanguageDoesNotCreateTheNeutralResx()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(LocalizedWithTimer));
        var created = 0;
        session.CreateResourceDocument = () =>
        {
            ++created;
            return Task.FromResult(WinFormsResourceDocumentTemplate.Empty);
        };
        ShowItalian(session);
        var designerBefore = session.Document.Source;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SetImageResourcePropertyAsync(session.FindControl("Button1")!, "Image", PngBytes, isIcon: false));

        Assert.Equal(NotInLanguage, thrown.Message);
        Assert.Equal(0, created);
        Assert.Null(session.ResourceSource);
        Assert.Equal(designerBefore, session.Document.Source);
    }

    /// <remarks>
    /// Defect reintroduced once (the undo applied the edit without showing the language it was made in): the undo
    /// ran with Italian on screen, and the first Culture assertion failed with "Value is not null".
    /// </remarks>
    [Fact]
    public async Task UndoAndRedoOfADefaultEditShowDefaultFirstAndLeaveTheItalianFileAlone()
    {
        using var session = NewSession();
        var italian = new WinFormsResourceDocument(WinFormsDesignerLocalizationSessionTests.ItalianResources);
        session.LoadCultureResources = culture => Task.FromResult<WinFormsResourceDocument?>(culture == "it" ? italian : null);
        var italianWrites = 0;
        session.CommitCultureResourceSource = (_, _, _) => ++italianWrites;
        var designerBefore = session.Document.Source;
        await session.AddControlAsync(typeof(Forms.Button), "Me");
        var designerAdded = session.Document.Source;
        ShowItalian(session);

        await session.UndoAsync();

        // The edit was made in (Default), so the undo shows (Default) and removes the control the way a (Default) undo does.
        Assert.Null(session.Culture);
        Assert.Equal(designerBefore, session.Document.Source);
        Assert.Null(session.FindControl("Button2"));
        Assert.Equal(0, italianWrites);

        ShowItalian(session);
        await session.RedoAsync();

        Assert.Null(session.Culture);
        Assert.Equal(designerAdded, session.Document.Source);
        Assert.NotNull(session.FindControl("Button2"));
        Assert.Equal(0, italianWrites);
    }
}
