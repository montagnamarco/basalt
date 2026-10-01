using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualBasic.Internal;

namespace Microsoft.VisualBasic;

/// <summary>
/// The type Microsoft.VisualBasic.Core's Interaction.MsgBox and Interaction.InputBox
/// find by reflection, in the assembly named Microsoft.VisualBasic.Forms, with the
/// exact signatures the VB runtime calls with every default already resolved. The
/// type and its members are internal: nothing outside this assembly calls them by
/// name, only Microsoft.VisualBasic.Core's reflection lookup does.
/// </summary>
internal static class _Interaction
{
    /// <summary>Shows MsgBox's dialog and returns the button the user chose.</summary>
    public static MsgBoxResult MsgBox(object Prompt, MsgBoxStyle Buttons, object Title)
    {
        // Both arrive as Object; VB converts them with CStr, so a number or a date shows as text.
        var text = Prompt is null ? string.Empty : Microsoft.VisualBasic.CompilerServices.Conversions.ToString(Prompt);
        var caption = Title is null ? DefaultTitle() : Microsoft.VisualBasic.CompilerServices.Conversions.ToString(Title);

        var style = (int)Buttons;
        var buttons = ButtonsOf(style & 0xF);
        var icon = IconOf(style & 0xF0);
        var defaultButton = DefaultButtonOf(style & 0xF00);
        var options = (MessageBoxOptions)(style & 0x180000);
        var showHelp = (Buttons & MsgBoxStyle.MsgBoxHelp) != 0;

        var result = showHelp
            ? MessageBox.Show(text, caption, buttons, icon, defaultButton, options, string.Empty)
            : MessageBox.Show(text, caption, buttons, icon, defaultButton, options);

        return ResultOf(result);
    }

    /// <summary>Shows InputBox's dialog and returns the text the user typed, or "" if cancelled.</summary>
    public static string InputBox(string Prompt, string Title, string DefaultResponse, int XPos, int YPos)
    {
        using var box = new InputBoxForm(Prompt, Title, DefaultResponse, XPos, YPos);

        return box.ShowDialog() == DialogResult.OK ? box.ResponseText : string.Empty;
    }

    private static MessageBoxButtons ButtonsOf(int group) => group switch
    {
        1 => MessageBoxButtons.OKCancel,
        2 => MessageBoxButtons.AbortRetryIgnore,
        3 => MessageBoxButtons.YesNoCancel,
        4 => MessageBoxButtons.YesNo,
        5 => MessageBoxButtons.RetryCancel,
        _ => MessageBoxButtons.OK,
    };

    private static MessageBoxIcon IconOf(int group) => group switch
    {
        0x10 => MessageBoxIcon.Error,
        0x20 => MessageBoxIcon.Question,
        0x30 => MessageBoxIcon.Warning,
        0x40 => MessageBoxIcon.Information,
        _ => MessageBoxIcon.None,
    };

    private static MessageBoxDefaultButton DefaultButtonOf(int group) => group switch
    {
        0x100 => MessageBoxDefaultButton.Button2,
        0x200 => MessageBoxDefaultButton.Button3,
        _ => MessageBoxDefaultButton.Button1,
    };

    private static MsgBoxResult ResultOf(DialogResult result) => result switch
    {
        DialogResult.OK => MsgBoxResult.Ok,
        DialogResult.Cancel => MsgBoxResult.Cancel,
        DialogResult.Abort => MsgBoxResult.Abort,
        DialogResult.Retry => MsgBoxResult.Retry,
        DialogResult.Ignore => MsgBoxResult.Ignore,
        DialogResult.Yes => MsgBoxResult.Yes,
        DialogResult.No => MsgBoxResult.No,
        _ => MsgBoxResult.Cancel,
    };

    /// <summary>
    /// The title VB uses when the caller omits one: the entry assembly's
    /// AssemblyTitle, falling back to its simple name.
    /// </summary>
    internal static string DefaultTitle()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null) return string.Empty;

        var title = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;

        return !string.IsNullOrEmpty(title) ? title : assembly.GetName().Name ?? string.Empty;
    }
}
