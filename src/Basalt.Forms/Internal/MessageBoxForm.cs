using System.Drawing;
using System.Globalization;

namespace System.Windows.Forms.Internal;

/// <summary>
/// The dialog behind MessageBox: icon on the left, text beside it, buttons
/// centred underneath, laid out with the measurements of the Windows 2000
/// message box.
/// </summary>
internal sealed class MessageBoxForm : Form
{
    private const int Edge = 11;
    private const int IconSize = 32;
    private const int IconGap = 12;
    private const int ButtonGap = 6;
    private const int MaximumTextWidth = 420;

    private readonly Icon? _icon;
    private readonly Control? _helpTarget;
    private Rectangle _iconBounds;

    public MessageBoxForm(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, bool showHelp, Control? helpTarget)
    {
        _icon = IconFor(icon);
        _helpTarget = helpTarget;

        SuspendLayout();

        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = SystemFonts.MessageBoxFont ?? DefaultFont;

        var results = Results(buttons);

        // Without a way to cancel, the box cannot be closed from its title bar either.
        var cancellable = results.Contains(DialogResult.Cancel) || buttons == MessageBoxButtons.OK;
        ControlBox = cancellable;

        var label = new Label
        {
            AutoSize = false,
            Text = text,
            UseMnemonic = false,
            TextAlign = (options & MessageBoxOptions.RightAlign) != 0 ? ContentAlignment.TopRight : ContentAlignment.TopLeft,
        };

        var textSize = TextRenderer.MeasureText(text, Font, new Size(MaximumTextWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var textLeft = _icon is null ? Edge : Edge + IconSize + IconGap;
        var contentHeight = Math.Max(textSize.Height, _icon is null ? 0 : IconSize);
        var textTop = Edge + (_icon is not null && textSize.Height < IconSize ? (IconSize - textSize.Height) / 2 : 0);
        label.Bounds = new Rectangle(textLeft, textTop, textSize.Width, textSize.Height);
        _iconBounds = new Rectangle(Edge, Edge, IconSize, IconSize);

        var buttonControls = new List<Button>();
        foreach (var result in results)
        {
            var button = new Button { Text = Caption(result), DialogResult = result, Size = new Size(75, 23) };
            buttonControls.Add(button);
        }

        if (showHelp)
        {
            var help = new Button { Text = Caption(DialogResult.None), Size = new Size(75, 23) };
            help.Click += (_, _) => RaiseHelp();
            buttonControls.Add(help);
        }

        var buttonsWidth = buttonControls.Count * 75 + (buttonControls.Count - 1) * ButtonGap;
        var clientWidth = Math.Max(textLeft + textSize.Width + Edge, buttonsWidth + 2 * Edge);
        var buttonsTop = Edge + contentHeight + Edge + 4;
        var left = (clientWidth - buttonsWidth) / 2;

        for (var i = 0; i < buttonControls.Count; i++)
        {
            buttonControls[i].Location = new Point(left + i * (75 + ButtonGap), buttonsTop);
            buttonControls[i].TabIndex = i;
        }

        ClientSize = new Size(clientWidth, buttonsTop + 23 + Edge);

        Controls.Add(label);
        foreach (var button in buttonControls) Controls.Add(button);

        var defaultIndex = Math.Min((int)defaultButton / 0x100, results.Count - 1);
        AcceptButton = buttonControls[defaultIndex];
        if (results.Contains(DialogResult.Cancel)) CancelButton = buttonControls[results.IndexOf(DialogResult.Cancel)];
        else if (buttons == MessageBoxButtons.OK) CancelButton = buttonControls[0];

        ActiveControl = buttonControls[defaultIndex];

        ResumeLayout(false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_icon is not null) e.Graphics.DrawIcon(_icon, _iconBounds);

        base.OnPaint(e);
    }

    /// <summary>The Help button asks the owner, as Windows sends WM_HELP to it.</summary>
    private void RaiseHelp()
    {
        if (_helpTarget is null) return;

        var arguments = new HelpEventArgs(MousePosition);
        _helpTarget.RaiseHelpRequested(arguments);
    }

    /// <summary>The selected button arrives via Shown: the active control is focused once the window is up.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl?.Focus();
    }

    private static Icon? IconFor(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Error => SystemIcons.Error,
        MessageBoxIcon.Warning => SystemIcons.Warning,
        MessageBoxIcon.Information => SystemIcons.Information,
        MessageBoxIcon.Question => SystemIcons.Question,
        _ => null,
    };

    private static List<DialogResult> Results(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OKCancel => [DialogResult.OK, DialogResult.Cancel],
        MessageBoxButtons.AbortRetryIgnore => [DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore],
        MessageBoxButtons.YesNoCancel => [DialogResult.Yes, DialogResult.No, DialogResult.Cancel],
        MessageBoxButtons.YesNo => [DialogResult.Yes, DialogResult.No],
        MessageBoxButtons.RetryCancel => [DialogResult.Retry, DialogResult.Cancel],
        MessageBoxButtons.CancelTryContinue => [DialogResult.Cancel, DialogResult.TryAgain, DialogResult.Continue],
        _ => [DialogResult.OK],
    };

    /// <summary>
    /// Button captions in the user's language, as Windows takes them from the
    /// system; English for languages not listed. DialogResult.None is Help.
    /// </summary>
    private static string Caption(DialogResult result)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var table = Captions.TryGetValue(language, out var found) ? found : Captions["en"];

        return table[result];
    }

    private static readonly Dictionary<string, Dictionary<DialogResult, string>> Captions = new()
    {
        ["en"] = new()
        {
            [DialogResult.OK] = "OK", [DialogResult.Cancel] = "Cancel", [DialogResult.Abort] = "&Abort",
            [DialogResult.Retry] = "&Retry", [DialogResult.Ignore] = "&Ignore", [DialogResult.Yes] = "&Yes",
            [DialogResult.No] = "&No", [DialogResult.TryAgain] = "&Try Again", [DialogResult.Continue] = "&Continue",
            [DialogResult.None] = "Help",
        },
        ["it"] = new()
        {
            [DialogResult.OK] = "OK", [DialogResult.Cancel] = "Annulla", [DialogResult.Abort] = "&Interrompi",
            [DialogResult.Retry] = "&Riprova", [DialogResult.Ignore] = "I&gnora", [DialogResult.Yes] = "&Sì",
            [DialogResult.No] = "&No", [DialogResult.TryAgain] = "&Riprova", [DialogResult.Continue] = "&Continua",
            [DialogResult.None] = "Guida",
        },
        ["de"] = new()
        {
            [DialogResult.OK] = "OK", [DialogResult.Cancel] = "Abbrechen", [DialogResult.Abort] = "&Abbrechen",
            [DialogResult.Retry] = "&Wiederholen", [DialogResult.Ignore] = "&Ignorieren", [DialogResult.Yes] = "&Ja",
            [DialogResult.No] = "&Nein", [DialogResult.TryAgain] = "&Wiederholen", [DialogResult.Continue] = "&Weiter",
            [DialogResult.None] = "Hilfe",
        },
        ["fr"] = new()
        {
            [DialogResult.OK] = "OK", [DialogResult.Cancel] = "Annuler", [DialogResult.Abort] = "&Abandonner",
            [DialogResult.Retry] = "&Recommencer", [DialogResult.Ignore] = "&Ignorer", [DialogResult.Yes] = "&Oui",
            [DialogResult.No] = "&Non", [DialogResult.TryAgain] = "&Réessayer", [DialogResult.Continue] = "&Continuer",
            [DialogResult.None] = "Aide",
        },
        ["es"] = new()
        {
            [DialogResult.OK] = "Aceptar", [DialogResult.Cancel] = "Cancelar", [DialogResult.Abort] = "&Anular",
            [DialogResult.Retry] = "&Reintentar", [DialogResult.Ignore] = "&Omitir", [DialogResult.Yes] = "&Sí",
            [DialogResult.No] = "&No", [DialogResult.TryAgain] = "&Volver a intentarlo", [DialogResult.Continue] = "&Continuar",
            [DialogResult.None] = "Ayuda",
        },
    };
}
