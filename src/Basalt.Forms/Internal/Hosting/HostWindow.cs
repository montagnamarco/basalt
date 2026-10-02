using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// The native window of a form. Its client area is the form's client area,
/// one device-independent pixel for one WinForms pixel; the title bar and
/// frame are the system's.
/// </summary>
internal sealed class HostWindow : Avalonia.Controls.Window
{
    private readonly Form _form;

    /// <summary>Set while the form itself resizes the window, so the resize is not reported back to it.</summary>
    private bool _sizingFromForm;

    public HostWindow(Form form)
    {
        _form = form;
        Canvas = new FormsCanvas(form);

        // A template of our own: the window must show its canvas whatever theme,
        // if any, the hosting Avalonia application has.
        Template = new FuncControlTemplate<HostWindow>((window, scope) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!ContentPresenter.ContentProperty] = window[!ContentProperty],
        });

        Content = Canvas;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.Manual;

        Resized += (_, e) =>
        {
            if (!_sizingFromForm) _form.OnHostClientSizeChanged(new Drawing.Size((int)Math.Round(e.ClientSize.Width), (int)Math.Round(e.ClientSize.Height)));
        };

        PositionChanged += (_, e) => _form.OnHostPositionChanged(ToLogical(e.Point));
        Activated += (_, _) => _form.OnHostActivated();
        Deactivated += (_, _) => _form.OnHostDeactivated();
        Closing += (_, e) => e.Cancel = !_form.OnHostClosing(e.IsProgrammatic);
        ScalingChanged += (_, _) => _form.OnHostScalingChanged(DesktopScaling);
    }

    public FormsCanvas Canvas { get; }

    /// <summary>Sizes the window's client area to the form's.</summary>
    public void SetClientSize(Drawing.Size size)
    {
        _sizingFromForm = true;

        try
        {
            Width = Math.Max(size.Width, 0);
            Height = Math.Max(size.Height, 0);
        }
        finally
        {
            _sizingFromForm = false;
        }
    }

    /// <summary>Places the window's client area at the point, in WinForms screen coordinates.</summary>
    public void SetClientLocation(Drawing.Point location)
    {
        var scaling = DesktopScaling;
        Position = new PixelPoint((int)Math.Round(location.X * scaling), (int)Math.Round(location.Y * scaling));
    }

    private Drawing.Point ToLogical(PixelPoint point)
    {
        var scaling = DesktopScaling;

        return new Drawing.Point((int)Math.Round(point.X / scaling), (int)Math.Round(point.Y / scaling));
    }

    /// <summary>The window's icon, as the one Form.Icon paints in its own non-client frame when it has none of its own.</summary>
    public void SetIcon(Drawing.Icon? icon)
    {
        if (icon is null)
        {
            Icon = null;
            return;
        }

        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        Icon = new WindowIcon(stream);
    }
}
