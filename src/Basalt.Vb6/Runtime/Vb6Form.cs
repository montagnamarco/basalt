using Avalonia.Controls;
using Avalonia.Media;

namespace Basalt.Vb6.Runtime;

/// <summary>
/// A Visual Basic 6 form, hosted in an Avalonia window.
/// </summary>
/// <remarks>
/// The class a converted .frm inherits. Its controls go on a Canvas, because a
/// .frm positions everything absolutely and a layout panel would rearrange the
/// form — the one thing someone opening a twenty-year-old project does not
/// want.
///
/// The events are the ones the code handles by name: Load, Activate, Unload
/// and Resize account for nearly every Form_ procedure ever written.
/// </remarks>
public abstract class Vb6Form : Vb6Control
{
    private readonly Window _window;
    private readonly Canvas _canvas = new();

    protected Vb6Form()
    {
        _window = new Window
        {
            Content = _canvas,

            // What a Visual Basic 6 form looked like, since a .frm that names
            // no colour was this grey and code reads BackColor expecting it.
            Background = new SolidColorBrush(Color.FromRgb(0xD4, 0xD0, 0xC8)),
        };

        _window.Opened += (_, _) =>
        {
            Load?.Invoke(this, EventArgs.Empty);
            Activate?.Invoke(this, EventArgs.Empty);
        };

        _window.Closing += (_, _) => Unload?.Invoke(this, EventArgs.Empty);
        _window.SizeChanged += (_, _) => Resize?.Invoke(this, EventArgs.Empty);
    }

    public override Control Native => _window;

    /// <summary>The window this form is.</summary>
    public Window Window => _window;

    public string Caption
    {
        get => _window.Title ?? "";
        set => _window.Title = value;
    }

    /// <summary>
    /// The area inside the form, which is what a .frm measures.
    /// </summary>
    /// <remarks>
    /// ClientWidth and ClientHeight leave out the title bar and the border, so
    /// a form written 4680 wide is 4680 of usable space. Setting Width from
    /// them makes the form narrower than it was by the width of two borders.
    /// </remarks>
    public double ScaleWidth
    {
        // Read back from the window's own Width rather than from ClientSize:
        // ClientSize is what the platform measured, and before the window is
        // shown that is a default rather than what was asked for — a form set
        // to 4680 reported 15360 and looked like an arithmetic mistake.
        get => _window.Width * TwipsPerPixel;
        set => _window.Width = value / TwipsPerPixel;
    }

    public double ScaleHeight
    {
        get => _window.Height * TwipsPerPixel;
        set => _window.Height = value / TwipsPerPixel;
    }

    public event EventHandler? Load;
    public event EventHandler? Activate;
    public event EventHandler? Unload;
    public event EventHandler? Resize;

    /// <summary>Puts a control on the form.</summary>
    public void Add(Vb6Control control) => _canvas.Children.Add(control.Native);

    /// <summary>Shows the form.</summary>
    public void Show() => _window.Show();

    /// <summary>Hides it without closing it.</summary>
    public void Hide() => _window.Hide();

    /// <summary>Closes it.</summary>
    public void Unload_() => _window.Close();

    protected override IBrush? BackgroundOf() => _window.Background;

    protected override void SetBackground(IBrush brush) => _window.Background = brush;

    /// <summary>
    /// Raises Load without showing anything.
    /// </summary>
    /// <remarks>
    /// For tests and for a headless run: Form_Load is where a Visual Basic 6
    /// program does its setting up, and checking that it ran should not need a
    /// window on a screen.
    /// </remarks>
    internal void RaiseLoad() => Load?.Invoke(this, EventArgs.Empty);
}
