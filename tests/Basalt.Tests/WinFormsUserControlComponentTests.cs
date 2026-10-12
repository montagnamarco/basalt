using System.ComponentModel;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// A project component placed from the designer: written the way Visual Studio writes it, kept in the component tray and
/// not on the form, read back byte for byte, and deleted and restored by undo. Each test builds the fixture project for real,
/// because the designer's output has to compile.
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WinFormsUserControlComponentTests
{
    private sealed class Opened : IDisposable
    {
        public required WinFormsUserControlProject Project { get; init; }
        public required WinFormsDesignAssemblyContext Context { get; init; }

        public Type TypeOf(string name) => Context.ProjectAssembly.GetType("App." + name, throwOnError: true)!;

        public void Dispose()
        {
            Context.Unload();
            Project.Dispose();
        }
    }

    /// <summary>A fixture project, prepared by <paramref name="prepare"/>, built and opened as the design context of the designer.</summary>
    private static async Task<Opened> OpenProjectAsync(Action<WinFormsUserControlProject>? prepare = null)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();

        var project = WinFormsUserControlProject.Create();
        try
        {
            prepare?.Invoke(project);
            var build = await new MsBuildBuildService().BuildAsync(project.ProjectPath);
            Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Diagnostics));
            var context = WinFormsDesignAssemblyContext.Open(Assert.IsType<string>(build.OutputAssemblyPath));
            return new Opened { Project = project, Context = context };
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    /// <summary>A session on the project's Form1.Designer.vb, read from its text as the designer reads any file.</summary>
    private static WinFormsDesignerSession OpenSession(Opened opened)
    {
        var source = File.ReadAllText(opened.Project.DesignerPath);
        return new WinFormsDesignerSession(new WinFormsDesignerDocument(source), null, opened.Project.ProjectPath, opened.Context);
    }

    /// <summary>Writes the session's Designer.vb to the project and builds the project: the designer's output must compile.</summary>
    private static async Task AssertProjectBuildsWithDesignerTextAsync(Opened opened, WinFormsDesignerSession session)
    {
        File.WriteAllText(opened.Project.DesignerPath, session.Document.Source);
        var build = await new MsBuildBuildService().BuildAsync(opened.Project.ProjectPath);
        Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Diagnostics));
    }

    /// <summary>
    /// A project component placed from the designer is written as Visual Studio writes a component: a field with WithEvents and
    /// a constructor call in InitializeComponent. It is kept in the component tray, not on the form, and the project still builds.
    /// </summary>
    /// <remarks>
    /// Red-proof (D3, a field no Controls collection adds is a control): the component is a control, not in Document.Components,
    /// FindComponent is null and the Assert.Contains on Components fails.
    /// </remarks>
    [Fact]
    public async Task APlacedProjectComponentIsWrittenAsVisualStudioWritesItAndKeptInTheTray()
    {
        using var opened = await OpenProjectAsync();
        using var session = OpenSession(opened);
        var type = opened.TypeOf("ProjectComponent");

        await session.AddComponentAsync(type);

        var source = session.Document.Source;
        Assert.Contains("Friend WithEvents ProjectComponent1 As Global.App.ProjectComponent", source);
        Assert.Contains("Me.ProjectComponent1 = New Global.App.ProjectComponent()", source);
        Assert.Contains(session.Document.Components, component => component.Name == "ProjectComponent1");
        Assert.DoesNotContain(session.Document.Controls, control => control.Name == "ProjectComponent1");
        Assert.DoesNotContain(session.Form.Controls.Cast<Forms.Control>(), control => control.Name == "ProjectComponent1");
        Assert.Same(type, session.FindComponent("ProjectComponent1")!.GetType());

        await AssertProjectBuildsWithDesignerTextAsync(opened, session);
    }

    /// <summary>
    /// A component whose constructor takes IContainer is given the form's components container, as Visual Studio gives it, and
    /// the preview creates it with that container.
    /// </summary>
    /// <remarks>
    /// Not reintroduced on its own. Under the combined defects (D1 to D4) it failed with the other placement tests.    /// </remarks>
    [Fact]
    public async Task AComponentWhoseConstructorTakesTheContainerIsGivenTheFormsComponentsContainer()
    {
        using var opened = await OpenProjectAsync(project => project.DeclareComponentsContainer());
        using var session = OpenSession(opened);

        await session.AddComponentAsync(opened.TypeOf("ContainerComponent"));

        Assert.Contains("Friend WithEvents ContainerComponent1 As Global.App.ContainerComponent", session.Document.Source);
        Assert.Contains("Me.ContainerComponent1 = New Global.App.ContainerComponent(Me.components)", session.Document.Source);
        Assert.NotNull(session.FindComponent("ContainerComponent1"));
        await AssertProjectBuildsWithDesignerTextAsync(opened, session);
    }

    /// <summary>
    /// A component whose constructor takes IContainer, placed on a form that declares no container, makes the form declare one
    /// first, as Visual Studio does: <c>Private components As System.ComponentModel.IContainer</c>, and
    /// <c>Me.components = New System.ComponentModel.Container()</c> at the top of InitializeComponent. The component then gets
    /// <c>Me.components</c>, and the project builds.
    /// </summary>
    /// <remarks>
    /// Red-proof (D2, the container declaration removed): the component is written with Me.components and no container is declared,
    /// so the Assert.Contains on the field fails and the project does not build.
    /// </remarks>
    [Fact]
    public async Task AComponentWhoseConstructorTakesTheContainerDeclaresTheFormsContainerWhenItHasNone()
    {
        using var opened = await OpenProjectAsync();
        using var session = OpenSession(opened);

        await session.AddComponentAsync(opened.TypeOf("ContainerComponent"));

        var source = session.Document.Source;
        Assert.Contains("    Private components As System.ComponentModel.IContainer", source);
        Assert.Contains("        Me.components = New System.ComponentModel.Container()", source);
        Assert.Contains("Me.ContainerComponent1 = New Global.App.ContainerComponent(Me.components)", source);
        Assert.Contains(session.Document.Components, component => component.Name == "ContainerComponent1");
        Assert.NotNull(session.FindComponent("ContainerComponent1"));
        // The container is the first statement of InitializeComponent, so the component's constructor can take it.
        const string signature = "Private Sub InitializeComponent()";
        var body = source[(source.IndexOf(signature, StringComparison.Ordinal) + signature.Length)..];
        Assert.StartsWith("Me.components = New System.ComponentModel.Container()", body.TrimStart());
        await AssertProjectBuildsWithDesignerTextAsync(opened, session);
    }

    /// <summary>
    /// The designer's output is read back as it was written, and the component is still in the tray: reopening the designer
    /// keeps the component where the user put it.
    /// </summary>
    /// <remarks>
    /// Red-proof (D3): the component is a control, not in Document.Components, and the Assert.Contains on Components fails.
    /// </remarks>
    [Fact]
    public async Task ReadingThePlacedDesignerAgainKeepsTheComponentInTheTrayAndWritesItBackUnchanged()
    {
        using var opened = await OpenProjectAsync();
        using var session = OpenSession(opened);
        await session.AddComponentAsync(opened.TypeOf("ProjectComponent"));
        var placed = session.Document.Source;
        File.WriteAllText(opened.Project.DesignerPath, placed);

        var reread = new WinFormsDesignerDocument(File.ReadAllText(opened.Project.DesignerPath));

        Assert.Equal(placed, reread.Source);
        Assert.Contains(reread.Components, component => component.Name == "ProjectComponent1");
        Assert.DoesNotContain(reread.Controls, control => control.Name == "ProjectComponent1");
        using var reopened = new WinFormsDesignerSession(reread, null, opened.Project.ProjectPath, opened.Context);
        Assert.Same(opened.TypeOf("ProjectComponent"), reopened.FindComponent("ProjectComponent1")!.GetType());
        Assert.DoesNotContain(reopened.PreviewProblems, problem => problem.StartsWith("ProjectComponent1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Deleting a placed component removes its field and its statements, and one undo step puts back exactly the text it had.
    /// </summary>
    /// <remarks>
    /// Red-proof (D3): under the classifier defect the component is not in the tray after the delete's rebuild, and the Assert.NotNull fails.
    /// </remarks>
    [Fact]
    public async Task DeletingAPlacedComponentRemovesItsLinesAndUndoPutsThemBack()
    {
        using var opened = await OpenProjectAsync();
        using var session = OpenSession(opened);
        await session.AddComponentAsync(opened.TypeOf("ProjectComponent"));
        var placed = session.Document.Source;

        await session.DeleteComponentAsync(session.FindComponent("ProjectComponent1")!);

        Assert.DoesNotContain("ProjectComponent1", session.Document.Source, StringComparison.Ordinal);
        Assert.Null(session.FindComponent("ProjectComponent1"));

        await session.UndoAsync();

        Assert.Equal(placed, session.Document.Source);
        Assert.Same(opened.TypeOf("ProjectComponent"), session.FindComponent("ProjectComponent1")!.GetType());
    }

    /// <summary>
    /// A component whose constructor throws is contained as a control's is: the failure reaches the designer's failure list,
    /// the component is not created, and the form stays open with the component still placed in the tray.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the BuildComponentPreviews catch removed, AddComponentAsync throws out of the session and the test fails
    /// at the await.
    /// </remarks>
    [Fact]
    public async Task AComponentWhoseConstructorFailsIsReportedAndTheFormStillOpens()
    {
        using var opened = await OpenProjectAsync();
        using var session = OpenSession(opened);

        await session.AddComponentAsync(opened.TypeOf("BrokenComponent"));

        Assert.Null(session.FindComponent("BrokenComponent1"));
        Assert.Contains(session.UserCodeFailures, line => line.Contains("Component constructor failed on purpose.", StringComparison.Ordinal));
        Assert.Contains("Friend WithEvents BrokenComponent1 As Global.App.BrokenComponent", session.Document.Source);
        Assert.Contains(session.Document.Components, component => component.Name == "BrokenComponent1");
        Assert.NotNull(session.Form);
    }

    /// <summary>
    /// A TableLayoutPanel takes its control with the cell after it: <c>Controls.Add(Me.Label1, 0, 0)</c>. That control is a child
    /// of the panel, not a tray component, and the designer reads and writes the file back byte for byte.
    /// </summary>
    /// <remarks>
    /// Red-proof (D1, Add accepting only one argument): Label1 is not a child of the panel and is classified as a tray component, so
    /// the Assert.Single and the DoesNotContain on Components fail.
    /// </remarks>
    [Fact]
    public void AControlAddedWithACellOfATableLayoutPanelStaysAControlAndRoundTrips()
    {
        const string source = """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Friend WithEvents TableLayoutPanel1 As System.Windows.Forms.TableLayoutPanel
                Friend WithEvents Label1 As System.Windows.Forms.Label

                Private Sub InitializeComponent()
                    Me.TableLayoutPanel1 = New System.Windows.Forms.TableLayoutPanel()
                    Me.Label1 = New System.Windows.Forms.Label()
                    Me.SuspendLayout()
                    Me.TableLayoutPanel1.SuspendLayout()
                    Me.TableLayoutPanel1.Controls.Add(Me.Label1, 0, 0)
                    Me.Controls.Add(Me.TableLayoutPanel1)
                    Me.ClientSize = New System.Drawing.Size(500, 400)
                    Me.TableLayoutPanel1.ResumeLayout(False)
                    Me.ResumeLayout(False)
                End Sub
            End Class
            """;

        var document = new WinFormsDesignerDocument(source);

        var label = Assert.Single(document.Controls, control => control.Name == "Label1");
        Assert.Equal("TableLayoutPanel1", label.ParentName);
        Assert.DoesNotContain(document.Components, component => component.Name == "Label1");
        Assert.Equal(source, document.Source);
    }

    /// <summary>
    /// A build that replaces the design context keeps the placed component in the tray: the session reads the same file, and
    /// the component is found in the new context's type.
    /// </summary>
    /// <remarks>
    /// Red-proof (D3, a non-tray field classified as a control): the component is a control after the swap's re-read, and the
    /// Assert.Contains on Components fails. The swap keeps the text, so the failure is the classification.
    /// </remarks>
    [Fact]
    public async Task ABuildSwapKeepsThePlacedComponentInTheTray()
    {
        using var opened = await OpenProjectAsync();
        using var session = OpenSession(opened);
        await session.AddComponentAsync(opened.TypeOf("ProjectComponent"));
        var placed = session.Document.Source;

        var build = await new MsBuildBuildService().BuildAsync(opened.Project.ProjectPath);
        Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Diagnostics));
        var swapped = WinFormsDesignAssemblyContext.Open(Assert.IsType<string>(build.OutputAssemblyPath));
        try
        {
            session.UseDesignContext(swapped);

            Assert.Equal(placed, session.Document.Source);
            Assert.Contains(session.Document.Components, component => component.Name == "ProjectComponent1");
            Assert.Same(swapped.ProjectAssembly.GetType("App.ProjectComponent", throwOnError: true),
                session.FindComponent("ProjectComponent1")!.GetType());
        }
        finally
        {
            swapped.Unload();
        }
    }

    /// <summary>
    /// A field that a collection other than a Controls collection holds is an item of that collection: a custom menu item and a
    /// custom grid column are neither controls nor tray components. Deleting a tray component leaves the collections' statements
    /// byte for byte, so their siblings stay attached.
    /// </summary>
    /// <remarks>
    /// Red-proof (the item rule removed from ClassifyField): Gauge and Column1 are tray components, so the Assert.Equal on the
    /// component names fails, and a delete would remove their AddRange statements, which the last assertions check.
    /// </remarks>
    [Fact]
    public void ACustomItemOfACollectionIsNeitherAControlNorATrayComponent()
    {
        const string source = """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Friend WithEvents Timer1 As System.Windows.Forms.Timer
                Friend WithEvents MenuStrip1 As System.Windows.Forms.MenuStrip
                Friend WithEvents Gauge As App.GaugeMenuItem
                Friend WithEvents Grid As System.Windows.Forms.DataGridView
                Friend WithEvents Column1 As App.CalendarColumn

                Private Sub InitializeComponent()
                    Me.Timer1 = New System.Windows.Forms.Timer()
                    Me.MenuStrip1 = New System.Windows.Forms.MenuStrip()
                    Me.Gauge = New App.GaugeMenuItem()
                    Me.Grid = New System.Windows.Forms.DataGridView()
                    Me.Column1 = New App.CalendarColumn()
                    Me.MenuStrip1.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.Gauge})
                    Me.Grid.Columns.AddRange(New System.Windows.Forms.DataGridViewColumn() {Me.Column1})
                    Me.Controls.Add(Me.MenuStrip1)
                    Me.Controls.Add(Me.Grid)
                    Me.Timer1.Interval = 500
                End Sub
            End Class
            """;

        var document = new WinFormsDesignerDocument(source);

        Assert.Equal(new[] { "Timer1" }, document.Components.Select(component => component.Name));
        Assert.Equal(new[] { "Grid", "MenuStrip1" }, document.Controls.Select(control => control.Name).OrderBy(name => name, StringComparer.Ordinal));
        var addRanges = source.Split('\n').Where(line => line.Contains("AddRange", StringComparison.Ordinal)).ToArray();
        var removed = new WinFormsDesignerDocument(source).RemoveComponent("Timer1");
        Assert.DoesNotContain("Timer1", removed, StringComparison.Ordinal);
        Assert.Equal(addRanges, removed.Split('\n').Where(line => line.Contains("AddRange", StringComparison.Ordinal)).ToArray());
    }

    /// <summary>
    /// A built-in Timer or ToolTip is placed with the form's components container, as Visual Studio places it: the container is
    /// declared first when the form has none, and the component takes Me.components.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the session's AddComponentAsync passing takesContainer as false, the Timer is written as
    /// <c>New Global.System.Windows.Forms.Timer()</c> and the Assert.Contains on the container argument fails.
    /// </remarks>
    [Fact]
    public async Task APlacedTimerAndToolTipTakeTheFormsComponentsContainer()
    {
        const string source = """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Private Sub InitializeComponent()
                    Me.ClientSize = New System.Drawing.Size(500, 400)
                    Me.Text = "Form1"
                    Me.ResumeLayout(False)
                End Sub
            End Class
            """;
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));

        await session.AddComponentAsync(typeof(Forms.Timer));
        await session.AddComponentAsync(typeof(Forms.ToolTip));

        var written = session.Document.Source;
        Assert.Contains("    Private components As System.ComponentModel.IContainer", written, StringComparison.Ordinal);
        Assert.Contains("        Me.components = New System.ComponentModel.Container()", written, StringComparison.Ordinal);
        Assert.Equal(1, written.Split("Private components As System.ComponentModel.IContainer", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, written.Split("Me.components = New System.ComponentModel.Container()", StringSplitOptions.None).Length - 1);
        Assert.Contains("Me.Timer1 = New Global.System.Windows.Forms.Timer(Me.components)", written, StringComparison.Ordinal);
        Assert.Contains("Me.ToolTip1 = New Global.System.Windows.Forms.ToolTip(Me.components)", written, StringComparison.Ordinal);
        Assert.NotNull(session.FindComponent("Timer1"));
        Assert.NotNull(session.FindComponent("ToolTip1"));
    }

    /// <summary>
    /// The designer's Add order of a TableLayoutPanel's children is the z-order the preview keeps: Label2 is added before Label1,
    /// although Label1 is assigned first, so the preview puts Label2 at the front.
    /// </summary>
    /// <remarks>
    /// Red-proof (R6, the three-argument Add ignored by the z-order): the z-order falls back to the assignment order, Label1 is
    /// placed first, and the Assert.Equal on Label2's child index fails.
    /// </remarks>
    [Fact]
    public void TheAddOrderOfATableLayoutPanelsChildrenIsTheZOrderThePreviewKeeps()
    {
        const string source = """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Friend WithEvents TableLayoutPanel1 As System.Windows.Forms.TableLayoutPanel
                Friend WithEvents Label1 As System.Windows.Forms.Label
                Friend WithEvents Label2 As System.Windows.Forms.Label

                Private Sub InitializeComponent()
                    Me.TableLayoutPanel1 = New System.Windows.Forms.TableLayoutPanel()
                    Me.Label1 = New System.Windows.Forms.Label()
                    Me.Label2 = New System.Windows.Forms.Label()
                    Me.TableLayoutPanel1.ColumnCount = 2
                    Me.TableLayoutPanel1.RowCount = 1
                    Me.TableLayoutPanel1.Controls.Add(Me.Label2, 1, 0)
                    Me.TableLayoutPanel1.Controls.Add(Me.Label1, 0, 0)
                    Me.Controls.Add(Me.TableLayoutPanel1)
                End Sub
            End Class
            """;
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));

        var table = (Forms.TableLayoutPanel)session.FindControl("TableLayoutPanel1")!;
        Assert.Equal(0, table.Controls.GetChildIndex(session.FindControl("Label2")!));
        Assert.Equal(1, table.Controls.GetChildIndex(session.FindControl("Label1")!));
    }
    private const string TableAddOrderSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Friend WithEvents TableLayoutPanel1 As System.Windows.Forms.TableLayoutPanel
            Friend WithEvents Label1 As System.Windows.Forms.Label
            Friend WithEvents Label2 As System.Windows.Forms.Label

            Private Sub InitializeComponent()
                Me.TableLayoutPanel1 = New System.Windows.Forms.TableLayoutPanel()
                Me.Label1 = New System.Windows.Forms.Label()
                Me.Label2 = New System.Windows.Forms.Label()
                Me.TableLayoutPanel1.ColumnCount = 2
                Me.TableLayoutPanel1.RowCount = 1
                Me.TableLayoutPanel1.Controls.Add(Me.Label2, 1, 0)
                Me.TableLayoutPanel1.Controls.Add(Me.Label1, 0, 0)
                Me.Controls.Add(Me.TableLayoutPanel1)
            End Sub
        End Class
        """;

    private const string TwoButtonsInPanelSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Friend WithEvents SplitContainer1 As System.Windows.Forms.SplitContainer
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Button2 As System.Windows.Forms.Button

            Private Sub InitializeComponent()
                Me.SplitContainer1 = New System.Windows.Forms.SplitContainer()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.Button2 = New System.Windows.Forms.Button()
                Me.SplitContainer1.Panel1.Controls.Add(Me.Button1)
                Me.SplitContainer1.Panel1.Controls.Add(Me.Button2)
                Me.Controls.Add(Me.SplitContainer1)
            End Sub
        End Class
        """;

    /// <summary>
    /// Bringing a TableLayoutPanel child to the front writes its order in the designer's code, and the child keeps its cell
    /// through the rebuild. Label1 is added after Label2, so it is not at the front and the written order is a change.
    /// </summary>
    /// <remarks>
    /// Red-proof (R6, the three-argument Add ignored by the z-order): the order falls back to the assignment order, which already
    /// puts Label1 first, so no SetChildIndex is written and the Assert.Contains on the written line fails.
    /// </remarks>
    [Fact]
    public async Task BringingATableLayoutChildToTheFrontWritesItsOrderAndKeepsItsCell()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(TableAddOrderSource));
        await session.SetZOrderAsync([session.FindControl("Label1")!], bringToFront: true);

        Assert.Contains("Me.TableLayoutPanel1.Controls.SetChildIndex(Me.Label1, 0)", session.Document.Source, StringComparison.Ordinal);
        var table = (Forms.TableLayoutPanel)session.FindControl("TableLayoutPanel1")!;
        var label1 = session.FindControl("Label1")!;
        Assert.Equal(0, table.Controls.GetChildIndex(label1));
        Assert.Equal(0, table.GetColumn(label1));
        Assert.Equal(1, table.GetColumn(session.FindControl("Label2")!));
    }

    /// <summary>
    /// A child of a SplitContainer's panel is ordered in that panel: the written line names Me.SplitContainer1.Panel1 as its receiver,
    /// as Visual Studio writes it, and the rebuild from the written text runs it without an exception.
    /// </summary>
    /// <remarks>
    /// Red-proof (the z-order key taken from the owner alone): the line is written as Me.SplitContainer1.Controls, so the
    /// DoesNotContain on that receiver fails.
    /// </remarks>
    [Fact]
    public async Task SendingAPanelChildToTheBackIsWrittenInItsPanelAndRebuildsWithoutAnException()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(TwoButtonsInPanelSource));

        await session.SetZOrderAsync([session.FindControl("Button1")!], bringToFront: false);

        var written = session.Document.Source;
        Assert.Contains("Me.SplitContainer1.Panel1.Controls.SetChildIndex(Me.Button2, 0)", written, StringComparison.Ordinal);
        Assert.Contains("Me.SplitContainer1.Panel1.Controls.SetChildIndex(Me.Button1, 1)", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Me.SplitContainer1.Controls.SetChildIndex", written, StringComparison.Ordinal);
        using var reread = new WinFormsDesignerSession(new WinFormsDesignerDocument(written));
        var panel = ((Forms.SplitContainer)reread.FindControl("SplitContainer1")!).Panel1;
        var button1 = reread.FindControl("Button1")!;
        Assert.Same(panel, button1.Parent);
        Assert.Equal(1, panel.Controls.GetChildIndex(button1));
    }

    /// <summary>A child of a SplitContainer's panel brought to the front is written in its panel, as the send-to-back test shows.</summary>
    /// <remarks>
    /// Red-proof (the z-order key taken from the owner alone): the line is written on Me.SplitContainer1.Controls, so the
    /// Assert.Contains on the panel's receiver fails.
    /// </remarks>
    [Fact]
    public async Task BringingAPanelChildToTheFrontIsWrittenInItsPanel()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(TwoButtonsInPanelSource));

        await session.SetZOrderAsync([session.FindControl("Button2")!], bringToFront: true);

        var written = session.Document.Source;
        Assert.Contains("Me.SplitContainer1.Panel1.Controls.SetChildIndex(Me.Button2, 0)", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Me.SplitContainer1.Controls.SetChildIndex", written, StringComparison.Ordinal);
        using var reread = new WinFormsDesignerSession(new WinFormsDesignerDocument(written));
        var panel = ((Forms.SplitContainer)reread.FindControl("SplitContainer1")!).Panel1;
        Assert.Equal(0, panel.Controls.GetChildIndex(reread.FindControl("Button2")!));
    }

    /// <summary>
    /// A toolbox item double-clicked while a control inside a container's panel is selected goes into that panel, as Visual Studio
    /// writes it: the written Add names the panel, and reading the text back makes the new control a child of the panel.
    /// </summary>
    /// <remarks>
    /// Red-proof (AddControl writing the form as the receiver): the Assert.Contains on the panel's Add line fails.
    /// Red-proof (NameOf not naming a panel): the call to NameOf throws InvalidOperationException.
    /// </remarks>
    [Fact]
    public async Task AToolboxItemDoubleClickedWhileAPanelChildIsSelectedGoesIntoThePanel()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(TwoButtonsInPanelSource));
        var panel = session.FindControl("Button1")!.Parent!;

        var parentName = session.NameOf(panel);
        await session.AddControlAsync(typeof(Forms.Button), parentName);

        Assert.Equal("SplitContainer1.Panel1", parentName);
        var written = session.Document.Source;
        Assert.Contains("Me.SplitContainer1.Panel1.Controls.Add(Me.Button3)", written, StringComparison.Ordinal);
        AssertPanelChild(written, "Button3");
    }

    /// <summary>
    /// A toolbox item dropped into a container's panel is written into the panel at the drop point, which is relative to the panel,
    /// and reading the text back makes it a child of the panel.
    /// </summary>
    /// <remarks>
    /// Red-proof (AddControl writing the form as the receiver): the Assert.Contains on the panel's Add line fails.
    /// </remarks>
    [Fact]
    public async Task AToolboxItemDroppedIntoAContainersPanelIsWrittenIntoThePanelAtTheDropPoint()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(TwoButtonsInPanelSource));
        var panel = session.FindControl("Button1")!.Parent!;

        await session.AddControlAtAsync(typeof(Forms.Button), session.NameOf(panel), new System.Drawing.Point(10, 20));

        var written = session.Document.Source;
        Assert.Contains("Me.SplitContainer1.Panel1.Controls.Add(Me.Button3)", written, StringComparison.Ordinal);
        Assert.Contains("Me.Button3.Location = New System.Drawing.Point(10, 20)", written, StringComparison.Ordinal);
        AssertPanelChild(written, "Button3");
    }

    /// <summary>
    /// A control pasted while a container's panel is the target goes into that panel: the written Add names the panel, and reading
    /// the text back makes the pasted control a child of the panel.
    /// </summary>
    /// <remarks>
    /// Red-proof (PasteControls writing the form as the receiver): the Assert.Contains on the panel's Add line fails.
    /// </remarks>
    [Fact]
    public async Task PastingIntoAContainersPanelIsWrittenIntoThePanel()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(TwoButtonsInPanelSource));
        var clipboard = new WinFormsDesignerDocument(TwoButtonsInPanelSource).CopyControls(["Button2"]);

        var names = await session.PasteControlsAsync(clipboard, session.FindControl("Button1")!.Parent!);

        var pasted = Assert.Single(names);
        var written = session.Document.Source;
        Assert.Contains($"Me.SplitContainer1.Panel1.Controls.Add(Me.{pasted})", written, StringComparison.Ordinal);
        AssertPanelChild(written, pasted);
    }

    /// <summary>Reads the text back and asserts that the control is a child of the SplitContainer's Panel1, and that the preview shows it there.</summary>
    private static void AssertPanelChild(string written, string name)
    {
        var reread = new WinFormsDesignerDocument(written);
        var child = Assert.Single(reread.Controls, control => control.Name == name);
        Assert.Equal("SplitContainer1", child.ParentName);
        Assert.Equal("Panel1", child.ParentPanel);
        using var reopened = new WinFormsDesignerSession(reread);
        Assert.Same(((Forms.SplitContainer)reopened.FindControl("SplitContainer1")!).Panel1, reopened.FindControl(name)!.Parent);
    }

    /// <summary>
    /// A form whose project control has a Panel1 getter that throws opens: the preview places a child of that control without
    /// running the project's getter, because only the Forms containers' panels are read.
    /// </summary>
    /// <remarks>
    /// Red-proof (the panel read by reflection on the owner, as the first version did): the throwing getter runs, the session
    /// constructor throws a TargetInvocationException and the test fails at OpenSession.
    /// </remarks>
    [Fact]
    public async Task APreviewOfAProjectControlWhosePanelGetterThrowsStillOpens()
    {
        using var opened = await OpenProjectAsync(project =>
        {
            File.WriteAllText(Path.Combine(project.Root, "ThrowingPanelHost.vb"), """
                Public Class ThrowingPanelHost
                    Inherits System.Windows.Forms.Panel

                    Public ReadOnly Property Panel1 As System.Windows.Forms.Control
                        Get
                            Throw New System.InvalidOperationException("A project getter ran.")
                        End Get
                    End Property
                End Class
                """);
            File.WriteAllText(project.DesignerPath, """
                Partial Class Form1
                    Inherits System.Windows.Forms.Form

                    Friend WithEvents Holder As Global.App.ThrowingPanelHost
                    Friend WithEvents Button1 As System.Windows.Forms.Button

                    Private Sub InitializeComponent()
                        Me.Holder = New Global.App.ThrowingPanelHost()
                        Me.Button1 = New System.Windows.Forms.Button()
                        Me.Holder.Panel1.Controls.Add(Me.Button1)
                        Me.Controls.Add(Me.Holder)
                    End Sub
                End Class
                """);
        });

        using var session = OpenSession(opened);

        var holder = session.FindControl("Holder");
        Assert.NotNull(holder);
        Assert.Same(holder, session.FindControl("Button1")!.Parent);
    }

    /// <summary>
    /// A container's panel is read from the Forms container itself, typed: a project member that is named as a panel and throws is
    /// never run, and an unknown panel name holds the child in its owner.
    /// </summary>
    /// <remarks>
    /// Red-proof (HolderOf reading the panel by reflection on the owner): the throwing Panel1 getter runs and the test fails with a
    /// TargetInvocationException.
    /// </remarks>
    [Fact]
    public void AProjectMemberNamedAsAPanelIsNeverRun()
    {
        var owner = new ThrowingPanelControl();
        Assert.Same(owner, WinFormsDesignerSession.HolderOf(owner, "Panel1"));
        var split = new Forms.SplitContainer();
        Assert.Same(split.Panel2, WinFormsDesignerSession.HolderOf(split, "Panel2"));
        Assert.Same(split, WinFormsDesignerSession.HolderOf(split, "Unknown"));
    }

    public sealed class ThrowingPanelControl : Forms.Control
    {
        public Forms.Control Panel1 => throw new InvalidOperationException("A project getter ran.");
    }

    private const string ContainersSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Friend WithEvents SplitContainer1 As System.Windows.Forms.SplitContainer
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents TextBox1 As System.Windows.Forms.TextBox

            Private Sub InitializeComponent()
                Me.SplitContainer1 = New System.Windows.Forms.SplitContainer()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.TextBox1 = New System.Windows.Forms.TextBox()
                Me.SplitContainer1.Panel1.Controls.Add(Me.Button1)
                Me.SplitContainer1.Panel2.Controls.Add(Me.TextBox1)
                Me.Controls.Add(Me.SplitContainer1)
            End Sub
        End Class
        """;

    private const string ToolStripContainerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Friend WithEvents ToolStripContainer1 As System.Windows.Forms.ToolStripContainer
            Friend WithEvents TextBox1 As System.Windows.Forms.TextBox

            Private Sub InitializeComponent()
                Me.ToolStripContainer1 = New System.Windows.Forms.ToolStripContainer()
                Me.TextBox1 = New System.Windows.Forms.TextBox()
                Me.ToolStripContainer1.ContentPanel.Controls.Add(Me.TextBox1)
                Me.Controls.Add(Me.ToolStripContainer1)
            End Sub
        End Class
        """;

    /// <summary>
    /// A copied SplitContainer pastes with each child in its own panel: the written Adds name Panel1 and Panel2 of the pasted
    /// container, and the text read back parents each child to its panel.
    /// </summary>
    /// <remarks>
    /// Red-proof (the paste writing the container without its panel): the Panel1 and Panel2 lines are written on the container
    /// and the Assert.Single on the children's panels fails.
    /// </remarks>
    [Fact]
    public async Task CopyAndPasteOfASplitContainerKeepsEachChildInItsPanel()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(ContainersSource));
        var clipboard = session.CopyControls([session.FindControl("SplitContainer1")!, session.FindControl("Button1")!, session.FindControl("TextBox1")!]);

        await session.PasteControlsAsync(clipboard, session.Form);

        var written = session.Document.Source;
        var reread = new WinFormsDesignerDocument(written);
        var container = reread.Controls.Single(control => control.ParentName == "Me" && control.Name != "SplitContainer1"
            && control.TypeName.EndsWith("SplitContainer", StringComparison.Ordinal)).Name;
        var button = Assert.Single(reread.Controls, control => control.ParentName == container && control.ParentPanel == "Panel1").Name;
        var textBox = Assert.Single(reread.Controls, control => control.ParentName == container && control.ParentPanel == "Panel2").Name;
        Assert.Contains($"Me.{container}.Panel1.Controls.Add(Me.{button})", written, StringComparison.Ordinal);
        Assert.Contains($"Me.{container}.Panel2.Controls.Add(Me.{textBox})", written, StringComparison.Ordinal);
        Assert.DoesNotContain($"Me.{container}.Controls.Add", written, StringComparison.Ordinal);
    }

    /// <summary>A copied ToolStripContainer pastes its child into the ContentPanel it was in, and the text read back parents it there.</summary>
    /// <remarks>
    /// Red-proof (the paste writing the container without its panel): the ContentPanel line is written on the container, which a
    /// ToolStripContainer's read-only Controls refuses in WinForms, and the Assert.Single on the ContentPanel child fails.
    /// </remarks>
    [Fact]
    public async Task CopyAndPasteOfAToolStripContainerKeepsItsChildInTheContentPanel()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(ToolStripContainerSource));
        var clipboard = session.CopyControls([session.FindControl("ToolStripContainer1")!, session.FindControl("TextBox1")!]);

        await session.PasteControlsAsync(clipboard, session.Form);

        var written = session.Document.Source;
        var reread = new WinFormsDesignerDocument(written);
        var container = reread.Controls.Single(control => control.ParentName == "Me" && control.Name != "ToolStripContainer1").Name;
        var textBox = Assert.Single(reread.Controls, control => control.ParentName == container && control.ParentPanel == "ContentPanel").Name;
        Assert.Contains($"Me.{container}.ContentPanel.Controls.Add(Me.{textBox})", written, StringComparison.Ordinal);
        Assert.DoesNotContain($"Me.{container}.Controls.Add", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// A SplitContainer is cut and pasted back: the cut deletes the container with its children, and the paste writes the container
    /// and each child into its panel again.
    /// </summary>
    /// <remarks>
    /// Red-proof (the paste writing the container without its panel): the Assert.Single on the Panel2 child fails after the paste.
    /// </remarks>
    [Fact]
    public async Task CutAndPasteOfASplitContainerWritesEachChildBackIntoItsPanel()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(ContainersSource));
        var clipboard = session.CopyControls([session.FindControl("SplitContainer1")!, session.FindControl("Button1")!, session.FindControl("TextBox1")!]);
        await session.CheckPastableAsync(clipboard);

        await session.DeleteControlsAsync([session.FindControl("SplitContainer1")!]);
        Assert.DoesNotContain("SplitContainer1", session.Document.Source, StringComparison.Ordinal);

        await session.PasteControlsAsync(clipboard, session.Form);

        var reread = new WinFormsDesignerDocument(session.Document.Source);
        var container = reread.Controls.Single(control => control.ParentName == "Me" && control.TypeName.EndsWith("SplitContainer", StringComparison.Ordinal)).Name;
        Assert.Single(reread.Controls, control => control.ParentName == container && control.ParentPanel == "Panel1");
        Assert.Single(reread.Controls, control => control.ParentName == container && control.ParentPanel == "Panel2");
    }

    /// <summary>
    /// A cut is checked before it deletes: a payload that could not be pasted back is refused with the designer's own error, and
    /// the document does not change. The payload here copies a button whose Image reads a field the copy does not carry.
    /// </summary>
    /// <remarks>
    /// Red-proof (CheckPastable made a no-op): the call does not throw and the Assert.Throws fails.
    /// </remarks>
    [Fact]
    public async Task ACutOfAPayloadThatCannotBePastedBackIsRefusedBeforeAnyDelete()
    {
        const string withImage = """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Friend WithEvents ImageList1 As System.Windows.Forms.ImageList
                Friend WithEvents Button1 As System.Windows.Forms.Button

                Private Sub InitializeComponent()
                    Me.ImageList1 = New System.Windows.Forms.ImageList()
                    Me.Button1 = New System.Windows.Forms.Button()
                    Me.Button1.ImageList = Me.ImageList1
                    Me.Controls.Add(Me.Button1)
                End Sub
            End Class
            """;
        var clipboard = new WinFormsDesignerDocument(withImage).CopyControls(["Button1"]);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(ContainersSource));
        var before = session.Document.Source;

        await Assert.ThrowsAnyAsync<Exception>(() => session.CheckPastableAsync(clipboard));

        Assert.Equal(before, session.Document.Source);
    }

    /// <summary>A container's panel is refused as a deletion with the designer's own message, and the document does not change.</summary>
    /// <remarks>
    /// Red-proof (the panel check in SelectedDeletionRoots removed): the lookup of the dotted name throws a KeyNotFoundException,
    /// and the Assert.ThrowsAsync of the localized NotSupportedException fails.
    /// </remarks>
    [Fact]
    public async Task DeletingAContainersPanelIsRefusedWithTheDesignersOwnMessage()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(ContainersSource));
        var before = session.Document.Source;
        var panel = ((Forms.SplitContainer)session.FindControl("SplitContainer1")!).Panel1;

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => session.DeleteControlsAsync([panel]));

        Assert.Equal(Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.WinFormsDesignerPanelNotSelectable), error.Message);
        Assert.Equal(before, session.Document.Source);
    }

    /// <summary>Deleting a container removes it with its children and their lines, and one undo puts back the text it had.</summary>
    [Fact]
    public async Task DeletingASplitContainerRemovesItsChildrenAndUndoPutsThemBack()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(ContainersSource));
        var before = session.Document.Source;

        await session.DeleteControlsAsync([session.FindControl("SplitContainer1")!]);

        var source = session.Document.Source;
        Assert.DoesNotContain("SplitContainer1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Button1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBox1", source, StringComparison.Ordinal);
        await session.UndoAsync();
        Assert.Equal(before, session.Document.Source);
    }

    private const string SparseTableSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Friend WithEvents TableLayoutPanel1 As System.Windows.Forms.TableLayoutPanel
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Friend WithEvents Button2 As System.Windows.Forms.Button

            Private Sub InitializeComponent()
                Me.TableLayoutPanel1 = New System.Windows.Forms.TableLayoutPanel()
                Me.Button1 = New System.Windows.Forms.Button()
                Me.Button2 = New System.Windows.Forms.Button()
                Me.TableLayoutPanel1.ColumnCount = 4
                Me.TableLayoutPanel1.RowCount = 3
                Me.TableLayoutPanel1.Controls.Add(Me.Button1, 2, 1)
                Me.TableLayoutPanel1.Controls.Add(Me.Button2, 0, 0)
                Me.Controls.Add(Me.TableLayoutPanel1)
            End Sub
        End Class
        """;

    /// <summary>
    /// A copied TableLayoutPanel pastes each child into its own cell: the written Adds name the column and row, and the text read
    /// back gives each child the same cell.
    /// </summary>
    /// <remarks>
    /// Red-proof (the cell left out of the pasted Add): the one-argument Add is written, the cell assertions after the re-read fail.
    /// </remarks>
    [Fact]
    public async Task CopyAndPasteOfATableLayoutPanelKeepsEachChildInItsCell()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(SparseTableSource));
        var clipboard = session.CopyControls([session.FindControl("TableLayoutPanel1")!, session.FindControl("Button1")!, session.FindControl("Button2")!]);

        await session.PasteControlsAsync(clipboard, session.Form);

        var written = session.Document.Source;
        var reread = new WinFormsDesignerDocument(written);
        var table = reread.Controls.Single(control => control.ParentName == "Me" && control.Name != "TableLayoutPanel1"
            && control.TypeName.EndsWith("TableLayoutPanel", StringComparison.Ordinal)).Name;
        var wide = Assert.Single(reread.Controls, control => control.ParentName == table && control.Cell == ((int Column, int Row)?)(2, 1));
        var origin = Assert.Single(reread.Controls, control => control.ParentName == table && control.Cell == ((int Column, int Row)?)(0, 0));
        Assert.Contains($"Me.{table}.Controls.Add(Me.{wide.Name}, 2, 1)", written, StringComparison.Ordinal);
        Assert.Contains($"Me.{table}.Controls.Add(Me.{origin.Name}, 0, 0)", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cut whose pasted preview would throw is refused before anything is deleted: a copied form that the paste puts on the form
    /// makes the preview throw an ArgumentException, and the document does not change.
    /// </summary>
    /// <remarks>
    /// Red-proof (the preview build removed from CheckPastableAsync): the check does not throw and the Assert.ThrowsAny fails.
    /// </remarks>
    [Fact]
    public async Task ACutWhosePastedPreviewWouldThrowIsRefusedBeforeAnyDelete()
    {
        const string withForm = """
            Partial Class Form1
                Inherits System.Windows.Forms.Form

                Friend WithEvents Panel1 As System.Windows.Forms.Panel
                Friend WithEvents Form2 As System.Windows.Forms.Form

                Private Sub InitializeComponent()
                    Me.Panel1 = New System.Windows.Forms.Panel()
                    Me.Form2 = New System.Windows.Forms.Form()
                    Me.Panel1.Controls.Add(Me.Form2)
                    Me.Controls.Add(Me.Panel1)
                End Sub
            End Class
            """;
        var clipboard = new WinFormsDesignerDocument(withForm).CopyControls(["Form2"]);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(ContainersSource));
        var before = session.Document.Source;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => session.CheckPastableAsync(clipboard));

        Assert.Equal(before, session.Document.Source);
    }

    /// <summary>
    /// A preview build that fails after it has created a project control releases that control: the failed constructor leaves
    /// nothing alive that the designer's preview made.
    /// </summary>
    /// <remarks>
    /// Red-proof (the constructor's partial release removed): the tracked control is not disposed and the Assert.All on IsDisposed fails.
    /// </remarks>
    [Fact]
    public async Task APreviewThatFailsAfterCreatingAProjectControlDisposesIt()
    {
        using var opened = await OpenProjectAsync(project =>
        {
            File.WriteAllText(Path.Combine(project.Root, "TrackedHost.vb"), """
                Public Class TrackedHost
                    Inherits System.Windows.Forms.Panel

                    Public Shared ReadOnly Created As New System.Collections.Generic.List(Of TrackedHost)

                    Public Sub New()
                        Created.Add(Me)
                    End Sub
                End Class
                """);
            File.WriteAllText(project.DesignerPath, """
                Partial Class Form1
                    Inherits System.Windows.Forms.Form

                    Friend WithEvents Tracked As Global.App.TrackedHost
                    Friend WithEvents Form2 As System.Windows.Forms.Form

                    Private Sub InitializeComponent()
                        Me.Tracked = New Global.App.TrackedHost()
                        Me.Form2 = New System.Windows.Forms.Form()
                        Me.Tracked.Controls.Add(Me.Form2)
                        Me.Controls.Add(Me.Tracked)
                    End Sub
                End Class
                """);
        });
        var source = File.ReadAllText(opened.Project.DesignerPath);

        Assert.ThrowsAny<ArgumentException>(() => new WinFormsDesignerSession(new WinFormsDesignerDocument(source), null,
            opened.Project.ProjectPath, opened.Context));

        var tracked = opened.Context.ProjectAssembly.GetType("App.TrackedHost", throwOnError: true)!;
        var instances = ((System.Collections.IEnumerable)tracked.GetField("Created")!.GetValue(null)!).Cast<Forms.Control>().ToArray();
        Assert.NotEmpty(instances);
        Assert.All(instances, instance => Assert.True(instance.IsDisposed));
    }
}
