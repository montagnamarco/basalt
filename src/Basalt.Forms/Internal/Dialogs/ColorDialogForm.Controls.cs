using System.Drawing;

namespace System.Windows.Forms.Internal.Dialogs;

/// <summary>
/// The palette grid, the hue/saturation spectrum and the luminance bar
/// ColorDialogForm is built from, plus the HSL math that ties them to RGB.
/// Windows' own numeric ranges are kept (hue 0-239, saturation and luminance
/// 0-240) so the spin boxes read the way the classic dialog's do.
/// </summary>
internal partial class ColorDialogForm
{
    internal const int HueMax = 239;
    internal const int SatMax = 240;
    internal const int LumMax = 240;

    /// <summary>
    /// The 48 basic swatches: eight hues around the wheel at five bands of
    /// lightness, plus a bottom row running from black to white. Windows'
    /// own basic palette is not reproduced here (it is not published as data
    /// anyone can read back out of the dialog): this is our own palette.
    /// </summary>
    internal static Color[] BasicColors()
    {
        var lightnesses = new[] { 0.80, 0.65, 0.55, 0.45, 0.35 };
        var colors = new Color[48];
        var index = 0;

        foreach (var lightness in lightnesses)
        {
            for (var hue = 0; hue < 8; hue++)
            {
                colors[index++] = FromHsl(hue / 8.0, 1.0, lightness);
            }
        }

        for (var step = 0; step < 8; step++)
        {
            var level = (int)Math.Round(step * 255.0 / 7);
            colors[index++] = Color.FromArgb(level, level, level);
        }

        return colors;
    }

    /// <summary>Windows packs a custom colour as 0x00BBGGRR: blue in the high byte, red in the low one.</summary>
    internal static int ToWin32Bgr(Color color) => color.R | (color.G << 8) | (color.B << 16);

    internal static Color FromWin32Bgr(int value) => Color.FromArgb(value & 0xFF, (value >> 8) & 0xFF, (value >> 16) & 0xFF);

    /// <summary>Hue in turns (0..1), saturation and lightness in 0..1, to RGB: the standard HSL conversion.</summary>
    internal static Color FromHsl(double hue, double saturation, double lightness)
    {
        if (saturation <= 0) return Gray(lightness);

        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;

        var r = HueToChannel(p, q, hue + 1.0 / 3.0);
        var g = HueToChannel(p, q, hue);
        var b = HueToChannel(p, q, hue - 1.0 / 3.0);

        return Color.FromArgb(ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>Hue 0..239, saturation and lightness 0..240, Windows' own ranges, to RGB.</summary>
    internal static Color FromHsl(int hue, int saturation, int lightness) =>
        FromHsl(hue / (double)HueMax, saturation / (double)SatMax, lightness / (double)LumMax);

    /// <summary>The reverse of <see cref="FromHsl(int,int,int)"/>: the standard RGB to HSL conversion.</summary>
    internal static (int Hue, int Saturation, int Lightness) ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;

        double hue = 0;
        double saturation = 0;

        if (max != min)
        {
            var delta = max - min;
            saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);

            if (max == r) hue = ((g - b) / delta + (g < b ? 6 : 0)) / 6;
            else if (max == g) hue = ((b - r) / delta + 2) / 6;
            else hue = ((r - g) / delta + 4) / 6;
        }

        return (
            (int)Math.Round(hue * HueMax),
            (int)Math.Round(saturation * SatMax),
            (int)Math.Round(lightness * LumMax));
    }

    private static Color Gray(double lightness) => Color.FromArgb(ToByte(lightness), ToByte(lightness), ToByte(lightness));

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;

        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;

        return p;
    }

    private static int ToByte(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);

    /// <summary>
    /// A grid of colour swatches with a sunken border around each cell and a
    /// black frame around the selected one, clicked with the mouse: the shape
    /// both the basic and the custom palettes share.
    /// </summary>
    private sealed class SwatchGrid : Control
    {
        private const int CellSize = 18;
        private const int CellGap = 2;

        private readonly int _columns;
        private Color[] _colors;
        private int _selectedIndex = -1;

        public SwatchGrid(int columns, int rows, Color[] colors)
        {
            _columns = columns;
            _colors = colors;
            TabStop = false;
            Size = new Size(columns * (CellSize + CellGap) - CellGap, rows * (CellSize + CellGap) - CellGap);
        }

        public Color[] Colors
        {
            get => _colors;
            set
            {
                _colors = value;
                Invalidate();
            }
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value == _selectedIndex) return;

                _selectedIndex = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? SelectedIndexChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            for (var index = 0; index < _colors.Length; index++)
            {
                var bounds = CellBounds(index);

                using (var brush = new SolidBrush(_colors[index])) e.Graphics.FillRectangle(brush, bounds);
                ControlPaint.DrawBorder3D(e.Graphics, bounds, Border3DStyle.Sunken);

                if (index == _selectedIndex)
                {
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, 1, 1), Color.Black, SystemColors.Control);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Focus();
            var index = IndexFromPoint(e.Location);
            if (index >= 0) SelectedIndex = index;
        }

        private Rectangle CellBounds(int index)
        {
            var column = index % _columns;
            var row = index / _columns;
            return new Rectangle(column * (CellSize + CellGap), row * (CellSize + CellGap), CellSize, CellSize);
        }

        private int IndexFromPoint(Point point)
        {
            for (var index = 0; index < _colors.Length; index++)
            {
                if (CellBounds(index).Contains(point)) return index;
            }

            return -1;
        }
    }

    /// <summary>
    /// The hue/saturation square: hue runs left to right, saturation runs
    /// bottom to top, both at the luminance <see cref="Lightness"/> carries. A
    /// crosshair marks the point the user picked.
    /// </summary>
    private sealed class SpectrumPanel : Control
    {
        private int _hue;
        private int _saturation = SatMax;
        private int _lightness = 120;
        private bool _dragging;

        public SpectrumPanel()
        {
            TabStop = false;
            Size = new Size(136, 116);
        }

        public int Hue
        {
            get => _hue;
            set
            {
                if (value == _hue) return;
                _hue = value;
                Invalidate();
            }
        }

        public int Saturation
        {
            get => _saturation;
            set
            {
                if (value == _saturation) return;
                _saturation = value;
                Invalidate();
            }
        }

        /// <summary>The luminance the square is painted at; set from the luminance bar so the two never disagree.</summary>
        public int Lightness
        {
            get => _lightness;
            set
            {
                if (value == _lightness) return;
                _lightness = value;
                Invalidate();
            }
        }

        public event EventHandler? HueSaturationChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            var client = ClientRectangle;

            for (var y = 0; y < client.Height; y++)
            {
                var saturation = SatMax - y * SatMax / Math.Max(client.Height - 1, 1);

                for (var x = 0; x < client.Width; x++)
                {
                    var hue = x * HueMax / Math.Max(client.Width - 1, 1);
                    using var pen = new Pen(FromHsl(hue, saturation, _lightness));
                    e.Graphics.DrawLine(pen, x, y, x, y);
                }
            }

            ControlPaint.DrawBorder3D(e.Graphics, client, Border3DStyle.Sunken);

            var marker = PointFromValue(client);
            using var markerPen = new Pen(Color.Black);
            e.Graphics.DrawEllipse(markerPen, marker.X - 4, marker.Y - 4, 8, 8);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Focus();
            _dragging = true;
            Capture = true;
            SetFromPoint(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFromPoint(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        private void SetFromPoint(Point point)
        {
            var client = ClientRectangle;
            var x = Math.Clamp(point.X, 0, Math.Max(client.Width - 1, 0));
            var y = Math.Clamp(point.Y, 0, Math.Max(client.Height - 1, 0));

            _hue = x * HueMax / Math.Max(client.Width - 1, 1);
            _saturation = SatMax - y * SatMax / Math.Max(client.Height - 1, 1);

            Invalidate();
            HueSaturationChanged?.Invoke(this, EventArgs.Empty);
        }

        private Point PointFromValue(Rectangle client)
        {
            var x = _hue * Math.Max(client.Width - 1, 1) / HueMax;
            var y = Math.Max(client.Height - 1, 1) - _saturation * Math.Max(client.Height - 1, 1) / SatMax;
            return new Point(x, y);
        }
    }

    /// <summary>
    /// The vertical luminance bar beside the spectrum: black at the bottom,
    /// the current hue and saturation at full strength in the middle, white
    /// at the top, with a pointer on the side marking the chosen level.
    /// </summary>
    private sealed class LuminanceBar : Control
    {
        private int _hue;
        private int _saturation = SatMax;
        private int _lightness = 120;
        private bool _dragging;

        public LuminanceBar()
        {
            TabStop = false;
            Size = new Size(26, 116);
        }

        public int Hue
        {
            get => _hue;
            set
            {
                if (value == _hue) return;
                _hue = value;
                Invalidate();
            }
        }

        public int Saturation
        {
            get => _saturation;
            set
            {
                if (value == _saturation) return;
                _saturation = value;
                Invalidate();
            }
        }

        public int Lightness
        {
            get => _lightness;
            set
            {
                if (value == _lightness) return;
                _lightness = value;
                Invalidate();
            }
        }

        public event EventHandler? LightnessChanged;

        protected override void OnPaint(PaintEventArgs e)
        {
            var client = ClientRectangle;
            var barWidth = Math.Max(client.Width - 8, 1);

            for (var y = 0; y < client.Height; y++)
            {
                var lightness = LumMax - y * LumMax / Math.Max(client.Height - 1, 1);
                using var pen = new Pen(FromHsl(_hue, _saturation, lightness));
                e.Graphics.DrawLine(pen, 0, y, barWidth, y);
            }

            ControlPaint.DrawBorder3D(e.Graphics, new Rectangle(0, 0, barWidth, client.Height), Border3DStyle.Sunken);

            var markerY = Math.Max(client.Height - 1, 1) - _lightness * Math.Max(client.Height - 1, 1) / LumMax;
            using var markerPen = new Pen(Color.Black);
            e.Graphics.DrawLine(markerPen, barWidth, markerY, client.Width - 1, markerY);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Focus();
            _dragging = true;
            Capture = true;
            SetFromPoint(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetFromPoint(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        private void SetFromPoint(Point point)
        {
            var client = ClientRectangle;
            var y = Math.Clamp(point.Y, 0, Math.Max(client.Height - 1, 0));

            _lightness = LumMax - y * LumMax / Math.Max(client.Height - 1, 1);

            Invalidate();
            LightnessChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
