using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>Draws temporary alignment lines over the live form preview.</summary>
internal sealed class WinFormsDesignerGuides : Control
{
    private static readonly Pen GuidePen = new(new SolidColorBrush(Color.FromRgb(0, 122, 204)), 1);

    private Rect _client;
    private double? _vertical;
    private double? _horizontal;

    internal void Show(Rect client, double? vertical, double? horizontal)
    {
        _client = client;
        _vertical = vertical;
        _horizontal = horizontal;
        InvalidateVisual();
    }

    internal void Clear()
    {
        _vertical = null;
        _horizontal = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_client.Width <= 0 || _client.Height <= 0)
        {
            return;
        }

        using var clip = context.PushClip(_client);
        if (_vertical is { } x)
        {
            context.DrawLine(GuidePen, new Point(x + 0.5, _client.Top),
                new Point(x + 0.5, _client.Bottom));
        }

        if (_horizontal is { } y)
        {
            context.DrawLine(GuidePen, new Point(_client.Left, y + 0.5),
                new Point(_client.Right, y + 0.5));
        }
    }
}
