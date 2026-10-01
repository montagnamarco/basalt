using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Answers a control's F1: a control extended with a help string shows it in
/// a pop-up; one extended with only a keyword or navigator opens the help
/// file at <see cref="HelpNamespace"/> instead.
/// </summary>
[ProvideProperty("HelpString", typeof(Control))]
[ProvideProperty("HelpKeyword", typeof(Control))]
[ProvideProperty("HelpNavigator", typeof(Control))]
[ProvideProperty("ShowHelp", typeof(Control))]
public class HelpProvider : Component, IExtenderProvider
{
    private readonly Dictionary<Control, HelpItem> _items = [];

    public HelpProvider()
    {
        // Microsoft's HelpProvider does not override Dispose(bool); it only needs
        // to let go of the controls it hooked, which the Disposed event covers.
        Disposed += (_, _) =>
        {
            foreach (var control in _items.Keys.ToArray()) UnhookControl(control);

            _items.Clear();
        };
    }

    [DefaultValue(null)]
    public virtual string? HelpNamespace { get; set; }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    public virtual bool CanExtend(object? target) => target is Control;

    [DefaultValue("")]
    public virtual string GetHelpKeyword(Control ctl) => _items.TryGetValue(NotNull(ctl), out var item) ? item.HelpKeyword ?? string.Empty : string.Empty;

    [DefaultValue(HelpNavigator.AssociateIndex)]
    public virtual HelpNavigator GetHelpNavigator(Control ctl) => _items.TryGetValue(NotNull(ctl), out var item) ? item.Navigator : HelpNavigator.AssociateIndex;

    [DefaultValue(null)]
    public virtual string GetHelpString(Control ctl) => _items.TryGetValue(NotNull(ctl), out var item) ? item.HelpString ?? string.Empty : string.Empty;

    public virtual bool GetShowHelp(Control ctl) => !_items.TryGetValue(NotNull(ctl), out var item) || item.ShowHelp;

    public virtual void ResetShowHelp(Control ctl) => SetShowHelp(ctl, true);

    public virtual void SetHelpKeyword(Control ctl, string? keyword) => EnsureItem(ctl).HelpKeyword = keyword;

    public virtual void SetHelpNavigator(Control ctl, HelpNavigator navigator)
    {
        if (!Enum.IsDefined(navigator)) throw new InvalidEnumArgumentException(nameof(navigator), (int)navigator, typeof(HelpNavigator));

        EnsureItem(ctl).Navigator = navigator;
    }

    public virtual void SetHelpString(Control ctl, string? helpString) => EnsureItem(ctl).HelpString = helpString;

    public virtual void SetShowHelp(Control ctl, bool value) => EnsureItem(ctl).ShowHelp = value;

    public override string ToString() => $"{base.ToString()}, HelpNamespace: {HelpNamespace}";

    private static Control NotNull(Control ctl)
    {
        ArgumentNullException.ThrowIfNull(ctl);

        return ctl;
    }

    private HelpItem EnsureItem(Control ctl)
    {
        ArgumentNullException.ThrowIfNull(ctl);

        if (_items.TryGetValue(ctl, out var existing)) return existing;

        var item = new HelpItem();
        _items[ctl] = item;
        HookControl(ctl);

        return item;
    }

    private void HookControl(Control control)
    {
        control.HelpRequested += OnHelpRequested;
        control.Disposed += OnControlDisposed;
    }

    private void UnhookControl(Control control)
    {
        control.HelpRequested -= OnHelpRequested;
        control.Disposed -= OnControlDisposed;
    }

    private void OnControlDisposed(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            UnhookControl(control);
            _items.Remove(control);
        }
    }

    /// <summary>
    /// A help string wins over the help file: it needs nothing beyond the
    /// pop-up this process can draw itself. Without one, a keyword or
    /// navigator asks Help to open the namespace's help file.
    /// </summary>
    private void OnHelpRequested(object? sender, HelpEventArgs e)
    {
        if (sender is not Control control || !_items.TryGetValue(control, out var item) || !item.ShowHelp) return;

        if (!string.IsNullOrEmpty(item.HelpString))
        {
            Help.ShowPopup(control, item.HelpString, Control.MousePosition);
            return;
        }

        if (string.IsNullOrEmpty(HelpNamespace)) return;

        if (!string.IsNullOrEmpty(item.HelpKeyword)) Help.ShowHelp(control, HelpNamespace, item.Navigator, item.HelpKeyword);
        else Help.ShowHelp(control, HelpNamespace, item.Navigator);
    }

    /// <summary>What this provider tracks for one extended control.</summary>
    private sealed class HelpItem
    {
        public string? HelpString;
        public string? HelpKeyword;
        public HelpNavigator Navigator = HelpNavigator.AssociateIndex;
        public bool ShowHelp = true;
    }
}
