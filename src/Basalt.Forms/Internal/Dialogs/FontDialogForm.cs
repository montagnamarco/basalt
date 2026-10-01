using System.Drawing;

namespace System.Windows.Forms.Internal.Dialogs;

/// <summary>
/// The dialog behind FontDialog: family, style and size lists, the
/// strikeout and underline effects, an optional colour list, a sample box
/// and an optional Apply button, laid out the way the Windows 2000 font
/// picker was.
/// </summary>
/// <remarks>
/// The real dialog's "Size:" field is an editable combo and its "Color:"
/// field a plain combo; ComboBox does not exist in this codebase yet, so
/// both are approximated with a TextBox and a ListBox.
/// </remarks>
internal sealed class FontDialogForm : Form
{
    private const int Edge = 11;
    private const int Gap = 8;
    private const int ListHeight = 108;

    private static readonly int[] StandardSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    private static readonly (string Name, Color Value)[] NamedColors =
    [
        ("Black", Color.Black), ("Red", Color.Red), ("Green", Color.Green), ("Blue", Color.Blue),
        ("Yellow", Color.Yellow), ("Magenta", Color.Magenta), ("Cyan", Color.Cyan), ("White", Color.White),
        ("Gray", Color.Gray), ("Maroon", Color.Maroon), ("Olive", Color.Olive), ("Navy", Color.Navy),
        ("Purple", Color.Purple), ("Teal", Color.Teal), ("Silver", Color.Silver), ("Lime", Color.Lime),
    ];

    private readonly ListBox _familyList;
    private readonly ListBox _styleList;
    private readonly TextBox _sizeText;
    private readonly ListBox _sizeList;
    private readonly CheckBox _strikeoutCheck;
    private readonly CheckBox _underlineCheck;
    private readonly ListBox? _colorList;
    private readonly Label _sampleLabel;
    private readonly Button _okButton;
    private readonly Button? _applyButton;

    private readonly int _minSize;
    private readonly int _maxSize;

    private bool _syncingFields;
    private Font _currentFont;
    private Color _currentColor;

    public FontDialogForm(Font initialFont, Color initialColor, bool showEffects, bool showColor, bool showApply, bool showHelp, bool fixedPitchOnly, int minSize, int maxSize)
    {
        _currentFont = initialFont;
        _currentColor = initialColor;
        _minSize = minSize;
        _maxSize = maxSize;

        SuspendLayout();

        Text = "Font";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont ?? DefaultFont;

        // --- Font, style and size lists -------------------------------------

        var fontLabel = new Label { AutoSize = true, Text = "Font:", Location = new Point(Edge, Edge) };
        _familyList = new ListBox { Name = "FontFamily", Location = new Point(Edge, fontLabel.Bottom + 2), Size = new Size(150, ListHeight) };
        _familyList.Items.AddRange([.. FamilyNames(fixedPitchOnly)]);
        _familyList.SelectedIndexChanged += (_, _) => UpdateFont();

        var styleLabel = new Label { AutoSize = true, Text = "Font style:", Location = new Point(_familyList.Right + Gap, Edge) };
        _styleList = new ListBox { Name = "FontStyle", Location = new Point(_familyList.Right + Gap, fontLabel.Bottom + 2), Size = new Size(100, ListHeight) };
        _styleList.Items.AddRange(["Regular", "Italic", "Bold", "Bold Italic"]);
        _styleList.SelectedIndexChanged += (_, _) => UpdateFont();

        var sizeLabel = new Label { AutoSize = true, Text = "Size:", Location = new Point(_styleList.Right + Gap, Edge) };
        _sizeText = new TextBox { Name = "FontSize", Location = new Point(_styleList.Right + Gap, fontLabel.Bottom + 2), Width = 56 };
        _sizeText.TextChanged += (_, _) => UpdateFont();

        _sizeList = new ListBox { Name = "FontSizeList", Location = new Point(_styleList.Right + Gap, _sizeText.Bottom + 2), Size = new Size(56, ListHeight - _sizeText.Height - 2) };
        _sizeList.Items.AddRange([.. SizesInRange().Select(size => (object)size.ToString())]);
        _sizeList.SelectedIndexChanged += (_, _) =>
        {
            if (_sizeList.SelectedItem is string text) _sizeText.Text = text;
        };

        // --- Effects: strikeout, underline and the optional colour list -----

        var effectsTop = _familyList.Bottom + Gap;
        var effectsLabel = new Label { AutoSize = true, Text = "Effects", Location = new Point(Edge, effectsTop) };
        _strikeoutCheck = new CheckBox { AutoSize = true, Text = "Strikeout", Checked = initialFont.Strikeout, Location = new Point(Edge, effectsLabel.Bottom + 4) };
        _strikeoutCheck.CheckedChanged += (_, _) => UpdateFont();

        _underlineCheck = new CheckBox { AutoSize = true, Text = "Underline", Checked = initialFont.Underline, Location = new Point(Edge, _strikeoutCheck.Bottom + 4) };
        _underlineCheck.CheckedChanged += (_, _) => UpdateFont();

        Label? colorLabel = null;
        if (showColor)
        {
            colorLabel = new Label { AutoSize = true, Text = "Color:", Location = new Point(Edge, _underlineCheck.Bottom + 8) };
            _colorList = new ListBox { Name = "FontColor", Location = new Point(Edge, colorLabel.Bottom + 2), Size = new Size(120, 70) };
            _colorList.Items.AddRange([.. NamedColors.Select(named => (object)named.Name)]);
            _colorList.SelectedIndexChanged += (_, _) => UpdateFont();
        }

        // --- Sample box -------------------------------------------------------

        var sampleLabelCaption = new Label { AutoSize = true, Text = "Sample", Location = new Point(_familyList.Right + Gap, effectsTop) };
        var samplePanel = new Panel
        {
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(_familyList.Right + Gap, sampleLabelCaption.Bottom + 4),
            Size = new Size(_sizeList.Right - _familyList.Right - Gap, 46),
        };
        _sampleLabel = new Label { Name = "SampleText", AutoSize = false, Text = "AaBbYyZz", TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
        samplePanel.Controls.Add(_sampleLabel);

        // --- OK, Cancel, Apply and (optionally) Help, always at the bottom --

        _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(75, 23) };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(75, 23) };

        var buttons = new List<Button> { _okButton, cancelButton };
        if (showApply)
        {
            _applyButton = new Button { Text = "Apply", Size = new Size(75, 23) };
            _applyButton.Click += (_, _) => Applied?.Invoke(this, EventArgs.Empty);
            buttons.Add(_applyButton);
        }

        if (showHelp)
        {
            var helpButton = new Button { Text = "Help", Size = new Size(75, 23) };
            helpButton.Click += (_, _) => RaiseHelpRequested(new HelpEventArgs(MousePosition));
            buttons.Add(helpButton);
        }

        var clientWidth = _sizeList.Right + Edge;
        var buttonsTop = Math.Max(samplePanel.Bottom, _colorList?.Bottom ?? _underlineCheck.Bottom) + Gap;
        var buttonsWidth = buttons.Count * 75 + (buttons.Count - 1) * 6;
        var buttonsLeft = clientWidth - Edge - buttonsWidth;

        for (var i = 0; i < buttons.Count; i++) buttons[i].Location = new Point(buttonsLeft + i * (75 + 6), buttonsTop);

        Controls.Add(fontLabel);
        Controls.Add(_familyList);
        Controls.Add(styleLabel);
        Controls.Add(_styleList);
        Controls.Add(sizeLabel);
        Controls.Add(_sizeText);
        Controls.Add(_sizeList);
        Controls.Add(effectsLabel);
        Controls.Add(_strikeoutCheck);
        Controls.Add(_underlineCheck);
        if (colorLabel is not null) Controls.Add(colorLabel);
        if (_colorList is not null) Controls.Add(_colorList);
        Controls.Add(sampleLabelCaption);
        Controls.Add(samplePanel);
        Controls.AddRange([.. buttons]);

        ClientSize = new Size(clientWidth, buttonsTop + 23 + Edge);

        AcceptButton = _okButton;
        CancelButton = cancelButton;

        SelectInitialValues(initialFont, initialColor);
        _strikeoutCheck.Visible = showEffects;
        _underlineCheck.Visible = showEffects;

        ResumeLayout(false);
    }

    /// <summary>The font the user confirmed, with OK or Apply; meaningless after Cancel.</summary>
    public Font SelectedFont => _currentFont;

    /// <summary>The colour the user confirmed, with OK or Apply; meaningless after Cancel.</summary>
    public Color SelectedColor => _currentColor;

    /// <summary>Raised from the Apply button, without closing the dialog.</summary>
    public event EventHandler? Applied;

    private static IEnumerable<string> FamilyNames(bool fixedPitchOnly)
    {
        var families = FontFamily.Families.Select(family => family.Name).Distinct(StringComparer.OrdinalIgnoreCase);
        if (fixedPitchOnly) families = families.Where(name => IsFixedPitch(new FontFamily(name)));

        return families.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// A family has no "fixed pitch" flag of its own here, so this measures
    /// a narrow and a wide character: equal widths is what a monospaced face
    /// gives, by construction.
    /// </summary>
    private static bool IsFixedPitch(FontFamily family)
    {
        using var font = new Font(family, 100f, FontStyle.Regular, GraphicsUnit.Pixel);
        var narrow = TextRenderer.MeasureText("i", font).Width;
        var wide = TextRenderer.MeasureText("W", font).Width;

        return Math.Abs(narrow - wide) <= 1;
    }

    private IEnumerable<int> SizesInRange() =>
        StandardSizes.Where(size => (_minSize <= 0 || size >= _minSize) && (_maxSize <= 0 || size <= _maxSize));

    private void SelectInitialValues(Font initialFont, Color initialColor)
    {
        _syncingFields = true;
        try
        {
            var familyIndex = _familyList.Items.IndexOf(initialFont.Name);
            _familyList.SelectedIndex = familyIndex >= 0 ? familyIndex : -1;
            _styleList.SelectedIndex = StyleIndex(initialFont.Style);
            _sizeText.Text = initialFont.Size.ToString("0.##");

            if (_colorList is not null)
            {
                var colorIndex = Array.FindIndex(NamedColors, named => named.Value.ToArgb() == initialColor.ToArgb());
                _colorList.SelectedIndex = colorIndex >= 0 ? colorIndex : -1;
            }
        }
        finally
        {
            _syncingFields = false;
        }

        UpdateFont();
    }

    private static int StyleIndex(FontStyle style)
    {
        var bold = (style & FontStyle.Bold) != 0;
        var italic = (style & FontStyle.Italic) != 0;

        if (bold && italic) return 3;
        if (bold) return 2;
        if (italic) return 1;
        return 0;
    }

    private static FontStyle StyleFromIndex(int index) => index switch
    {
        1 => FontStyle.Italic,
        2 => FontStyle.Bold,
        3 => FontStyle.Bold | FontStyle.Italic,
        _ => FontStyle.Regular,
    };

    private void UpdateFont()
    {
        if (_syncingFields) return;

        var familyName = _familyList.SelectedItem as string ?? _currentFont.Name;
        var style = StyleFromIndex(_styleList.SelectedIndex)
            | (_strikeoutCheck.Checked ? FontStyle.Strikeout : 0)
            | (_underlineCheck.Checked ? FontStyle.Underline : 0);
        var size = ParseSize();

        _currentFont = new Font(familyName, size, style);

        if (_colorList is not null && _colorList.SelectedIndex >= 0) _currentColor = NamedColors[_colorList.SelectedIndex].Value;

        _sampleLabel.Font = _currentFont;
        _sampleLabel.ForeColor = _currentColor;
    }

    private float ParseSize()
    {
        if (!float.TryParse(_sizeText.Text, out var size) || size <= 0) return _currentFont.Size;

        if (_minSize > 0) size = Math.Max(size, _minSize);
        if (_maxSize > 0) size = Math.Min(size, _maxSize);

        return size;
    }
}
