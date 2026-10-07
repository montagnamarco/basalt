using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormsControl = System.Windows.Forms.Control;

namespace Basalt.Shell.Controls;

/// <summary>
/// Compact pixel rulers in the surface's existing twelve-pixel gutter, their
/// zero tick aligned to the root form's client origin rather than to the
/// gutter alone: once the design surface started drawing the form's own
/// border and caption (see Form.DesignerFrame.cs), the dotted client area -
/// and so the grid's dots - moved past the gutter by the form's
/// <see cref="FormsControl.NonClient"/> frame, the same offset
/// <see cref="WinFormsDesignerGrid"/> already applies to its own dots.
/// </summary>
internal sealed class WinFormsDesignerRulers : Control
{
    private const double Gutter = 12;
    private const int MajorStep = 40;
    private const int MinorStep = 8;
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(243, 243, 243));
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.FromRgb(86, 86, 86));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));

    private FormsControl? _rootForm;
    private bool _showRulers = true;

    internal FormsControl? RootForm
    {
        get => _rootForm;
        set
        {
            _rootForm = value;
            InvalidateVisual();
        }
    }

    internal bool ShowRulers
    {
        get => _showRulers;
        set
        {
            if (_showRulers == value) return;
            _showRulers = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!ShowRulers || _rootForm is null) return;

        var clientSize = _rootForm.ClientSize;
        if (clientSize.Width <= 0 || clientSize.Height <= 0) return;

        // The same frame offset the grid's dots use (WinFormsDesignerGrid.Render):
        // the gutter alone no longer reaches the client area once a caption or
        // border is drawn there.
        var nonClient = _rootForm.NonClient;
        var originX = Gutter + nonClient.Left;
        var originY = Gutter + nonClient.Top;

        context.FillRectangle(BackgroundBrush, new Rect(0, 0, originX + clientSize.Width + Gutter, Gutter));
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Gutter, originY + clientSize.Height + Gutter));
        context.FillRectangle(BorderBrush, new Rect(0, Gutter - 1, originX + clientSize.Width + Gutter, 1));
        context.FillRectangle(BorderBrush, new Rect(Gutter - 1, 0, 1, originY + clientSize.Height + Gutter));

        using (context.PushClip(new Rect(0, 0, originX + clientSize.Width + Gutter, Gutter)))
        {
            for (var x = 0; x <= clientSize.Width; x += MinorStep)
            {
                var at = originX + x;
                var major = x % MajorStep == 0;
                context.FillRectangle(TickBrush, new Rect(at, major ? 8 : 10, 1, major ? 3 : 1));
                if (major && x > 0) DrawNumber(context, x, new Point(at + 2, 0));
            }
        }

        using (context.PushClip(new Rect(0, 0, Gutter, originY + clientSize.Height + Gutter)))
        {
            for (var y = 0; y <= clientSize.Height; y += MinorStep)
            {
                var at = originY + y;
                var major = y % MajorStep == 0;
                context.FillRectangle(TickBrush, new Rect(major ? 8 : 10, at, major ? 3 : 1, 1));
                if (major && y > 0) DrawNumber(context, y, new Point(0, at + 1));
            }
        }
    }

    private static void DrawNumber(DrawingContext context, int value, Point origin)
    {
        var number = new FormattedText(value.ToString(CultureInfo.CurrentCulture),
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 7, TickBrush);
        context.DrawText(number, origin);
    }
}
