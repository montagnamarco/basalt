using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Dialogs;

namespace System.Windows.Forms;

/// <summary>
/// The standard colour picker: 48 basic swatches, 16 custom slots the user
/// fills, and an optional hue/saturation/luminance editor behind "Define
/// Custom Colors >>", drawn from our own controls rather than the native
/// Windows CHOOSECOLOR dialog.
/// </summary>
[DefaultProperty("Color")]
public class ColorDialog : CommonDialog
{
    // The bit layout of Win32's CHOOSECOLOR.Flags: Options reports the same
    // numbers a native dialog would, even though nothing here reads them back.
    private const int CcRgbInit = 0x00000001;
    private const int CcFullOpen = 0x00000002;
    private const int CcPreventFullOpen = 0x00000004;
    private const int CcShowHelp = 0x00000008;
    private const int CcSolidColor = 0x00000080;
    private const int CcAnyColor = 0x00000100;

    private int[] _customColors = DefaultCustomColors();

    public ColorDialog()
    {
    }

    [DefaultValue(true)]
    public virtual bool AllowFullOpen { get; set; } = true;

    [DefaultValue(false)]
    public virtual bool AnyColor { get; set; }

    public Color Color { get; set; } = Color.Black;

    /// <summary>
    /// The sixteen custom swatches, each packed as Microsoft does: 0x00BBGGRR,
    /// blue in the high byte. A value with fewer than sixteen entries is
    /// padded with white; a longer one is truncated, as the native dialog's
    /// fixed-size array would be.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int[] CustomColors
    {
        get => (int[])_customColors.Clone();
        set
        {
            var copy = DefaultCustomColors();
            if (value is not null)
            {
                for (var i = 0; i < copy.Length && i < value.Length; i++) copy[i] = value[i];
            }

            _customColors = copy;
        }
    }

    [DefaultValue(false)]
    public virtual bool FullOpen { get; set; }

    /// <summary>
    /// Windows' HINSTANCE of the module that owns the dialog template; there
    /// is no native template here, so this is always a null handle.
    /// </summary>
    protected virtual IntPtr Instance => IntPtr.Zero;

    protected virtual int Options =>
        CcRgbInit
        | (FullOpen ? CcFullOpen : 0)
        | (!AllowFullOpen ? CcPreventFullOpen : 0)
        | (ShowHelp ? CcShowHelp : 0)
        | (SolidColorOnly ? CcSolidColor : 0)
        | (AnyColor ? CcAnyColor : 0);

    [DefaultValue(false)]
    public virtual bool ShowHelp { get; set; }

    [DefaultValue(false)]
    public virtual bool SolidColorOnly { get; set; }

    public override void Reset()
    {
        AllowFullOpen = true;
        AnyColor = false;
        Color = Color.Black;
        _customColors = DefaultCustomColors();
        FullOpen = false;
        ShowHelp = false;
        SolidColorOnly = false;
    }

    protected override bool RunDialog(IntPtr hwndOwner)
    {
        using var form = new ColorDialogForm(Color, _customColors, AllowFullOpen, FullOpen, ShowHelp);
        form.HelpRequested += (_, _) => OnHelpRequest(EventArgs.Empty);

        var result = form.ShowDialog(OwnerForm());
        if (result != DialogResult.OK) return false;

        Color = form.SelectedColor;
        _customColors = form.CustomColors;
        return true;
    }

    public override string ToString() => $"{base.ToString()}, Color: {Color}";

    /// <summary>
    /// The owner ShowDialog(IWin32Window) was given, found back from the host
    /// window ResolveOwnerHost already resolved: CommonDialog keeps the
    /// original owner private, but every open Form can be matched by its own
    /// host window.
    /// </summary>
    private Form? OwnerForm()
    {
        var host = ResolveOwnerHost();
        return host is null ? null : Application.OpenForms.Snapshot().FirstOrDefault(form => form.Host == host);
    }

    private static int[] DefaultCustomColors()
    {
        var colors = new int[16];
        Array.Fill(colors, 0x00FFFFFF);
        return colors;
    }
}
