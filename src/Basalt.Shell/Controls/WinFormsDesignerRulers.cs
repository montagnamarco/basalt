using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>Compact pixel rulers in the surface's existing twelve-pixel gutter.</summary>
internal sealed class WinFormsDesignerRulers : Control
{
    private const double Gutter = 12;
    private const int MajorStep = 40;
    private const int MinorStep = 8;
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(243, 243, 243));
    private static readonly IBrush TickBrush = new SolidColorBrush(Color.FromRgb(86, 86, 86));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));

    private Size _clientSize;
    private bool _showRulers = true;

    internal Size ClientSize
    {
        get => _clientSize;
        set
        {
            _clientSize = value;
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
        if (!ShowRulers || _clientSize.Width <= 0 || _clientSize.Height <= 0)
        {
            return;
        }

        context.FillRectangle(BackgroundBrush, new Rect(0, 0, _clientSize.Width + Gutter * 2, Gutter));
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Gutter, _clientSize.Height + Gutter * 2));
        context.FillRectangle(BorderBrush, new Rect(0, Gutter - 1, _clientSize.Width + Gutter * 2, 1));
        context.FillRectangle(BorderBrush, new Rect(Gutter - 1, 0, 1, _clientSize.Height + Gutter * 2));

        using (context.PushClip(new Rect(0, 0, _clientSize.Width + Gutter * 2, Gutter)))
        {
            for (var x = 0; x <= _clientSize.Width; x += MinorStep)
            {
                var at = Gutter + x;
                var major = x % MajorStep == 0;
                context.FillRectangle(TickBrush, new Rect(at, major ? 8 : 10, 1, major ? 3 : 1));
                if (major && x > 0) DrawNumber(context, x, new Point(at + 2, 0));
            }
        }

        using (context.PushClip(new Rect(0, 0, Gutter, _clientSize.Height + Gutter * 2)))
        {
            for (var y = 0; y <= _clientSize.Height; y += MinorStep)
            {
                var at = Gutter + y;
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
