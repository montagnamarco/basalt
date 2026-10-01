using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Basalt.Drawing.Goldens;

/// <summary>
/// The drawings compared between Windows' GDI+ and Basalt.Drawing.
/// </summary>
/// <remarks>
/// This one file is compiled twice: into the generator, against Microsoft's
/// System.Drawing, and into the tests, against ours. The two APIs are the same,
/// so the same lines draw on both sides and only the result can differ. Each
/// scenario isolates one rule the code says has not been compared with
/// Windows: where pixels land, how edges are shaded, how text is measured.
/// </remarks>
public static class Scenarios
{
    public sealed record Scenario(string Name, int Width, int Height, Action<Graphics> Draw);

    private static readonly Color Ink = Color.FromArgb(255, 20, 40, 160);

    public static IReadOnlyList<Scenario> All { get; } =
    [
        // Where pixels land.
        new("rectangle-outline", 24, 24, g => g.DrawRectangle(Pens.Black, 2, 3, 10, 8)),
        new("rectangle-fill", 24, 24, g => g.FillRectangle(Brushes.Black, 2, 3, 10, 8)),
        new("rectangle-fill-fractional", 24, 24, g => g.FillRectangle(Brushes.Black, 2.3f, 3.3f, 10, 8)),
        new("rectangle-outline-wide", 32, 32, g => { using var pen = new Pen(Color.Black, 4); g.DrawRectangle(pen, 6, 6, 16, 12); }),
        new("line-horizontal", 24, 8, g => g.DrawLine(Pens.Black, 2, 3, 20, 3)),
        new("line-diagonal", 24, 24, g => g.DrawLine(Pens.Black, 2, 2, 20, 15)),
        new("line-ends", 24, 8, g => g.DrawLine(Pens.Black, 2, 3, 12, 3)),
        new("pixel-offset-half", 24, 24, g => { g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawRectangle(Pens.Black, 2, 3, 10, 8); g.FillRectangle(Brushes.Black, 15, 3, 5, 5); }),

        // Smoothing.
        new("ellipse-aliased", 40, 30, g => { g.DrawEllipse(Pens.Black, 4, 4, 30, 20); }),
        new("ellipse-smooth", 40, 30, g => { g.SmoothingMode = SmoothingMode.AntiAlias; using var pen = new Pen(Ink, 2); g.DrawEllipse(pen, 4, 4, 30, 20); }),
        new("ellipse-fill-smooth", 40, 30, g => { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillEllipse(Brushes.Black, 4.5f, 4.5f, 30, 20); }),

        // Arcs, pies and curves.
        new("arc-wide-45", 60, 40, g => g.DrawArc(Pens.Black, 5, 5, 50, 25, 0, 45)),
        new("pie-fill", 60, 40, g => g.FillPie(Brushes.Black, 5, 5, 50, 30, 30, 120)),
        new("polygon-fill", 40, 40, g => g.FillPolygon(Brushes.Black, [new PointF(5, 5), new PointF(35, 10), new PointF(20, 35)])),
        new("bezier", 60, 40, g => g.DrawBezier(Pens.Black, 5, 35, 15, 0, 45, 40, 55, 5)),
        new("curve", 60, 40, g => g.DrawCurve(Pens.Black, [new PointF(5, 30), new PointF(20, 5), new PointF(40, 35), new PointF(55, 10)])),

        // Pens.
        new("pen-dash", 60, 10, g => { using var pen = new Pen(Color.Black, 2) { DashStyle = DashStyle.Dash }; g.DrawLine(pen, 2, 5, 58, 5); }),
        new("pen-dashdot", 60, 10, g => { using var pen = new Pen(Color.Black, 2) { DashStyle = DashStyle.DashDot }; g.DrawLine(pen, 2, 5, 58, 5); }),
        new("pen-caps", 60, 30, g => { using var pen = new Pen(Color.Black, 8) { StartCap = LineCap.Round, EndCap = LineCap.Triangle }; g.DrawLine(pen, 10, 15, 50, 15); }),
        new("pen-anchors", 80, 30, g => { using var pen = new Pen(Color.Black, 3) { StartCap = LineCap.RoundAnchor, EndCap = LineCap.ArrowAnchor }; g.DrawLine(pen, 10, 15, 70, 15); }),
        new("pen-arrow-cap", 80, 30, g => { using var cap = new AdjustableArrowCap(4, 4); using var pen = new Pen(Color.Black, 2) { CustomEndCap = cap }; g.DrawLine(pen, 10, 15, 70, 15); }),
        new("pen-joins", 60, 40, g => { using var pen = new Pen(Color.Black, 6) { LineJoin = LineJoin.Bevel }; g.DrawLines(pen, [new PointF(5, 35), new PointF(30, 5), new PointF(55, 35)]); }),

        // Brushes.
        new("gradient-linear", 40, 4, g => { using var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(39, 0), Color.Red, Color.Blue); g.FillRectangle(brush, 0, 0, 40, 4); }),
        new("gradient-linear-mode", 40, 20, g => { using var brush = new LinearGradientBrush(new Rectangle(0, 0, 40, 20), Color.Red, Color.Blue, LinearGradientMode.ForwardDiagonal); g.FillRectangle(brush, 0, 0, 40, 20); }),
        new("gradient-path", 40, 40, g => { using var brush = new PathGradientBrush([new PointF(0, 0), new PointF(40, 0), new PointF(40, 40), new PointF(0, 40)]) { CenterColor = Color.White, SurroundColors = [Color.Black] }; g.FillRectangle(brush, 0, 0, 40, 40); }),
        new("gradient-path-defaults", 40, 40, g => { using var brush = new PathGradientBrush([new PointF(0, 0), new PointF(40, 0), new PointF(40, 40), new PointF(0, 40)]); g.FillRectangle(brush, 0, 0, 40, 40); }),
        .. Enum.GetValues<HatchStyle>().Distinct().Select(style => new Scenario(
            $"hatch-{(int)style:D2}", 16, 16, g => { using var brush = new HatchBrush(style, Color.Black, Color.White); g.FillRectangle(brush, 0, 0, 16, 16); })),

        // Clipping and transforms.
        new("clip-exclude", 30, 30, g => { g.ExcludeClip(new Rectangle(10, 10, 10, 10)); g.FillRectangle(Brushes.Black, 0, 0, 30, 30); }),
        new("clear-clipped", 30, 30, g => { g.SetClip(new Rectangle(5, 5, 10, 10)); g.Clear(Color.Black); }),
        new("transform-rotate", 40, 40, g => { g.TranslateTransform(20, 20); g.RotateTransform(30); g.FillRectangle(Brushes.Black, -10, -5, 20, 10); }),

        // Images.
        new("image-scaled-nearest", 12, 8, g => { using var image = Checker(); g.InterpolationMode = InterpolationMode.NearestNeighbor; g.DrawImage(image, new Rectangle(0, 0, 12, 8)); }),
        new("image-scaled-default", 12, 8, g => { using var image = Checker(); g.DrawImage(image, new Rectangle(0, 0, 12, 8)); }),
        new("image-unscaled", 8, 6, g => { using var image = Checker(); g.DrawImage(image, 1, 1); }),

        // Text.
        new("text-arial", 160, 30, g => { using var font = new Font("Arial", 10f); g.DrawString("Hamburgefontsiv", font, Brushes.Black, 2, 2); }),
        new("text-segoe", 160, 30, g => { using var font = new Font("Segoe UI", 9f); g.DrawString("Hamburgefontsiv", font, Brushes.Black, 2, 2); }),
        new("text-sans-serif", 160, 30, g => { using var font = new Font("Microsoft Sans Serif", 8.25f); g.DrawString("Hamburgefontsiv", font, Brushes.Black, 2, 2); }),
        new("text-wrap", 80, 80, g => { using var font = new Font("Arial", 10f); g.DrawString("The quick brown fox jumps over the lazy dog", font, Brushes.Black, new RectangleF(0, 0, 80, 80)); }),
        new("text-ellipsis", 80, 20, g => { using var font = new Font("Arial", 10f); using var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter }; g.DrawString("The quick brown fox jumps", font, Brushes.Black, new RectangleF(0, 0, 80, 20), format); }),
        new("text-center", 100, 30, g => { using var font = new Font("Arial", 10f); using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }; g.DrawString("Center", font, Brushes.Black, new RectangleF(0, 0, 100, 30), format); }),
        new("text-hotkey", 80, 20, g => { using var font = new Font("Arial", 10f); using var format = new StringFormat { HotkeyPrefix = HotkeyPrefix.Show }; g.DrawString("&File", font, Brushes.Black, 2, 2, format); }),
        new("text-tab", 120, 20, g => { using var font = new Font("Arial", 10f); g.DrawString("a\tb", font, Brushes.Black, 2, 2); }),
        new("text-aliased", 160, 30, g => { g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit; using var font = new Font("Arial", 10f); g.DrawString("Hamburgefontsiv", font, Brushes.Black, 2, 2); }),
    ];

    /// <summary>A 4×3 image of alternating black and white pixels.</summary>
    private static Bitmap Checker()
    {
        var image = new Bitmap(4, 3, PixelFormat.Format32bppArgb);

        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                image.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
            }
        }

        return image;
    }

    private static readonly string[] Families = ["Arial", "Times New Roman", "Courier New", "Tahoma", "Segoe UI", "Microsoft Sans Serif", "Verdana"];

    private static readonly float[] Sizes = [8.25f, 9f, 10f, 12f];

    private static readonly string[] Texts = ["Hamburgefontsiv", "OK", "Cancel", "The quick brown fox jumps over the lazy dog", "W i l l", "1234567890", "a\tb"];

    /// <summary>
    /// Font metrics and string sizes, the numbers a form's layout is built
    /// from: family design units, heights, and MeasureString with the default
    /// and the typographic format.
    /// </summary>
    public static Dictionary<string, object> Measure()
    {
        var result = new Dictionary<string, object>();
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);

        foreach (var name in Families)
        {
            using var family = new FontFamily(name);
            result[$"family/{name}"] = new Dictionary<string, object>
            {
                ["em"] = family.GetEmHeight(FontStyle.Regular),
                ["ascent"] = family.GetCellAscent(FontStyle.Regular),
                ["descent"] = family.GetCellDescent(FontStyle.Regular),
                ["lineSpacing"] = family.GetLineSpacing(FontStyle.Regular),
            };

            foreach (var size in Sizes)
            {
                using var font = new Font(name, size);
                result[$"font/{name}/{size}"] = new Dictionary<string, object>
                {
                    ["height"] = font.Height,
                    ["getHeight"] = font.GetHeight(),
                };

                foreach (var text in Texts)
                {
                    var standard = graphics.MeasureString(text, font);
                    var typographic = graphics.MeasureString(text, font, new PointF(0, 0), StringFormat.GenericTypographic);
                    result[$"measure/{name}/{size}/{text}"] = new[] { standard.Width, standard.Height, typographic.Width, typographic.Height };
                }
            }
        }

        return result;
    }
}
