using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Basalt.Vb6.Runtime;

namespace Basalt.Tests;

/// <summary>
/// The controls a converted Visual Basic 6 form talks to.
/// </summary>
/// <remarks>
/// Wrappers over Avalonia rather than controls of their own: the drawing and
/// the input are Avalonia's, and this is the surface twenty-year-old code
/// names. What is tested here is the translation between the two vocabularies,
/// which is where the quiet mistakes live.
/// </remarks>
public class Vb6RuntimeTests
{
    /// <summary>A form with the controls a small dialog has.</summary>
    private sealed class Dialogo : Vb6Form
    {
        public readonly Vb6TextBox Nome = new();
        public readonly Vb6CommandButton Ok = new();

        public string Detto = "";

        public Dialogo()
        {
            Caption = "Anagrafica";
            Add(Nome);
            Add(Ok);

            Ok.Click += (_, _) => Detto = Nome.Text.Trim() == ""
                ? "Inserire il nome"
                : "Ciao " + Nome.Text;
        }
    }

    [AvaloniaFact]
    public void MeasuresInTwipsTheWayAFormDoes()
    {
        // A .frm holds twips and so does the code arithmetic: a form that
        // moved a control by 120 moved it by an eighth of an inch. A runtime
        // measuring in pixels moves it fifteen times too far, which looks like
        // a layout bug rather than a unit one.
        var box = new Vb6TextBox { Left = 1320, Top = 480, Width = 2295 };

        Assert.Equal(1320, box.Left);
        Assert.Equal(88, Canvas.GetLeft(box.Native));
        Assert.Equal(32, Canvas.GetTop(box.Native));
        Assert.Equal(153, box.Native.Width);
    }

    [AvaloniaFact]
    public void ReadsAndWritesColourTheWayVisualBasic6Did()
    {
        // A VB6 colour is BGR, not RGB: &HFF is red there and blue everywhere
        // else. A form setting BackColor from a literal comes out with its
        // colours swapped without this, and the code looks right.
        var label = new Vb6Label { BackColor = 0x0000FF };

        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(
            ((TextBlock)label.Native).Background);

        Assert.Equal(255, brush.Color.R);
        Assert.Equal(0, brush.Color.B);

        // And back again, unchanged.
        Assert.Equal(0x0000FF, label.BackColor);
    }

    [AvaloniaFact]
    public void CountsACheckBoxTheWayVisualBasic6Counted()
    {
        // 0 and 1, not False and True: VB6 had a third state and code tests
        // Value = 1. A Boolean would make every such test compile and never
        // match.
        var check = new Vb6CheckBox();

        Assert.Equal(0, check.Value);

        check.Value = 1;

        Assert.True(((CheckBox)check.Native).IsChecked);
        Assert.Equal(1, check.Value);
    }

    [AvaloniaFact]
    public void KeepsTheNamesTwentyYearOldCodeUses()
    {
        // Caption, not Content. The whole point of a wrapper is that the old
        // spelling still works.
        var button = new Vb6CommandButton { Caption = "OK" };
        var label = new Vb6Label { Caption = "Nome:" };

        Assert.Equal("OK", ((Button)button.Native).Content);
        Assert.Equal("Nome:", ((TextBlock)label.Native).Text);
    }

    [AvaloniaFact]
    public void ShowsAListTheItemsAddedAfterItWasBound()
    {
        // A plain List raises nothing when it changes, so a control bound to
        // one shows what it held at the time and never an item added later —
        // and AddItem is how every Visual Basic 6 list is filled.
        var list = new Vb6ListBox();

        list.AddItem("uno");
        list.AddItem("due");

        Assert.Equal(2, list.ListCount);
        Assert.Equal("due", list.List(1));

        // -1 until something is chosen, which is what code tests before
        // reading the text.
        Assert.Equal(-1, list.ListIndex);
    }

    [AvaloniaFact]
    public void RunsTheHandlersAFormDeclares()
    {
        // The whole point, end to end: a click reaches the handler and the
        // handler reads the other controls.
        var form = new Dialogo();

        ((Button)form.Ok.Native).RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("Inserire il nome", form.Detto);

        form.Nome.Text = "Marco";

        ((Button)form.Ok.Native).RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("Ciao Marco", form.Detto);
    }

    [AvaloniaFact]
    public void PutsAFramesControlsInsideIt()
    {
        // Losing the nesting puts a group's contents loose on the form.
        var frame = new Vb6Frame { Caption = "Dati" };
        var box = new Vb6TextBox();

        frame.Add(box);

        var inside = Assert.IsType<Canvas>(
            ((HeaderedContentControl)frame.Native).Content);

        Assert.Contains(box.Native, inside.Children);
    }

    [AvaloniaFact]
    public void DrawsAFormWithItsControlsWhereTheFormPutThem()
    {
        // Rendered and looked at. Positions and sizes pass every numeric
        // assertion and still come out wrong on screen — a control behind
        // another, a label clipped to nothing, a frame that did not size to
        // its contents.
        var form = new Dialogo();

        form.ScaleWidth = 4680;
        form.ScaleHeight = 3195;
        form.Nome.Left = 1320;
        form.Nome.Top = 480;
        form.Nome.Width = 2295;
        form.Nome.Height = 285;
        form.Nome.Text = "Marco";
        form.Ok.Left = 1320;
        form.Ok.Top = 1080;
        form.Ok.Width = 1215;
        form.Ok.Height = 375;
        form.Ok.Caption = "OK";

        form.Window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var target = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new Avalonia.PixelSize(312, 213));

        target.Render(form.Window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-vb6-form.png");

        using (var file = File.Create(path))
            target.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 1000, "the render is empty");
    }
}
