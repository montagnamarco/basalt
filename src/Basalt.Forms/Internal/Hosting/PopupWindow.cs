using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// A borderless native window for a top-level control that is not a form: a
/// drop-down list, a menu, a tool tip. It can extend beyond its owner form, as
/// WS_POPUP windows do, and it does not take the activation from the form.
/// </summary>
internal sealed class PopupWindow : Avalonia.Controls.Window
{
    public PopupWindow(Control root)
    {
        Root = root;
        Canvas = new FormsCanvas(root) { Focusable = false };

        Template = new FuncControlTemplate<PopupWindow>((window, scope) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            [!ContentPresenter.ContentProperty] = window[!ContentProperty],
        });

        Content = Canvas;
        Background = Brushes.Transparent;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    public Control Root { get; }

    public FormsCanvas Canvas { get; }

    /// <summary>Places the window, in WinForms screen coordinates, and sizes it to the control.</summary>
    public void Place(Drawing.Rectangle screenBounds)
    {
        var scaling = DesktopScaling;
        Position = new PixelPoint((int)Math.Round(screenBounds.X * scaling), (int)Math.Round(screenBounds.Y * scaling));
        Width = Math.Max(screenBounds.Width, 1);
        Height = Math.Max(screenBounds.Height, 1);
    }
}

/// <summary>
/// The popups open now. A press anywhere else in the application closes the
/// ones that close on an outside click, as a menu or a drop-down list does,
/// and the press is not passed on.
/// </summary>
internal static class Popups
{
    private static readonly List<Control> Open = [];

    /// <summary>The window each popup belongs to: its owner's top-level control.</summary>
    private static readonly Dictionary<Control, Control?> Owners = [];

    public static IReadOnlyList<Control> All => Open;

    /// <summary>Shows the top-level control in a popup window at the screen bounds, owned by the owner's window.</summary>
    public static void Show(Control root, Control? owner, Drawing.Rectangle screenBounds)
    {
        AvaloniaPlatform.EnsureStarted();

        var window = root.PopupWindow ??= new PopupWindow(root);
        root.Bounds = new Drawing.Rectangle(screenBounds.Location, screenBounds.Size);
        root.CreateControl(ignoreVisible: true);
        window.Place(screenBounds);

        if (!Open.Contains(root)) Open.Add(root);
        Owners[root] = owner?.TopLevelControl;

        var ownerCanvas = owner?.TopLevelControl?.Canvas;
        var ownerWindow = ownerCanvas is null ? null : Avalonia.Controls.TopLevel.GetTopLevel(ownerCanvas) as Avalonia.Controls.Window;
        if (!window.IsVisible)
        {
            if (ownerWindow is not null) window.Show(ownerWindow);
            else window.Show();
        }

        root.Invalidate(true);
    }

    public static void Close(Control root)
    {
        Open.Remove(root);
        Owners.Remove(root);
        root.PopupWindow?.Hide();
    }

    /// <summary>A window is going away: the popups it owns close with it, as owned windows do.</summary>
    public static void CloseOwnedBy(Control window)
    {
        foreach (var popup in Open.ToArray())
        {
            if (Owners.GetValueOrDefault(popup) == window) popup.DismissPopup();
        }
    }

    /// <summary>
    /// The popup that takes the keyboard while it is open, for a window: the
    /// most recent one that asks for it, as a menu takes the keys from the
    /// form that opened it.
    /// </summary>
    public static Control? KeyboardOwnerFor(Control window)
    {
        for (var i = Open.Count - 1; i >= 0; i--)
        {
            var popup = Open[i];
            if (popup.TakesKeyboardWhileOpen && Owners.GetValueOrDefault(popup) == window) return popup;
        }

        return null;
    }

    /// <summary>
    /// A press arrived in a window: popups other than the one pressed close if
    /// they close on outside clicks. True when the press closed one and must
    /// not go on, as the click that dismisses a menu does nothing else.
    /// </summary>
    public static bool DismissForPressIn(Control pressedWindow)
    {
        var dismissed = false;

        foreach (var popup in Open.ToArray())
        {
            if (popup == pressedWindow || popup.IsPopupAncestorOf(pressedWindow) || !popup.ClosesOnOutsideClick) continue;

            popup.DismissPopup();
            dismissed = true;
        }

        return dismissed;
    }

    /// <summary>A window lost the activation: the popups it owns that close on outside clicks close.</summary>
    public static void DismissAll(Control window)
    {
        foreach (var popup in Open.ToArray())
        {
            if (popup.ClosesOnOutsideClick && Owners.GetValueOrDefault(popup) == window) popup.DismissPopup();
        }
    }
}
