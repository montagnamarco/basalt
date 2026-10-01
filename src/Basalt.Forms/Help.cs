using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// Opens a help file's viewer, or shows a small pop-up of plain text next to
/// a control, the way F1 shows context help without a help file.
/// </summary>
/// <remarks>
/// A real help viewer (WinHelp's .hlp or the HTML Help .chm) is Windows'; we
/// have neither. <see cref="ShowHelp(Control?, string?)"/> therefore hands the
/// address to the system, which opens a web page in the browser and a local
/// file in whatever application the system associates with it; the keyword,
/// navigator and topic parameters only a help viewer understands are ignored.
/// Like the Windows viewer, which reports a missing file in its own window,
/// it never throws: F1 must not bring an application down.
/// </remarks>
public static class Help
{
    /// <summary>What opens an address; replaceable so tests do not start a browser.</summary>
    internal static Action<string> Opener { get; set; } = OpenWithSystem;

    public static void ShowHelp(Control? parent, string? url) => Open(url);

    public static void ShowHelp(Control? parent, string? url, HelpNavigator navigator) => Open(url);

    public static void ShowHelp(Control? parent, string? url, string? keyword) => Open(url);

    public static void ShowHelp(Control? parent, string? url, HelpNavigator command, object? parameter) => Open(url);

    public static void ShowHelpIndex(Control? parent, string? url) => Open(url);

    private static void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var isAddress = Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile;
        if (!isAddress && !File.Exists(url)) return;

        Opener(url);
    }

    private static void OpenWithSystem(string address)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Nothing can open it: as with a help file Windows cannot show, the request ends here.
        }
    }

    /// <summary>Shows the caption in a small pop-up at the point, in screen coordinates, as F1 help without a help file does.</summary>
    public static void ShowPopup(Control? parent, string caption, Point location)
    {
        ArgumentNullException.ThrowIfNull(caption);

        var font = SystemFonts.MessageBoxFont ?? Control.DefaultFont;
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var textSize = TextRenderer.MeasureText(caption, font, new Size(int.MaxValue, int.MaxValue), flags);
        var size = new Size(textSize.Width + 2 * TextPadding + 2, textSize.Height + 2 * TextPadding + 2);

        var popup = new HelpPopup(caption, font);
        Popups.Show(popup, parent, new Rectangle(location, size));
    }

    private const int TextPadding = 3;

    /// <summary>The pop-up window ShowPopup shows: the Info colour, a one-pixel frame, and the caption.</summary>
    private sealed class HelpPopup : Control
    {
        private readonly string _caption;
        private readonly Font _font;

        public HelpPopup(string caption, Font font)
        {
            _caption = caption;
            _font = font;

            SetTopLevel(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var background = new SolidBrush(SystemColors.Info);
            using var frame = new Pen(SystemColors.WindowFrame);

            var bounds = ClientRectangle;
            e.Graphics.FillRectangle(background, bounds);
            e.Graphics.DrawRectangle(frame, new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));

            var textBounds = Rectangle.Inflate(bounds, -TextPadding - 1, -TextPadding - 1);
            TextRenderer.DrawText(e.Graphics, _caption, _font, textBounds, SystemColors.InfoText, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
}
