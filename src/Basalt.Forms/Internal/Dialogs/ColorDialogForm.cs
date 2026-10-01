using System.Drawing;

namespace System.Windows.Forms.Internal.Dialogs;

/// <summary>
/// The dialog behind ColorDialog: 48 basic swatches and 16 custom ones always
/// shown, with "Define Custom Colors >>" expanding a hue/saturation square, a
/// luminance bar and the matching HSL/RGB spin boxes, laid out the way the
/// Windows 2000 colour picker was.
/// </summary>
internal sealed partial class ColorDialogForm : Form
{
    private const int Edge = 11;
    private const int Gap = 8;

    private readonly Color[] _basicColors = BasicColors();
    private readonly int[] _customColorsBgr;
    private readonly List<Label> _fieldLabels = [];

    private readonly SwatchGrid _basicGrid;
    private readonly SwatchGrid _customGrid;
    private readonly Button _defineButton;
    private readonly Button _okButton;

    private readonly Control[] _expandedControls;
    private readonly SpectrumPanel _spectrum;
    private readonly LuminanceBar _luminanceBar;
    private readonly Panel _previewPanel;
    private readonly NumericUpDown _hueUpDown;
    private readonly NumericUpDown _satUpDown;
    private readonly NumericUpDown _lumUpDown;
    private readonly NumericUpDown _redUpDown;
    private readonly NumericUpDown _greenUpDown;
    private readonly NumericUpDown _blueUpDown;
    private readonly Button _addCustomButton;

    private readonly Size _collapsedSize;
    private readonly Size _expandedSize;

    private bool _expanded;
    private bool _syncingFields;
    private int _nextCustomSlot;
    private Color _currentColor;

    public ColorDialogForm(Color initialColor, int[] customColorsBgr, bool allowFullOpen, bool fullOpen, bool showHelp)
    {
        _customColorsBgr = (int[])customColorsBgr.Clone();
        _currentColor = initialColor;

        SuspendLayout();

        Text = "Color";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont ?? DefaultFont;

        // --- Left side: the two palettes, always visible -------------------

        var basicLabel = new Label { AutoSize = true, Text = "Basic colors:", Location = new Point(Edge, Edge) };
        _basicGrid = new SwatchGrid(8, 6, _basicColors) { Name = "BasicColors", Location = new Point(Edge, basicLabel.Bottom + 4) };
        _basicGrid.SelectedIndexChanged += OnBasicSelected;

        var customLabel = new Label { AutoSize = true, Text = "Custom colors:", Location = new Point(Edge, _basicGrid.Bottom + Gap) };
        _customGrid = new SwatchGrid(8, 2, CustomColorsAsColors()) { Name = "CustomColors", Location = new Point(Edge, customLabel.Bottom + 4) };
        _customGrid.SelectedIndexChanged += OnCustomSelected;

        _defineButton = new Button
        {
            Name = "DefineCustomColors",
            Text = "Define Custom Colors >>",
            Location = new Point(Edge, _customGrid.Bottom + Gap),
            Size = new Size(_basicGrid.Width, 23),
            // CC_PREVENTFULLOPEN greys the button out rather than removing it.
            Enabled = allowFullOpen,
        };
        _defineButton.Click += (_, _) => ToggleExpanded();

        // --- Bottom row: OK, Cancel and (optionally) Help, always visible --

        _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(75, 23) };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(75, 23) };

        var buttons = new List<Button> { _okButton, cancelButton };
        if (showHelp)
        {
            var helpButton = new Button { Text = "Help", Size = new Size(75, 23) };
            helpButton.Click += (_, _) => RaiseHelpRequested(new HelpEventArgs(MousePosition));
            buttons.Add(helpButton);
        }

        var leftColumnWidth = _basicGrid.Width + 2 * Edge;
        var buttonsTop = _defineButton.Bottom + Gap;

        // --- Right side: the expanded hue/saturation/luminance editor ------

        var rightLeft = leftColumnWidth + Gap;

        _spectrum = new SpectrumPanel { Name = "Spectrum", Location = new Point(rightLeft, Edge) };
        _spectrum.HueSaturationChanged += OnSpectrumChanged;

        _luminanceBar = new LuminanceBar { Name = "LuminanceBar", Location = new Point(_spectrum.Right + Gap, Edge) };
        _luminanceBar.LightnessChanged += OnLuminanceChanged;

        var editorTop = _spectrum.Bottom + Gap;

        _previewPanel = new Panel
        {
            Name = "ColorPreview",
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(rightLeft, editorTop),
            Size = new Size(50, 58),
        };

        var fieldsLeft = _previewPanel.Right + Gap;
        _hueUpDown = NumericField("Hue", "Hue:", fieldsLeft, editorTop, HueMax);
        _satUpDown = NumericField("Saturation", "Sat:", fieldsLeft, editorTop + 26, SatMax);
        _lumUpDown = NumericField("Luminance", "Lum:", fieldsLeft, editorTop + 52, LumMax);

        var secondColumnLeft = fieldsLeft + 70;
        _redUpDown = NumericField("Red", "Red:", secondColumnLeft, editorTop, 255);
        _greenUpDown = NumericField("Green", "Green:", secondColumnLeft, editorTop + 26, 255);
        _blueUpDown = NumericField("Blue", "Blue:", secondColumnLeft, editorTop + 52, 255);

        _hueUpDown.ValueChanged += OnHslFieldChanged;
        _satUpDown.ValueChanged += OnHslFieldChanged;
        _lumUpDown.ValueChanged += OnHslFieldChanged;
        _redUpDown.ValueChanged += OnRgbFieldChanged;
        _greenUpDown.ValueChanged += OnRgbFieldChanged;
        _blueUpDown.ValueChanged += OnRgbFieldChanged;

        _addCustomButton = new Button
        {
            Name = "AddToCustomColors",
            Text = "Add to Custom Colors",
            Location = new Point(rightLeft, editorTop + 58 + Gap),
            Size = new Size(secondColumnLeft + 70 - rightLeft, 23),
        };
        _addCustomButton.Click += OnAddCustomColor;

        _expandedControls = [_spectrum, _luminanceBar, _previewPanel, _hueUpDown, _satUpDown, _lumUpDown, _redUpDown, _greenUpDown, _blueUpDown, _addCustomButton];
        foreach (var label in _fieldLabels) Controls.Add(label);
        Controls.AddRange(_expandedControls);

        Controls.Add(basicLabel);
        Controls.Add(_basicGrid);
        Controls.Add(customLabel);
        Controls.Add(_customGrid);
        Controls.Add(_defineButton);
        Controls.AddRange([.. buttons]);

        var rightColumnWidth = Math.Max(_luminanceBar.Right, _addCustomButton.Right) + Edge - rightLeft;
        _collapsedSize = new Size(leftColumnWidth, buttonsTop + 23 + Edge);
        _expandedSize = new Size(leftColumnWidth + Gap + rightColumnWidth, Math.Max(_collapsedSize.Height, _addCustomButton.Bottom + Edge));

        var buttonsLeft = _collapsedSize.Width - Edge - buttons.Count * 75 - (buttons.Count - 1) * 6;
        for (var i = 0; i < buttons.Count; i++) buttons[i].Location = new Point(buttonsLeft + i * (75 + 6), buttonsTop);

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        _expanded = fullOpen && allowFullOpen;
        ApplyExpanded();

        SelectColor(initialColor);

        ResumeLayout(false);
    }

    /// <summary>The colour the user confirmed with OK; meaningless after Cancel.</summary>
    public Color SelectedColor => _currentColor;

    /// <summary>The sixteen custom slots, packed as Microsoft does: 0x00BBGGRR.</summary>
    public int[] CustomColors => (int[])_customColorsBgr.Clone();

    private NumericUpDown NumericField(string name, string caption, int left, int top, int maximum)
    {
        var label = new Label { AutoSize = true, Text = caption, Location = new Point(left, top + 3) };
        _fieldLabels.Add(label);

        return new NumericUpDown
        {
            Name = name,
            Minimum = 0,
            Maximum = maximum,
            Location = new Point(left + 38, top),
            Width = 50,
        };
    }

    private Color[] CustomColorsAsColors() => [.. _customColorsBgr.Select(FromWin32Bgr)];

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        ApplyExpanded();
    }

    private void ApplyExpanded()
    {
        foreach (var control in _expandedControls) control.Visible = _expanded;
        _defineButton.Text = _expanded ? "Define Custom Colors <<" : "Define Custom Colors >>";
        ClientSize = _expanded ? _expandedSize : _collapsedSize;
    }

    private void SelectColor(Color color)
    {
        _currentColor = color;
        var (hue, saturation, lightness) = ToHsl(color);

        _syncingFields = true;
        try
        {
            _hueUpDown.Value = hue;
            _satUpDown.Value = saturation;
            _lumUpDown.Value = lightness;
            _redUpDown.Value = color.R;
            _greenUpDown.Value = color.G;
            _blueUpDown.Value = color.B;

            _spectrum.Hue = hue;
            _spectrum.Saturation = saturation;
            _spectrum.Lightness = lightness;
            _luminanceBar.Hue = hue;
            _luminanceBar.Saturation = saturation;
            _luminanceBar.Lightness = lightness;

            _previewPanel.BackColor = color;
        }
        finally
        {
            _syncingFields = false;
        }
    }

    private void OnBasicSelected(object? sender, EventArgs e)
    {
        if (_basicGrid.SelectedIndex < 0) return;

        _customGrid.SelectedIndex = -1;
        SelectColor(_basicColors[_basicGrid.SelectedIndex]);
    }

    private void OnCustomSelected(object? sender, EventArgs e)
    {
        if (_customGrid.SelectedIndex < 0) return;

        _basicGrid.SelectedIndex = -1;
        SelectColor(FromWin32Bgr(_customColorsBgr[_customGrid.SelectedIndex]));
    }

    private void OnSpectrumChanged(object? sender, EventArgs e)
    {
        if (_syncingFields) return;

        _luminanceBar.Hue = _spectrum.Hue;
        _luminanceBar.Saturation = _spectrum.Saturation;
        _basicGrid.SelectedIndex = -1;
        _customGrid.SelectedIndex = -1;

        SelectColor(FromHsl(_spectrum.Hue, _spectrum.Saturation, _luminanceBar.Lightness));
    }

    private void OnLuminanceChanged(object? sender, EventArgs e)
    {
        if (_syncingFields) return;

        _spectrum.Lightness = _luminanceBar.Lightness;
        _basicGrid.SelectedIndex = -1;
        _customGrid.SelectedIndex = -1;

        SelectColor(FromHsl(_spectrum.Hue, _spectrum.Saturation, _luminanceBar.Lightness));
    }

    private void OnHslFieldChanged(object? sender, EventArgs e)
    {
        if (_syncingFields) return;

        _basicGrid.SelectedIndex = -1;
        _customGrid.SelectedIndex = -1;

        SelectColor(FromHsl((int)_hueUpDown.Value, (int)_satUpDown.Value, (int)_lumUpDown.Value));
    }

    private void OnRgbFieldChanged(object? sender, EventArgs e)
    {
        if (_syncingFields) return;

        _basicGrid.SelectedIndex = -1;
        _customGrid.SelectedIndex = -1;

        SelectColor(Color.FromArgb((int)_redUpDown.Value, (int)_greenUpDown.Value, (int)_blueUpDown.Value));
    }

    private void OnAddCustomColor(object? sender, EventArgs e)
    {
        var targetIndex = _customGrid.SelectedIndex >= 0 ? _customGrid.SelectedIndex : _nextCustomSlot;

        _customColorsBgr[targetIndex] = ToWin32Bgr(_currentColor);
        _customGrid.Colors = CustomColorsAsColors();
        _customGrid.SelectedIndex = targetIndex;

        _nextCustomSlot = (targetIndex + 1) % _customColorsBgr.Length;
    }
}
