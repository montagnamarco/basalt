using System.ComponentModel;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// The standard message box: a modal dialog with text, an icon and a set of
/// buttons, drawn as Windows 2000 drew it, that returns the button pressed.
/// </summary>
public class MessageBox
{
    private MessageBox()
    {
    }

    public static DialogResult Show(string? text) =>
        ShowCore(null, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(string? text, string? caption) =>
        ShowCore(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons) =>
        ShowCore(null, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        ShowCore(null, text, caption, buttons, icon, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) =>
        ShowCore(null, text, caption, buttons, icon, defaultButton, 0, false);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options) =>
        ShowCore(null, text, caption, buttons, icon, defaultButton, options, false);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, bool displayHelpButton) =>
        ShowCore(null, text, caption, buttons, icon, defaultButton, options, displayHelpButton);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath) =>
        ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, string keyword) =>
        ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator) =>
        ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator, object? param) =>
        ShowCore(null, text, caption, buttons, icon, defaultButton, options, true);

    public static DialogResult Show(IWin32Window? owner, string? text) =>
        ShowCore(owner, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption) =>
        ShowCore(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons) =>
        ShowCore(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        ShowCore(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1, 0, false);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) =>
        ShowCore(owner, text, caption, buttons, icon, defaultButton, 0, false);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options) =>
        ShowCore(owner, text, caption, buttons, icon, defaultButton, options, false);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath) =>
        ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, string keyword) =>
        ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator) =>
        ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator, object? param) =>
        ShowCore(owner, text, caption, buttons, icon, defaultButton, options, true);

    /// <summary>
    /// Validates as WinForms does, then shows the box modally. Help files
    /// have no viewer here: the Help button is shown and raises HelpRequested
    /// on the owner, which is what applications hook.
    /// </summary>
    private static DialogResult ShowCore(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, bool showHelp)
    {
        if (!Enum.IsDefined(buttons)) throw new InvalidEnumArgumentException(nameof(buttons), (int)buttons, typeof(MessageBoxButtons));
        if (!Enum.IsDefined(icon)) throw new InvalidEnumArgumentException(nameof(icon), (int)icon, typeof(MessageBoxIcon));
        if (!Enum.IsDefined(defaultButton)) throw new InvalidEnumArgumentException(nameof(defaultButton), (int)defaultButton, typeof(MessageBoxDefaultButton));

        var serviceOptions = MessageBoxOptions.ServiceNotification | MessageBoxOptions.DefaultDesktopOnly;
        if ((options & serviceOptions) != 0 && owner is not null)
        {
            throw new ArgumentException("Cannot show a modal dialog box with an owner when ServiceNotification or DefaultDesktopOnly is specified.", nameof(options));
        }

        using var box = new MessageBoxForm(text ?? string.Empty, caption ?? string.Empty, buttons, icon, defaultButton, options, showHelp, owner as Control);

        return box.ShowDialog(owner);
    }
}
