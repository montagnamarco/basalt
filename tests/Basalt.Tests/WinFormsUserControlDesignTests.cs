using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// The user's own controls in the WinForms designer, driven the way a user drives them: a real VB
/// project with a UserControl is built through <see cref="MainWindowViewModel.BuildProjectForTestsAsync"/>,
/// its form is opened in the designer, and the controls are placed by a real double-click on the
/// Toolbox. The control on the surface must be a real instance of the project's type, and a rebuild
/// must reload it.
/// </summary>
[Collection(WindowsDesktopCollection.Name)]
public sealed class WinFormsUserControlDesignTests
{
    private sealed class Journey : IDisposable
    {
        public required TestWindow Host { get; init; }
        public required MainWindowViewModel Vm { get; init; }
        public required WinFormsDesignSurface Surface { get; init; }
        public required WinFormsUserControlProject Project { get; init; }

        public string DesignerText => Vm.OpenDocuments.Single(d => d.FilePath == Project.DesignerPath).Text;

        public void Dispose()
        {
            Host.Dispose();
            Project.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Toolbox: the project's controls, under the project's own group.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheToolboxListsTheProjectsControlsUnderTheProjectsCategory()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);

        var items = ToolboxItems(journey);
        var gauge = Assert.Single(items, item => item.DisplayName == "GaugeControl");
        Assert.Equal(Localizer.Get(StringKeys.WinFormsDesignerProjectComponents, "App"), gauge.Category);

        // [ToolboxItem(False)] and forms are left out; the built-in Forms entries stay.
        Assert.DoesNotContain(items, item => item.DisplayName == "HiddenControl");
        Assert.DoesNotContain(items, item => item.DisplayName == "Form1");
        Assert.Contains(items, item => item.DisplayName == "Button" && item.Category == WinFormsToolboxCatalog.Common);
    }

    [AvaloniaFact]
    public async Task TheToolboxIsRefreshedWhenAFirstBuildLoadsTheProjectWhileTheDesignerIsOpen()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: false);
        Assert.DoesNotContain(ToolboxItems(journey), item => item.DisplayName == "GaugeControl");

        await BuildAsync(journey.Vm);

        await WaitUntilAsync(() => ToolboxItems(journey).Any(item => item.DisplayName == "GaugeControl"),
            "The toolbox was not refreshed after the build.");
    }

    // ------------------------------------------------------------------
    // Placing: the designer file names the project's type and the surface draws a real instance of it.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task PlacingAProjectControlWritesItsTypeAndDrawsTheRealControl()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);

        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedControl(journey, "GaugeControl1") is not null, "GaugeControl1 was not placed.");

        Assert.Equal("App.GaugeControl", PlacedTypeName(journey, "GaugeControl1"));
        Assert.True(PlacedIsFormsControl(journey, "GaugeControl1"), "The placed control is not a Forms control of the designer.");
        Assert.True(PlacedDerivesFromUserControl(journey, "GaugeControl1"),
            "The placed control does not derive from the designer's own UserControl.");
        await WaitUntilAsync(() => journey.DesignerText.Contains("New Global.App.GaugeControl()", StringComparison.Ordinal),
            "The designer file does not name the project's type.");

        // The project still builds with the control in its designer file.
        await BuildAsync(journey.Vm);
    }

    // ------------------------------------------------------------------
    // Rebuilding: the open designer takes the new build and collects the old context.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ARebuildReloadsTheControlWithTheNewBuild()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);

        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedControl(journey, "GaugeControl1") is not null, "GaugeControl1 was not placed.");
        Assert.Equal(42, PlacedValue(journey, "GaugeControl1"));

        journey.Project.WriteGauge(7);
        await BuildAsync(journey.Vm);

        await WaitUntilAsync(() => PlacedValue(journey, "GaugeControl1") == 7,
            "The surface still shows the control of the earlier build.");
    }

    // ------------------------------------------------------------------
    // Failures in the project's code: a placeholder, a line in Output, and the IDE keeps working.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AControlWhoseConstructorOrPaintFailsShowsAPlaceholderAndTheIdeKeepsWorking()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);

        await DoubleClickToolboxItemAsync(journey, "BrokenControl");
        await WaitUntilAsync(() => PlacedControl(journey, "BrokenControl1") is Forms.Label, "The constructor failure was not shown as a placeholder.");
        Assert.Contains("Constructor failed on purpose.", ((Forms.Label)PlacedControl(journey, "BrokenControl1")!).Text, StringComparison.Ordinal);

        await DoubleClickToolboxItemAsync(journey, "PaintBrokenControl");
        await WaitUntilAsync(() => PlacedControl(journey, "PaintBrokenControl1") is Forms.Label, "The paint failure was not shown as a placeholder.");
        Assert.Contains("Painting failed on purpose.", ((Forms.Label)PlacedControl(journey, "PaintBrokenControl1")!).Text, StringComparison.Ordinal);

        // The IDE is still working: a working control is placed beside the placeholders.
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "GaugeControl1") == "App.GaugeControl", "A working control could not be placed after the failures.");

        // Output receives each failure once, when the designer takes the build it was given.
        await BuildAsync(journey.Vm);
        await WaitUntilAsync(() => CountOutputLines(journey, "BrokenControl1", "Constructor failed on purpose.") == 1,
            "The constructor failure was not written to Output once.");
        Assert.Equal(1, CountOutputLines(journey, "PaintBrokenControl1", "Painting failed on purpose."));
    }

    /// <summary>
    /// A control that fails while it is placed reaches Output when the designer takes the placement, not
    /// only at the next build, so the user sees the reason without rebuilding.
    /// </summary>
    [AvaloniaFact]
    public async Task APlacementFailureReachesOutputBeforeTheNextBuild()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);

        await DoubleClickToolboxItemAsync(journey, "PaintBrokenControl");
        await WaitUntilAsync(() => PlacedControl(journey, "PaintBrokenControl1") is Forms.Label, "The paint failure was not shown as a placeholder.");

        // No build since the placement: the line is in Output already, and only once.
        await WaitUntilAsync(() => CountOutputLines(journey, "PaintBrokenControl1", "Painting failed on purpose.") == 1,
            "The paint failure of a placed control was not written to Output before the next build.");
    }

    /// <summary>A project control type, so the design-time sink records its failure as project code.</summary>
    private sealed class ProjectControl : Forms.Control { }

    /// <summary>
    /// A paint failure the designer records for a preview control reaches Output at the next flush, and an
    /// identical failure of the same control is written once, while a different failure is a new line.
    /// </summary>
    [AvaloniaFact]
    public async Task ADesignTimePaintFailureIsWrittenToOutputOnceForEachDistinctFailure()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        var session = Assert.Single(journey.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>()).DesignerSession;
        Assert.NotNull(session);

        var control = new ProjectControl { Name = "PaintBrokenControl1" };
        session.RecordPaintFailure(control, new InvalidOperationException("Paint failed at run time."));
        session.RecordPaintFailure(control, new InvalidOperationException("Paint failed at run time."));
        session.RecordPaintFailure(control, new InvalidOperationException("A different failure."));

        // The next flush is a placement; it writes what the sink recorded.
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "GaugeControl1") == "App.GaugeControl", "A working control could not be placed.");

        Assert.Equal(1, CountOutputLines(journey, "PaintBrokenControl1", "Paint failed at run time."));
        Assert.Equal(1, CountOutputLines(journey, "PaintBrokenControl1", "A different failure."));
    }

    /// <summary>
    /// A project control that paints once, when the designer creates it, and throws from its second paint on.
    /// The creation paint is the first; the designer paints the control again only when it is invalidated.
    /// </summary>
    private const string SecondPaintControlSource = """
        Public Class SecondPaintControl
            Inherits System.Windows.Forms.UserControl

            Private _paints As Integer

            Protected Overrides Sub OnPaint(e As System.Windows.Forms.PaintEventArgs)
                MyBase.OnPaint(e)
                _paints += 1
                If _paints > 1 Then Throw New System.InvalidOperationException("Second paint failed on purpose.")
                e.Graphics.Clear(System.Drawing.Color.Orange)
            End Sub
        End Class
        """;

    /// <summary>
    /// A project control whose second paint fails is drawn as the designer's error frame, the IDE keeps working,
    /// and Output receives the failure once, however often the control fails again.
    /// </summary>
    /// <remarks>
    /// The frame is read from the control's recorded picture, the one the surface composes. The surface's own
    /// pixels do not show the preview in this window (its creation paint is not visible there either), so the
    /// control is drawn the way the surface records it instead.
    /// Red-proof: the site assignment in WinFormsDesignerSession.CreateControl was removed once and this test
    /// failed; without the site the failing paint is not contained.
    /// </remarks>
    [AvaloniaFact]
    public async Task AProjectControlWhoseSecondPaintFailsShowsTheErrorFrameAndTheIdeKeepsWorking()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: false);
        File.WriteAllText(Path.Combine(journey.Project.Root, "SecondPaintControl.vb"), SecondPaintControlSource);
        await BuildAsync(journey.Vm);
        await WaitUntilAsync(() => ToolboxItems(journey).Any(item => item.DisplayName == "SecondPaintControl"),
            "The toolbox did not list the new control after the build.");

        await DoubleClickToolboxItemAsync(journey, "SecondPaintControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "SecondPaintControl1") == "App.SecondPaintControl",
            "SecondPaintControl1 was not placed.");

        // The creation paint drew the control, and the surface's paint after the placement is its second, which
        // throws. The control shows the error frame, the red the designer draws for it.
        await journey.Host.SettleAsync(2);
        Assert.True(RedPixels(PlacedControl(journey, "SecondPaintControl1")!) > 100,
            "The failing paint of the placed control was not drawn as the designer's error frame.");

        // Another edit still works: the preview is rebuilt and a working control is placed beside the other.
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "GaugeControl1") == "App.GaugeControl",
            "A working control could not be placed after the paint failure.");

        // The rebuilt control fails again on the surface, and the next edit still writes the failure once.
        await journey.Host.SettleAsync(2);
        Assert.True(RedPixels(PlacedControl(journey, "SecondPaintControl1")!) > 100,
            "The rebuilt control did not show the error frame after it failed again.");
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "GaugeControl2") == "App.GaugeControl",
            "A second working control could not be placed.");
        Assert.Equal(1, CountOutputLines(journey, "SecondPaintControl1", "Second paint failed on purpose."));
    }

    /// <summary>
    /// A project control with a built-in Button on it. The control wires the Button's Paint handler, which throws from the
    /// Button's second paint on. The Button has no site of its own, so its failure is reported through the control it is on.
    /// </summary>
    private const string ButtonHostSource = """
        Public Class ButtonHostControl
            Inherits System.Windows.Forms.UserControl

            Private ReadOnly _button As New System.Windows.Forms.Button()
            Private _paints As Integer

            Public Sub New()
                MyBase.New()
                _button.Name = "PressButton"
                _button.Text = "Press"
                AddHandler _button.Paint, AddressOf OnButtonPaint
                Controls.Add(_button)
            End Sub

            Public Property Caption As String
                Get
                    Return _button.Text
                End Get
                Set(value As String)
                    _button.Text = value
                End Set
            End Property

            Private Sub OnButtonPaint(sender As Object, e As System.Windows.Forms.PaintEventArgs)
                _paints += 1
                If _paints > 1 Then Throw New System.InvalidOperationException("Button paint failed on purpose.")
            End Sub
        End Class
        """;

    /// <summary>
    /// The failing paint of a built-in Button on a project control reaches Output, once, though the Button fails again on
    /// every rebuilt preview. The Button is not the project's code by itself, but its control is: RecordPaintFailure reports
    /// the failure through the nearest sited ancestor, so the built-in filter must not drop it.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the IsFormsType filter applied to the failing control's own type in RecordPaintFailure (the form
    /// before the fix), the Button was filtered out, and the wait for its line in Output timed out.
    /// </remarks>
    [AvaloniaFact]
    public async Task APaintFailureOfABuiltInButtonOnAProjectControlReachesOutputOnce()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: false);
        File.WriteAllText(Path.Combine(journey.Project.Root, "ButtonHostControl.vb"), ButtonHostSource);
        await BuildAsync(journey.Vm);
        await WaitUntilAsync(() => ToolboxItems(journey).Any(item => item.DisplayName == "ButtonHostControl"),
            "The toolbox did not list the new control after the build.");

        await DoubleClickToolboxItemAsync(journey, "ButtonHostControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "ButtonHostControl1") == "App.ButtonHostControl",
            "ButtonHostControl1 was not placed.");

        // A property change through the session rebuilds the preview. The new Button is painted when its control is created,
        // then its caption is set, which invalidates it, so the surface's paint of the form is the Button's second, and it throws.
        var session = DesignerView(journey).DesignerSession!;
        await session.SetPropertyAsync(PlacedControl(journey, "ButtonHostControl1")!, "Caption", "Changed");
        await journey.Host.SettleAsync(2);

        // The next flush is a placement; it writes the failure once.
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "GaugeControl1") == "App.GaugeControl", "A working control could not be placed.");
        await WaitUntilAsync(() => CountOutputLines(journey, "PressButton", "Button paint failed on purpose.") == 1,
            "The paint failure of the built-in Button was not written to Output.");

        // The Button fails again on the next rebuilt preview, and the same failure is not written a second time.
        await session.SetPropertyAsync(PlacedControl(journey, "ButtonHostControl1")!, "Caption", "Again");
        await journey.Host.SettleAsync(2);
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "GaugeControl2") == "App.GaugeControl", "A second working control could not be placed.");
        Assert.Equal(1, CountOutputLines(journey, "PressButton", "Button paint failed on purpose."));
    }

    /// <summary>The pixels of a control drawn the way the designer draws it, that are the error frame's red.</summary>
    private static int RedPixels(Forms.Control control)
    {
        using var bitmap = new System.Drawing.Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, control.Width, control.Height));

        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R >= 240 && pixel.G <= 15 && pixel.B <= 15) count++;
            }

        return count;
    }

    private static int CountOutputLines(Journey journey, string control, string message) =>
        journey.Vm.BuildOutput.Count(line => line.Contains(control, StringComparison.Ordinal)
            && line.Contains(message, StringComparison.Ordinal));

    // ------------------------------------------------------------------
    // A failed build keeps the controls of the last build that worked.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AFailedBuildKeepsTheControlsOfTheLastGoodBuild()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);

        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedControl(journey, "GaugeControl1") is not null, "GaugeControl1 was not placed.");

        File.WriteAllText(journey.Project.GaugePath, "Public Class GaugeControl\n    This does not compile\nEnd Class\n");
        Assert.False(await journey.Vm.BuildProjectForTestsAsync(), "A project that does not compile reported success.");
        await journey.Host.SettleAsync(2);

        Assert.Equal("App.GaugeControl", PlacedTypeName(journey, "GaugeControl1"));
        Assert.Equal(42, PlacedValue(journey, "GaugeControl1"));
        Assert.Contains(ToolboxItems(journey), item => item.DisplayName == "GaugeControl");
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // A build that finishes during an edit waits for it; a rebuild that cannot be placed keeps the context in use.
    // ------------------------------------------------------------------

    /// <summary>
    /// A build that finishes while an edit runs does not swap the context under the edit: the new context is applied
    /// once the edit has completed, and the edit's result is kept.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the busy deferral removed from OnProjectBuilt so the new context is applied at once, the
    /// Assert.Same on the context in use failed during the edit (expected context #1, actual #2).
    /// </remarks>
    [AvaloniaFact]
    public async Task ABuildFinishingDuringAnEditWaitsForTheEditToComplete()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        var view = DesignerView(journey);
        var before = view.CurrentDesignContextForTests;
        Assert.NotNull(before);

        // An edit that waits on a gate before it writes its result. The view is busy until the edit completes.
        var gate = new TaskCompletionSource();
        var edit = view.RunEditForTestsAsync(async () =>
        {
            await gate.Task;
            await view.DesignerSession!.SetPropertyAsync(view.DesignerSession.Form, "Text", "Written by the edit");
        });
        try
        {
            journey.Project.WriteGauge(7);
            await BuildAsync(journey.Vm);
            await WaitUntilAsync(() => view.CurrentDesignContextForTests != before || view.HasPendingDesignContextForTests,
                "The build's context was not loaded.");

            Assert.Same(before, view.CurrentDesignContextForTests);
        }
        finally
        {
            gate.TrySetResult();
        }

        await edit;
        await WaitUntilAsync(() => view.CurrentDesignContextForTests != before,
            "The build's context was not applied once the edit completed.");
        Assert.Equal("Written by the edit", journey.Surface.RootForm?.Text);
    }

    /// <summary>
    /// A rebuilt control that cannot be put on the form: the designer keeps the context and the controls it had,
    /// writes the reason to Output once, and still places controls.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the keep-previous restore removed from the catch in ApplyDesignContext, the Assert.Same on the
    /// context in use failed (expected #1, actual #2); with the whole catch removed, the parenting exception escaped the rebuild and failed the test.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARebuildThatCannotBePlacedKeepsTheContextTheDesignerHad()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedControl(journey, "GaugeControl1") is not null, "GaugeControl1 was not placed.");
        var view = DesignerView(journey);
        var before = view.CurrentDesignContextForTests;

        journey.Project.WriteGauge(7, failWhenParented: true);
        await BuildAsync(journey.Vm);

        await WaitUntilAsync(() => CountOutputLinesWith(journey, "Parenting failed on purpose.") == 1,
            "The failure of the rebuilt control was not written to Output once.");
        await journey.Host.SettleAsync(2);
        Assert.Same(before, view.CurrentDesignContextForTests);
        Assert.Equal("App.GaugeControl", PlacedTypeName(journey, "GaugeControl1"));
        Assert.Equal(42, PlacedValue(journey, "GaugeControl1"));

        // The designer still places controls, from the context it kept.
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "GaugeControl2") == "App.GaugeControl",
            "A control could not be placed after the rebuild failed.");
        Assert.Equal(42, PlacedValue(journey, "GaugeControl2"));
        Assert.Equal(1, CountOutputLinesWith(journey, "Parenting failed on purpose."));
    }

    /// <summary>
    /// A control that reads My.Computer is placed. Its My class derives from a Windows Forms base class in
    /// Microsoft.VisualBasic.Forms, which the project's output folder holds and the IDE does not ship, so the
    /// designer must read that copy rather than send the name to its own default context.
    /// </summary>
    /// <remarks>
    /// Red-proof: with Microsoft.VisualBasic.Forms added to the host names, ComputerControl1 was never placed as
    /// App.ComputerControl and the wait for it timed out.
    /// </remarks>
    [AvaloniaFact]
    public async Task AControlThatUsesMyIsPlacedFromTheVisualBasicFormsCopyInItsOutput()
    {
        RequireUnix();
        var project = WinFormsUserControlProject.Create();
        project.UseMyWithWindowsForms();
        using var journey = await OpenAsync(buildFirst: true, project);

        await DoubleClickToolboxItemAsync(journey, "ComputerControl");
        await WaitUntilAsync(() => PlacedTypeName(journey, "ComputerControl1") == "App.ComputerControl",
            "ComputerControl1 was not placed as the project's control.");
    }

    /// <summary>A project component is not listed: the designer cannot place one yet, so listing it would offer what it refuses.</summary>
    /// <remarks>
    /// Red-proof: with the toolbox described from System.ComponentModel.Component instead of Control, ProjectComponent
    /// is listed and Assert.DoesNotContain fails.
    /// </remarks>
    [AvaloniaFact]
    public async Task AProjectComponentIsNotListedInTheToolbox()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);

        var items = ToolboxItems(journey);
        Assert.Contains(items, item => item.DisplayName == "GaugeControl");
        Assert.DoesNotContain(items, item => item.DisplayName == "ProjectComponent");
    }

    /// <summary>
    /// The designer is shown again and its subscription replays the last build: the build is not loaded a second
    /// time, so the designer keeps the context it has.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the requested-build check removed from OnProjectBuilt, the replay loaded the output again and
    /// the Assert.Same on the context in use failed (expected #1, actual #2).
    /// </remarks>
    [AvaloniaFact]
    public async Task ReplayingTheLastBuildDoesNotLoadItAgain()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        var view = DesignerView(journey);
        var before = view.CurrentDesignContextForTests;
        Assert.NotNull(before);

        view.ResubscribeToBuildsForTests();
        for (var tick = 0; tick < 30; tick++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
        }

        Assert.Same(before, view.CurrentDesignContextForTests);
    }

    /// <summary>
    /// After the designer has rebuilt its preview on a new build, the context of the previous build is collected:
    /// the preview, the surface, the toolbox and the property panel hold nothing of it.
    /// </summary>
    /// <remarks>
    /// A guard rather than a regression test: it passed when it was written, so it has not been seen to fail.
    /// It fails if a designer holder keeps the previous context, such as a toolbox item, a selection or a descriptor.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheContextOfAReplacedBuildIsCollectedAfterTheDesignerUsesTheNewOne()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedControl(journey, "GaugeControl1") is not null, "GaugeControl1 was not placed.");
        var first = CurrentContextReference(journey);

        journey.Project.WriteGauge(7);
        await BuildAsync(journey.Vm);
        await WaitUntilAsync(() => PlacedValue(journey, "GaugeControl1") == 7, "The surface still shows the control of the earlier build.");

        Assert.True(await IsCollectedAsync(first), "The context of the replaced build is still alive after the designer took the new one.");
    }

    /// <summary>
    /// A second successful build whose output file did not change still loads. Only a referenced library changed, so
    /// the App.dll write time stayed the same; the build id, not the file, says that a new build succeeded.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the build key of the earlier design (output path and write time) restored in OnProjectBuilt,
    /// the second raise was skipped, and the wait for the second build to be applied failed ("The second build was
    /// not applied.").
    /// </remarks>
    [AvaloniaFact]
    public async Task ASecondBuildWhoseOutputFileIsUnchangedIsLoadedAgain()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        var view = DesignerView(journey);
        var (output, buildId) = LastBuild(journey);
        var before = view.CurrentDesignContextForTests;
        Assert.NotNull(before);

        journey.Vm.RaiseProjectBuiltForTests(journey.Project.ProjectPath, output, buildId + 1);

        await WaitUntilAsync(() => view.AppliedBuildIdForTests == buildId + 1, "The second build was not applied.");
        Assert.NotSame(before, view.CurrentDesignContextForTests);
    }

    /// <summary>
    /// A build whose swap failed is not recorded as applied, so the same build can be applied again once its output
    /// is good. The failed swap keeps the previous context, which is what the designer showed meanwhile.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the applied id recorded before the swap (ahead of the try in ApplyDesignContext), the failed
    /// build counted as applied: Assert.Equal on the applied id failed (expected 1, actual 2).
    /// </remarks>
    [AvaloniaFact]
    public async Task AFailedSwapIsNotRecordedSoTheSameBuildCanBeAppliedAgain()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        var view = DesignerView(journey);
        var (output, goodBuildId) = LastBuild(journey);
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedControl(journey, "GaugeControl1") is not null, "GaugeControl1 was not placed.");

        // A copy of the good output, to put back once the build that cannot be placed has been raised.
        var goodFolder = Directory.CreateTempSubdirectory("basalt-good-build-").FullName;
        try
        {
            var good = Path.Combine(goodFolder, Path.GetFileName(output));
            File.Copy(output, good);

            journey.Project.WriteGauge(7, failWhenParented: true);
            await BuildAsync(journey.Vm);
            await WaitUntilAsync(() => CountOutputLinesWith(journey, "Parenting failed on purpose.") == 1,
                "The failure of the rebuilt control was not written to Output.");
            Assert.Equal(goodBuildId, view.AppliedBuildIdForTests);
            var kept = view.CurrentDesignContextForTests;

            // The output is good again, and the same build is raised once more.
            File.Delete(output);
            File.Copy(good, output);
            journey.Vm.RaiseProjectBuiltForTests(journey.Project.ProjectPath, output, goodBuildId + 1);

            await WaitUntilAsync(() => view.CurrentDesignContextForTests != kept,
                "The build whose swap failed was not applied when it was raised again.");
            await WaitUntilAsync(() => view.AppliedBuildIdForTests == goodBuildId + 1, "The applied build id did not advance.");
            Assert.Equal(42, PlacedValue(journey, "GaugeControl1"));
        }
        finally
        {
            WindowsDesktopSamples.DeleteQuietly(goodFolder);
        }
    }

    /// <summary>
    /// A build that finishes while a modal property editor is open waits for the editor to close: the editor's result
    /// is written to the control the designer shows, and the new context is applied afterwards.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the open-modal count removed from IsBusyForDesignContext (only the edit holds the view busy), the
    /// build's context replaced the shown one while the editor was open, and Assert.Same failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task ABuildFinishingWhileAPropertyEditorIsOpenWaitsForTheEditorToClose()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        var view = DesignerView(journey);
        var before = view.CurrentDesignContextForTests;
        Assert.NotNull(before);

        var editor = view.HoldModalForTests();
        try
        {
            // The editor's result is written while it is open, to the control the designer shows.
            await view.RunEditForTestsAsync(() => view.DesignerSession!.SetPropertyAsync(view.DesignerSession.Form, "Text", "Set while the editor was open"));

            journey.Project.WriteGauge(7);
            await BuildAsync(journey.Vm);
            await WaitUntilAsync(() => view.HasPendingDesignContextForTests || view.CurrentDesignContextForTests != before,
                "The build's context was not loaded.");

            Assert.Same(before, view.CurrentDesignContextForTests);
            Assert.Equal("Set while the editor was open", journey.Surface.RootForm?.Text);
        }
        finally
        {
            editor.Dispose();
        }

        await WaitUntilAsync(() => view.CurrentDesignContextForTests != before,
            "The build's context was not applied once the editor closed.");
        Assert.Equal("Set while the editor was open", journey.Surface.RootForm?.Text);
    }

    /// <summary>
    /// The collection editor is a modal of the designer too: a build that finishes while it is open waits until it
    /// closes, as it does for a property editor. OpenCollectionEditor is called the way the property grid's Items
    /// ellipsis calls it, on a ListBox placed by a double-click on the Toolbox.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the BeginModal line removed from OpenCollectionEditor, the build's context was applied while the
    /// editor was open, and the Assert.Same on the context failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task ABuildFinishingWhileTheCollectionEditorIsOpenWaitsForTheEditorToClose()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        await DoubleClickToolboxItemAsync(journey, "ListBox");
        await WaitUntilAsync(() => PlacedControl(journey, "ListBox1") is not null, "ListBox1 was not placed.");

        InvokeDesignerMethod(journey, "OpenCollectionEditor", PlacedControl(journey, "ListBox1"), "Items");
        await WaitUntilAsync(() => journey.Host.Window.OwnedWindows.OfType<WinFormsCollectionEditor>().Any(),
            "The collection editor did not open.");

        await AssertBuildWaitsForModalAsync(journey,
            () => journey.Host.Window.OwnedWindows.OfType<WinFormsCollectionEditor>().Single().Close());
    }

    /// <summary>
    /// A menu item's property editor is a modal too: a build that finishes while its Font editor is open waits until the
    /// editor closes. The item is not placed in the designer. The editor only reads the item's Font, and it is closed
    /// without an edit, so a detached item is enough to open it the way the Properties grid does.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the BeginModal line removed from OpenMenuItemPropertyEditor, the build's context was applied while
    /// the Font editor was open, and the Assert.Same on the context failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task ABuildFinishingWhileAMenuItemFontEditorIsOpenWaitsForTheEditorToClose()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        var session = DesignerView(journey).DesignerSession!;
        var property = new Basalt.Designer.DesignableProperty("Font", typeof(System.Drawing.Font),
            Basalt.Designer.PropertyEditorKind.Text, Basalt.Designer.PropertyCategory.Appearance, [], null);

        InvokeDesignerMethod(journey, "OpenMenuItemPropertyEditor", session, session.Document, "MenuStrip1", "Open",
            new Forms.ToolStripMenuItem("Open"), property);
        await WaitUntilAsync(() => journey.Host.Window.OwnedWindows.OfType<WinFormsFontEditor>().Any(),
            "The menu item's Font editor did not open.");

        await AssertBuildWaitsForModalAsync(journey,
            () => journey.Host.Window.OwnedWindows.OfType<WinFormsFontEditor>().Single().Close());
    }

    /// <summary>
    /// Builds while a modal of the designer is open, and asserts that the swap of the design context waits for it: the
    /// context is queued, the controls stay, and it is applied once <paramref name="closeModal"/> has closed the modal.
    /// </summary>
    private static async Task AssertBuildWaitsForModalAsync(Journey journey, Action closeModal)
    {
        var view = DesignerView(journey);
        var before = view.CurrentDesignContextForTests;
        Assert.NotNull(before);

        journey.Project.WriteGauge(7);
        await BuildAsync(journey.Vm);
        await WaitUntilAsync(() => view.HasPendingDesignContextForTests || view.CurrentDesignContextForTests != before,
            "The build's context was not loaded.");
        Assert.Same(before, view.CurrentDesignContextForTests);

        closeModal();
        await WaitUntilAsync(() => view.CurrentDesignContextForTests != before,
            "The build's context was not applied once the modal closed.");
    }

    /// <summary>Calls a private method of the designer view the way its own code path does, for the modal tests.</summary>
    private static void InvokeDesignerMethod(Journey journey, string name, params object?[] arguments) =>
        typeof(WinFormsDesignerView).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(DesignerView(journey), arguments);

    /// <summary>
    /// Two rebuilds in a row that cannot be placed leave the form the surface shows as it was: the same form object,
    /// with its controls. The designer rebuilds nothing when a swap fails, so the surface is never handed a new form.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the previous preview rebuilt after a failed swap (the second Replace in UseDesignContext),
    /// the surface showed a new form after the first failure, and Assert.Same on the form failed.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARebuildThatFailsTwiceKeepsTheFormTheSurfaceShows()
    {
        RequireUnix();
        using var journey = await OpenAsync(buildFirst: true);
        await DoubleClickToolboxItemAsync(journey, "GaugeControl");
        await WaitUntilAsync(() => PlacedControl(journey, "GaugeControl1") is not null, "GaugeControl1 was not placed.");
        var form = journey.Surface.RootForm;
        Assert.NotNull(form);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            journey.Project.WriteGauge(7 + attempt, failWhenParented: true);
            await BuildAsync(journey.Vm);

            // Each build clears Output, so the failure of this attempt is the only line there.
            await WaitUntilAsync(() => CountOutputLinesWith(journey, "Parenting failed on purpose.") == 1,
                "The failure of rebuild " + (attempt + 1) + " was not written to Output.");
        }

        await journey.Host.SettleAsync(2);
        Assert.Same(form, journey.Surface.RootForm);
        Assert.Equal("App.GaugeControl", PlacedTypeName(journey, "GaugeControl1"));
        Assert.Equal(42, PlacedValue(journey, "GaugeControl1"));
    }

    private static (string Assembly, long BuildId) LastBuild(Journey journey)
    {
        (string Assembly, long BuildId)? last = null;

        // A subscription is handed the last successful build at once, under the id it had.
        using var subscription = journey.Vm.SubscribeToSuccessfulBuilds((_, assembly, buildId) => last = (assembly, buildId));
        return last ?? throw new InvalidOperationException("The project has no successful build.");
    }

    private static void RequireUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows builds the projects with the real WinForms.");
        WindowsDesktopSamples.RequireSources();
    }

    private static WinFormsDesignerView DesignerView(Journey journey) =>
        Assert.Single(journey.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());

    private static int CountOutputLinesWith(Journey journey, string message) =>
        journey.Vm.BuildOutput.Count(line => line.Contains(message, StringComparison.Ordinal));

    /// <summary>A reference to the context the designer is on. Not inlined, so no local of the caller keeps the context alive.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CurrentContextReference(Journey journey) =>
        new(DesignerView(journey).CurrentDesignContextForTests!);

    private static async Task<bool> IsCollectedAsync(WeakReference reference)
    {
        for (var attempt = 0; attempt < 30 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        return !reference.IsAlive;
    }

    private static async Task<Journey> OpenAsync(bool buildFirst, WinFormsUserControlProject? project = null)
    {
        project ??= WinFormsUserControlProject.Create();
        var host = new TestWindow(project.ProjectPath);
        try
        {
            var vm = (MainWindowViewModel)host.Window.DataContext!;

            // The window opens the project when it loads, in the background. A user cannot build
            // before the project has opened, so the journey waits for the same moment.
            await host.SettleAsync(10);
            // Opening goes through MSBuild, which on a loaded machine has been seen to take
            // longer than the 30 seconds every other step gets.
            await WaitUntilAsync(() => vm.StartupProject == project.ProjectPath && !vm.IsBusy,
                "The project did not open in the window.", seconds: 120);

            // Built before the designer opens for one journey, after it for the other: the designer
            // takes the output of a build it did not see as well as one it did.
            if (buildFirst) await BuildAsync(vm);
            await vm.OpenFileAsync(project.DesignerPath, inDesigner: true);
            await host.SettleAsync(10);

            var surface = Assert.Single(host.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            return new Journey { Host = host, Vm = vm, Surface = surface, Project = project };
        }
        catch
        {
            host.Dispose();
            project.Dispose();
            throw;
        }
    }

    private static async Task BuildAsync(MainWindowViewModel vm)
    {
        var built = await vm.BuildProjectForTestsAsync();
        Assert.True(built, "The project did not build (" + vm.StatusMessage + "): " + string.Join(Environment.NewLine, vm.BuildOutput));
    }

    private static WinFormsToolboxItem[] ToolboxItems(Journey journey) =>
        ToolboxItemsOf(Assert.Single(journey.Host.Window.GetVisualDescendants().OfType<ListBox>(),
            box => ToolboxItemsOf(box).Length > 0));

    private static WinFormsToolboxItem[] ToolboxItemsOf(ListBox box) =>
        box.ItemsSource?.Cast<object>().OfType<WinFormsToolboxItem>().ToArray() ?? [];

    /// <summary>
    /// A double-click on the Toolbox row, the way a user adds a control. A pause first, so the
    /// press is not taken for the second click of an earlier double-click.
    /// </summary>
    private static async Task DoubleClickToolboxItemAsync(Journey journey, string displayName)
    {
        await Task.Delay(700);
        var list = Assert.Single(journey.Host.Window.GetVisualDescendants().OfType<ListBox>(),
            box => ToolboxItemsOf(box).Any(item => item.DisplayName == displayName));

        // The list virtualises its rows: a control in a group below the fold has no row until it is
        // scrolled to, exactly as the user would scroll to it before double-clicking.
        var item = ToolboxItemsOf(list).Single(entry => entry.DisplayName == displayName);
        list.ScrollIntoView(item);
        journey.Host.Window.UpdateLayout();
        var row = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),
            candidate => ReferenceEquals(candidate.DataContext, item));
        row.BringIntoView();
        journey.Host.Window.UpdateLayout();
        var bounds = Raster.BoundsIn(row, journey.Host.Window);
        var at = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);

        journey.Host.Window.MouseDown(at, MouseButton.Left);
        journey.Host.Window.MouseUp(at, MouseButton.Left);
        journey.Host.Window.MouseDown(at, MouseButton.Left);
        journey.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    // The helpers below return values rather than the control itself, so no local of a test method
    // keeps an earlier control, and through it its design context, alive while a rebuild is awaited.

    private static Forms.Control? PlacedControl(Journey journey, string name) =>
        journey.Surface.RootForm?.Controls.Cast<Forms.Control>().SingleOrDefault(control => control.Name == name);

    private static string? PlacedTypeName(Journey journey, string name) => PlacedControl(journey, name)?.GetType().FullName;

    private static bool PlacedIsFormsControl(Journey journey, string name) => PlacedControl(journey, name) is Forms.Control;

    private static bool PlacedDerivesFromUserControl(Journey journey, string name) =>
        PlacedControl(journey, name)?.GetType().BaseType == typeof(Forms.UserControl);

    private static int? PlacedValue(Journey journey, string name)
    {
        var control = PlacedControl(journey, name);
        return control is null ? null : (int?)control.GetType().GetProperty("Value")!.GetValue(control);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string? what = null, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not reach the expected state in time" + (what is null ? "." : ": " + what));
    }
}
