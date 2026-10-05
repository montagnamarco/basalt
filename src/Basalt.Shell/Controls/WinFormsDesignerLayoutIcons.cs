using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>Small vector marks for layout actions, independent of display scaling.</summary>
internal static class WinFormsDesignerLayoutIcons
{
    internal static Control Create(WinFormsLayoutCommand command)
    {
        var icon = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };

        void Rule(double x1, double y1, double x2, double y2) => icon.Children.Add(new Line
        {
            StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2),
            Stroke = Brushes.SlateGray, StrokeThickness = 1.5,
        });

        void Bar(double x, double y, double width, double height)
        {
            var bar = new Rectangle
            {
                Width = width, Height = height, Fill = Brushes.DodgerBlue,
            };
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
            icon.Children.Add(bar);
        }

        switch (command)
        {
            case WinFormsLayoutCommand.AlignLeft:
                Rule(2, 2, 2, 18);
                Bar(4, 4, 13, 4);
                Bar(4, 12, 9, 4);
                break;
            case WinFormsLayoutCommand.AlignCenter:
                Rule(10, 2, 10, 18);
                Bar(3, 4, 14, 4);
                Bar(6, 12, 8, 4);
                break;
            case WinFormsLayoutCommand.AlignRight:
                Rule(18, 2, 18, 18);
                Bar(3, 4, 13, 4);
                Bar(7, 12, 9, 4);
                break;
            case WinFormsLayoutCommand.AlignTop:
                Rule(2, 2, 18, 2);
                Bar(4, 4, 4, 9);
                Bar(12, 4, 4, 13);
                break;
            case WinFormsLayoutCommand.AlignMiddle:
                Rule(2, 10, 18, 10);
                Bar(4, 5, 4, 10);
                Bar(12, 3, 4, 14);
                break;
            case WinFormsLayoutCommand.AlignBottom:
                Rule(2, 18, 18, 18);
                Bar(4, 9, 4, 7);
                Bar(12, 5, 4, 11);
                break;
            case WinFormsLayoutCommand.DistributeHorizontal:
                Rule(3, 10, 18, 10);
                Bar(2, 4, 3, 12);
                Bar(9, 4, 3, 12);
                Bar(16, 4, 3, 12);
                break;
            case WinFormsLayoutCommand.DistributeVertical:
                Rule(10, 3, 10, 18);
                Bar(4, 2, 12, 3);
                Bar(4, 9, 12, 3);
                Bar(4, 16, 12, 3);
                break;
        }

        return icon;
    }
}
