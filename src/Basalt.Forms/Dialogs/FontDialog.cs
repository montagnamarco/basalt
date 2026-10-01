using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Dialogs;

namespace System.Windows.Forms;

/// <summary>
/// The standard font picker: family, style and size lists, the strikeout and
/// underline effects, an optional colour list and an Apply button, drawn from
/// our own controls rather than the native Windows CHOOSEFONT dialog.
/// </summary>
[DefaultEvent("Apply")]
[DefaultProperty("Font")]
public class FontDialog : CommonDialog
{
    protected static readonly object EventApply = new();

    // The bit layout of Win32's CHOOSEFONT.Flags: Options reports the same
    // numbers a native dialog would, even though nothing here reads them back.
    private const int CfScreenFonts = 0x00000001;
    private const int CfEffects = 0x00000100;
    private const int CfApply = 0x00000200;
    private const int CfInitToLogFontStruct = 0x00000040;
    private const int CfLimitSize = 0x00002000;
    private const int CfFixedPitchOnly = 0x00004000;
    private const int CfForceFontExist = 0x00010000;
    private const int CfScriptsOnly = 0x00400000;
    private const int CfNoScriptSel = 0x00800000;
    private const int CfNoVerticalFonts = 0x01000000;
    private const int CfNoVectorFonts = 0x00000800;
    private const int CfNoSimulations = 0x00001000;

    public FontDialog()
    {
    }

    [DefaultValue(true)]
    public bool AllowScriptChange { get; set; } = true;

    [DefaultValue(true)]
    public bool AllowSimulations { get; set; } = true;

    [DefaultValue(true)]
    public bool AllowVectorFonts { get; set; } = true;

    [DefaultValue(true)]
    public bool AllowVerticalFonts { get; set; } = true;

    public Color Color { get; set; } = Color.Black;

    [DefaultValue(false)]
    public bool FixedPitchOnly { get; set; }

    public Font Font { get; set; } = SystemFonts.DefaultFont;

    [DefaultValue(false)]
    public bool FontMustExist { get; set; }

    [DefaultValue(0)]
    public int MaxSize { get; set; }

    [DefaultValue(0)]
    public int MinSize { get; set; }

    protected int Options =>
        CfScreenFonts
        | CfInitToLogFontStruct
        | (ShowEffects ? CfEffects : 0)
        | (ShowApply ? CfApply : 0)
        | (ShowHelp ? 0x00000004 : 0)
        | (ScriptsOnly ? CfScriptsOnly : 0)
        | (!AllowScriptChange ? CfNoScriptSel : 0)
        | (!AllowVerticalFonts ? CfNoVerticalFonts : 0)
        | (!AllowVectorFonts ? CfNoVectorFonts : 0)
        | (!AllowSimulations ? CfNoSimulations : 0)
        | (FixedPitchOnly ? CfFixedPitchOnly : 0)
        | (FontMustExist ? CfForceFontExist : 0)
        | ((MinSize > 0 || MaxSize > 0) ? CfLimitSize : 0);

    [DefaultValue(false)]
    public bool ScriptsOnly { get; set; }

    [DefaultValue(false)]
    public bool ShowApply { get; set; }

    [DefaultValue(false)]
    public bool ShowColor { get; set; }

    [DefaultValue(true)]
    public bool ShowEffects { get; set; } = true;

    [DefaultValue(false)]
    public bool ShowHelp { get; set; }

    public event EventHandler? Apply;

    /// <summary>
    /// Windows subclasses the native dialog's window procedure to intercept
    /// its messages; there is no such window here, so this hook is never
    /// called, exactly as the base it overrides documents.
    /// </summary>
    protected override IntPtr HookProc(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam) =>
        base.HookProc(hWnd, msg, wparam, lparam);

    protected virtual void OnApply(EventArgs e) => Apply?.Invoke(this, e);

    public override void Reset()
    {
        AllowScriptChange = true;
        AllowSimulations = true;
        AllowVectorFonts = true;
        AllowVerticalFonts = true;
        Color = Color.Black;
        FixedPitchOnly = false;
        Font = SystemFonts.DefaultFont;
        FontMustExist = false;
        MaxSize = 0;
        MinSize = 0;
        ScriptsOnly = false;
        ShowApply = false;
        ShowColor = false;
        ShowEffects = true;
        ShowHelp = false;
    }

    protected override bool RunDialog(IntPtr hwndOwner)
    {
        using var form = new FontDialogForm(Font, Color, ShowEffects, ShowColor, ShowApply, ShowHelp, FixedPitchOnly, MinSize, MaxSize);
        form.HelpRequested += (_, _) => OnHelpRequest(EventArgs.Empty);
        form.Applied += (_, _) =>
        {
            Font = form.SelectedFont;
            Color = form.SelectedColor;
            OnApply(EventArgs.Empty);
        };

        var result = form.ShowDialog(OwnerForm());
        if (result != DialogResult.OK) return false;

        Font = form.SelectedFont;
        Color = form.SelectedColor;
        return true;
    }

    public override string ToString() => $"{base.ToString()}, Font: {Font}";

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
}
