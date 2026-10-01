using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Controls;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>
/// A window: the top of a tree of controls, shown in a native window whose
/// client area is the form's.
/// </summary>
/// <remarks>
/// The lifecycle follows WinForms: Show creates the handle, raises Load
/// once, makes the form visible and posts Shown; Close raises FormClosing
/// (which may cancel) and FormClosed, then disposes a modeless form, while a
/// modal one is only hidden so ShowDialog can return its DialogResult.
/// </remarks>
[DesignerCategory("Form")]
[DefaultEvent("Load")]
public partial class Form : ContainerControl
{
    private HostWindow? _host;
    private FormBorderStyle _borderStyle = FormBorderStyle.Sizable;
    private FormStartPosition _startPosition = FormStartPosition.WindowsDefaultLocation;
    private FormWindowState _windowState = FormWindowState.Normal;
    private DialogResult _dialogResult;
    private Form? _owner;
    private readonly List<Form> _ownedForms = [];
    private bool _controlBox = true;
    private bool _minimizeBox = true;
    private bool _maximizeBox = true;
    private bool _showInTaskbar = true;
    private bool _topMost;
    private double _opacity = 1.0;

    private bool _loaded;
    private bool _shownRaised;
    private bool _closing;

    /// <summary>How many times the form completed a close: a close inside Load must stop the form from showing.</summary>
    private int _completedCloses;
    private bool _closingHostOurselves;
    private bool _modal;
    private DispatcherFrame? _modalFrame;

    public Form()
    {
        // A form is a window of its own and starts hidden, until Show or Application.Run.
        // The state is set directly: SetVisibleCore is virtual, and an override in a
        // derived form would run before that form's own fields are initialised.
        SetTopLevel(true);
        InitializeHidden();
    }

    protected override Size DefaultSize => new(300, 300);

    /// <summary>The window's styles, from which the frame and so the client size follow.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style &= ~WindowStyles.Child;
            parameters.ExStyle |= WindowStyles.ExControlParent;

            switch (_borderStyle)
            {
                case FormBorderStyle.None:
                    break;

                case FormBorderStyle.FixedSingle:
                    parameters.Style |= WindowStyles.Border;
                    break;

                case FormBorderStyle.Fixed3D:
                    parameters.Style |= WindowStyles.Border;
                    parameters.ExStyle |= WindowStyles.ExClientEdge;
                    break;

                case FormBorderStyle.FixedDialog:
                    parameters.Style |= WindowStyles.Border;
                    parameters.ExStyle |= WindowStyles.ExDialogModalFrame;
                    break;

                case FormBorderStyle.Sizable:
                    parameters.Style |= WindowStyles.Border | WindowStyles.ThickFrame;
                    break;

                case FormBorderStyle.FixedToolWindow:
                    parameters.Style |= WindowStyles.Border;
                    parameters.ExStyle |= WindowStyles.ExToolWindow;
                    break;

                case FormBorderStyle.SizableToolWindow:
                    parameters.Style |= WindowStyles.Border | WindowStyles.ThickFrame;
                    parameters.ExStyle |= WindowStyles.ExToolWindow;
                    break;
            }

            // Any border but none has a title bar, unless there is neither text nor control box.
            if (_borderStyle != FormBorderStyle.None && (_controlBox || Text.Length > 0)) parameters.Style |= WindowStyles.Caption;

            if (_controlBox) parameters.Style |= WindowStyles.SystemMenu;
            if (_maximizeBox) parameters.Style |= WindowStyles.MaximizeBox;
            if (_minimizeBox) parameters.Style |= WindowStyles.MinimizeBox;
            if (_topMost) parameters.ExStyle |= WindowStyles.ExTopmost;

            return parameters;
        }
    }

    [Localizable(true)]
    [DefaultValue(FormBorderStyle.Sizable)]
    public FormBorderStyle FormBorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FormBorderStyle));

            if (value == _borderStyle) return;

            // The client area keeps its size; the frame around it changes.
            var clientSize = ClientSize;
            _borderStyle = value;
            UpdateStyles();
            ClientSize = clientSize;
            ApplyWindowStyle();
        }
    }

    [DefaultValue(true)]
    public bool ControlBox
    {
        get => _controlBox;
        set
        {
            _controlBox = value;
            ApplyWindowStyle();
        }
    }

    [DefaultValue(true)]
    public bool MinimizeBox
    {
        get => _minimizeBox;
        set
        {
            _minimizeBox = value;
            ApplyWindowStyle();
        }
    }

    [DefaultValue(true)]
    public bool MaximizeBox
    {
        get => _maximizeBox;
        set
        {
            _maximizeBox = value;
            ApplyWindowStyle();
        }
    }

    [DefaultValue(true)]
    public bool ShowInTaskbar
    {
        get => _showInTaskbar;
        set
        {
            _showInTaskbar = value;
            if (_host is not null) _host.ShowInTaskbar = value;
        }
    }

    [DefaultValue(false)]
    public bool TopMost
    {
        get => _topMost;
        set
        {
            _topMost = value;
            if (_host is not null) _host.Topmost = value;
        }
    }

    [DefaultValue(1.0)]
    public double Opacity
    {
        get => _opacity;
        set
        {
            _opacity = Math.Clamp(value, 0.0, 1.0);
            if (_host is not null) _host.Opacity = _opacity;
        }
    }

    [Localizable(true)]
    [DefaultValue(FormStartPosition.WindowsDefaultLocation)]
    public FormStartPosition StartPosition
    {
        get => _startPosition;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FormStartPosition));

            _startPosition = value;
        }
    }

    [DefaultValue(FormWindowState.Normal)]
    public FormWindowState WindowState
    {
        get => _windowState;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FormWindowState));

            _windowState = value;
            if (_host is not null) _host.WindowState = ToAvalonia(value);
        }
    }

    /// <summary>The form's caption, shown in the title bar.</summary>
    [Localizable(true)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_host is not null) _host.Title = Text;
    }

    /// <summary>The result a modal form returns; setting it on a modal form closes it once the current handler returns.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult
    {
        get => _dialogResult;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DialogResult));

            _dialogResult = value;

            // The modal loop checks after each message; here, once the current handler has returned.
            if (_modal && value != DialogResult.None) Dispatcher.UIThread.Post(CheckCloseDialog, DispatcherPriority.Input);
        }
    }

    [Browsable(false)]
    public bool Modal => _modal;

    /// <summary>The form that owns this one: an owned form stays above it and closes with it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Form? Owner
    {
        get => _owner;
        set
        {
            if (value == _owner) return;

            _owner?._ownedForms.Remove(this);
            _owner = value;
            _owner?._ownedForms.Add(this);
        }
    }

    [Browsable(false)]
    public Form[] OwnedForms => [.. _ownedForms];

    public void AddOwnedForm(Form? ownedForm)
    {
        if (ownedForm is not null) ownedForm.Owner = this;
    }

    public void RemoveOwnedForm(Form? ownedForm)
    {
        if (ownedForm?.Owner == this) ownedForm.Owner = null;
    }

    private IButtonControl? _acceptButton;

    /// <summary>The button Enter presses; it draws itself as the default button.</summary>
    [DefaultValue(null)]
    public IButtonControl? AcceptButton
    {
        get => _acceptButton;
        set
        {
            if (value == _acceptButton) return;

            _acceptButton?.NotifyDefault(false);
            _acceptButton = value;
            _acceptButton?.NotifyDefault(true);
        }
    }

    [DefaultValue(null)]
    public IButtonControl? CancelButton { get; set; }

    /// <summary>Whether the form sees its controls' keys before they do.</summary>
    [DefaultValue(false)]
    public bool KeyPreview { get; set; }

    /// <summary>A form is top level unless it is placed inside another control.</summary>
    [Browsable(false)]
    public bool TopLevel
    {
        get => GetTopLevel();
        set
        {
            if (value == GetTopLevel()) return;
            if (value && Parent is not null) throw new ArgumentException("Top-level control cannot be added to a control.", nameof(value));

            SetTopLevel(value);
        }
    }

    /// <summary>The form of this application whose window is active, or null.</summary>
    public static Form? ActiveForm { get; private set; }

    /// <summary>Shows the form without waiting for it to close.</summary>
    public void Show(IWin32Window? owner)
    {
        if (owner == this) throw new InvalidOperationException("Forms cannot own themselves or their owners.");

        if (owner is Form ownerForm) Owner = ownerForm;

        Visible = true;
    }

    /// <summary>Shows the form and waits, processing messages, until it is closed; then returns its DialogResult.</summary>
    public DialogResult ShowDialog() => ShowDialog(null);

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        if (owner == this) throw new ArgumentException("Forms cannot own themselves or their owners.", nameof(owner));
        if (Visible) throw new InvalidOperationException("Form that is already visible cannot be displayed as a modal dialog box. Set the form's visible property to false before calling showDialog.");
        if (!Enabled) throw new InvalidOperationException("Form that is not enabled cannot be displayed as a modal dialog box. Set the form's enabled property to true before calling showDialog.");
        if (!TopLevel) throw new InvalidOperationException("Form that is not a top-level form cannot be displayed as a modal dialog box. Remove the form from any parent form before calling showDialog.");

        var ownerForm = owner as Form ?? ActiveForm;
        if (ownerForm == this) ownerForm = null;

        var previousOwner = _owner;
        if (ownerForm is not null && _owner is null) Owner = ownerForm;

        _modal = true;
        _dialogResult = DialogResult.None;

        var disabled = DisableOtherForms();

        try
        {
            Visible = true;

            if (_modal && !IsDisposed)
            {
                _modalFrame = new DispatcherFrame();
                Dispatcher.UIThread.PushFrame(_modalFrame);
            }
        }
        finally
        {
            _modalFrame = null;
            _modal = false;
            foreach (var form in disabled) form.SetHostEnabled(true);

            if (!IsDisposed) Visible = false;
            if (_owner != previousOwner) Owner = previousOwner;
            ownerForm?.Activate();
        }

        return _dialogResult;
    }

    /// <summary>The other forms ignore input while a modal one is open, as Windows disables their windows.</summary>
    private List<Form> DisableOtherForms()
    {
        var disabled = new List<Form>();

        foreach (var form in Application.OpenForms.Snapshot())
        {
            if (form == this || form._host is not { IsEnabled: true }) continue;

            form.SetHostEnabled(false);
            disabled.Add(form);
        }

        return disabled;
    }

    private void SetHostEnabled(bool enabled)
    {
        if (_host is not null) _host.IsEnabled = enabled;
    }

    /// <summary>Raises FormClosing for the modal form; unless cancelled, FormClosed, and the modal loop ends.</summary>
    private void CheckCloseDialog()
    {
        if (!_modal || _dialogResult == DialogResult.None || _closing) return;

        var closing = new FormClosingEventArgs(CloseReason.None, false);
        RaiseClosing(closing);

        if (closing.Cancel)
        {
            _dialogResult = DialogResult.None;
            return;
        }

        RaiseClosed(new FormClosedEventArgs(CloseReason.None));
        EndModal();
    }

    private void EndModal()
    {
        _modal = false;
        if (_modalFrame is not null) _modalFrame.Continue = false;
    }

    /// <summary>Closes the form: FormClosing, which may cancel, then FormClosed; a modeless form is disposed.</summary>
    public void Close()
    {
        if (GetCreatingHandle()) throw new InvalidOperationException("Value Close() cannot be called while doing CreateHandle().");

        if (IsHandleCreated) CloseCore(CloseReason.UserClosing);
        else Dispose();
    }

    /// <summary>Runs the closing sequence for a reason; false when a handler cancelled it.</summary>
    internal bool CloseCore(CloseReason reason)
    {
        if (_closing || IsDisposed) return true;

        _closing = true;

        try
        {
            var closing = new FormClosingEventArgs(reason, false);

            // Owned forms are asked first, and their own answers count, as Windows asks them.
            foreach (var owned in _ownedForms.ToArray())
            {
                var ownedClosing = new FormClosingEventArgs(CloseReason.FormOwnerClosing, false);
                owned.RaiseClosing(ownedClosing);
                if (ownedClosing.Cancel) closing.Cancel = true;
            }

            RaiseClosing(closing);
            if (closing.Cancel) return false;

            if (_modal)
            {
                if (_dialogResult == DialogResult.None) _dialogResult = DialogResult.Cancel;

                RaiseClosed(new FormClosedEventArgs(reason));
                _completedCloses++;
                EndModal();

                return true;
            }

            foreach (var owned in _ownedForms.ToArray()) owned.CloseFromOwner();

            RaiseClosed(new FormClosedEventArgs(reason));
            _completedCloses++;
            Dispose();

            return true;
        }
        finally
        {
            _closing = false;
        }
    }

    private void CloseFromOwner()
    {
        RaiseClosed(new FormClosedEventArgs(CloseReason.FormOwnerClosing));
        Dispose();
    }

    private void RaiseClosing(FormClosingEventArgs e)
    {
#pragma warning disable CS0618 // The obsolete Closing event is still raised, before FormClosing, as WinForms does.
        OnClosing(e);
#pragma warning restore CS0618
        OnFormClosing(e);
    }

    private void RaiseClosed(FormClosedEventArgs e)
    {
#pragma warning disable CS0618
        OnClosed(e);
#pragma warning restore CS0618
        OnFormClosed(e);
    }

    /// <summary>Brings the form's window to the front and gives it the keyboard.</summary>
    public void Activate()
    {
        if (Visible && _host is not null) _host.Activate();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value == DesiredVisibility && (!value || _host is { IsVisible: true })) return;

        if (!value)
        {
            base.SetVisibleCore(false);
            _host?.Hide();
            return;
        }

        if (!IsHandleCreated) CreateHandle();

        // Load once, before the form is first seen; a handler may close the form.
        if (!_loaded)
        {
            _loaded = true;
            var closesBefore = _completedCloses;
            CreateControl(ignoreVisible: true);
            OnLoad(EventArgs.Empty);
            if (IsDisposed || !IsHandleCreated || _completedCloses != closesBefore) return;
        }

        PlaceWindow();
        base.SetVisibleCore(true);
        Application.OpenForms.Add(this);

        ShowHost();

        if (!_shownRaised)
        {
            _shownRaised = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsDisposed) OnShown(EventArgs.Empty);
            });
        }
    }

    private void ShowHost()
    {
        var host = _host!;
        host.Title = Text;
        host.ShowInTaskbar = _showInTaskbar;
        host.Topmost = _topMost;
        host.Opacity = _opacity;
        host.WindowState = ToAvalonia(_windowState);
        host.SetClientSize(ClientSize);
        ApplyWindowStyle();

        if (_owner?._host is { } ownerHost && !_modal)
        {
            host.Show(ownerHost);
        }
        else
        {
            host.Show();
        }

        // The canvas takes Avalonia's keyboard focus, so keys reach the form's own routing.
        host.Canvas.Focus();
        RequestPaint();
    }

    /// <summary>Where the window opens, from StartPosition.</summary>
    private void PlaceWindow()
    {
        var host = _host!;

        switch (_startPosition)
        {
            case FormStartPosition.CenterScreen:
                host.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                break;

            case FormStartPosition.CenterParent:
                host.WindowStartupLocation = _owner?._host is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
                break;

            case FormStartPosition.Manual:
                host.WindowStartupLocation = WindowStartupLocation.Manual;
                host.SetClientLocation(Location);
                break;

            default:
                // Windows chooses a position, cascading new windows; the platform's default placement is the equivalent.
                host.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                break;
        }
    }

    private void ApplyWindowStyle()
    {
        if (_host is null) return;

        _host.CanResize = _borderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;
        _host.WindowDecorations = _borderStyle == FormBorderStyle.None ? WindowDecorations.None : WindowDecorations.Full;
        _host.CanMinimize = _minimizeBox && _controlBox;
        _host.CanMaximize = _maximizeBox && _controlBox && _host.CanResize;
    }

    private static WindowState ToAvalonia(FormWindowState state) => state switch
    {
        FormWindowState.Minimized => Avalonia.Controls.WindowState.Minimized,
        FormWindowState.Maximized => Avalonia.Controls.WindowState.Maximized,
        _ => Avalonia.Controls.WindowState.Normal,
    };

    protected override void CreateHandle()
    {
        if (IsHandleCreated) return;

        AvaloniaPlatform.EnsureStarted();
        _host ??= new HostWindow(this);

        base.CreateHandle();
    }

    /// <summary>The handle is gone: the native window closes with it.</summary>
    internal override void OnHandleDestroyedInternal()
    {
        Popups.CloseOwnedBy(this);

        if (_host is { } host)
        {
            _host = null;
            _closingHostOurselves = true;

            try
            {
                host.Close();
            }
            finally
            {
                _closingHostOurselves = false;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.OpenForms.Remove(this);
            Owner = null;
            if (ActiveForm == this) ActiveForm = null;
        }

        base.Dispose(disposing);
    }

    internal override FormsCanvas? Canvas => _host?.Canvas;

    /// <summary>The native window, once the handle exists.</summary>
    internal HostWindow? Host => _host;

    /// <summary>The user resized the window: the form's size follows its client area.</summary>
    internal void OnHostClientSizeChanged(Size clientSize)
    {
        var frame = NonClient;
        FormsInput.Guard(this, () => UpdateBounds(Left, Top, clientSize.Width + frame.Horizontal, clientSize.Height + frame.Vertical, clientSize.Width, clientSize.Height));
        Invalidate(true);
    }

    internal void OnHostPositionChanged(Point location) => FormsInput.Guard(this, () => UpdateBounds(location.X, location.Y, Width, Height, ClientSize.Width, ClientSize.Height));

    internal void OnHostActivated()
    {
        FormsInput.Guard(this, () =>
        {
            ActiveForm = this;
            FormsInput.ForgetHeldKeys();
            _host?.Canvas.Focus();
            OnActivated(EventArgs.Empty);

            // A window that becomes active gives the focus back to its active control, or to the first one.
            if (ActiveControl is { CanFocus: true } active) active.SetWindowFocus();
            else if (WindowFocus is null) SelectNextControl(null, true, true, true, false);
        });
    }

    internal void OnHostDeactivated()
    {
        FormsInput.Guard(this, () =>
        {
            if (ActiveForm == this) ActiveForm = null;
            FormsInput.ForgetHeldKeys();
            Popups.DismissAll(this);
            OnDeactivate(EventArgs.Empty);
        });
    }

    /// <summary>
    /// The window is asked to close: by us, it closes; by the user, the form
    /// decides. The user's request is always cancelled and the form closes the
    /// window itself, from inside this handler, if FormClosing allows it:
    /// Avalonia accepts Close() re-entered from Closing (covered by the
    /// lifecycle tests on the headless platform).
    /// </summary>
    internal bool OnHostClosing(bool programmatic)
    {
        if (_closingHostOurselves) return true;

        FormsInput.Guard(this, () => CloseCore(CloseReason.UserClosing));

        return false;
    }

    protected override void SetClientSizeCore(int x, int y)
    {
        base.SetClientSizeCore(x, y);
        _host?.SetClientSize(new Size(x, y));
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        base.SetBoundsCore(x, y, width, height, specified);
        _host?.SetClientSize(ClientSize);
    }

    /// <summary>The menu bar of the form; Windows Forms uses it for menu merging and keyboard menu mode.</summary>
    [DefaultValue(null)]
    public MenuStrip? MainMenuStrip { get; set; }

    /// <summary>After the focused control's chain: a menu item's shortcut key, whatever control has the focus.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (base.ProcessCmdKey(ref msg, keyData)) return true;

        return ToolStripManager.ProcessShortcut(this, keyData);
    }

    /// <summary>Enter presses the accept button and Escape the cancel button; Tab moves among the controls, wrapping.</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & (Keys.Alt | Keys.Control)) == Keys.None)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Enter when AcceptButton is { } accept:
                    accept.PerformClick();
                    return true;

                case Keys.Escape when CancelButton is { } cancel:
                    cancel.PerformClick();
                    return true;
            }
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessTabKey(bool forward) => SelectNextControl(ActiveControl, forward, true, true, true);

    /// <summary>With KeyPreview, the form raises its own key events for its controls' keys first.</summary>
    protected override bool ProcessKeyPreview(ref Message m)
    {
        if (KeyPreview && ProcessKeyEventArgs(ref m)) return true;

        return base.ProcessKeyPreview(ref m);
    }

    protected virtual void OnLoad(EventArgs e) => ((EventHandler?)Events[s_loadEvent])?.Invoke(this, e);

    protected virtual void OnShown(EventArgs e) => ((EventHandler?)Events[s_shownEvent])?.Invoke(this, e);

    protected virtual void OnActivated(EventArgs e) => ((EventHandler?)Events[s_activatedEvent])?.Invoke(this, e);

    protected virtual void OnDeactivate(EventArgs e) => ((EventHandler?)Events[s_deactivateEvent])?.Invoke(this, e);

    protected virtual void OnFormClosing(FormClosingEventArgs e) => ((FormClosingEventHandler?)Events[s_formClosingEvent])?.Invoke(this, e);

    protected virtual void OnFormClosed(FormClosedEventArgs e)
    {
        Application.OpenForms.Remove(this);
        ((FormClosedEventHandler?)Events[s_formClosedEvent])?.Invoke(this, e);
    }

    [Obsolete("Form.OnClosing has been deprecated. Use Form.OnFormClosing instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void OnClosing(CancelEventArgs e) => ((CancelEventHandler?)Events[s_closingEvent])?.Invoke(this, e);

    [Obsolete("Form.OnClosed has been deprecated. Use Form.OnFormClosed instead.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void OnClosed(EventArgs e) => ((EventHandler?)Events[s_closedEvent])?.Invoke(this, e);

    public event EventHandler? Load
    {
        add => Events.AddHandler(s_loadEvent, value);
        remove => Events.RemoveHandler(s_loadEvent, value);
    }

    public event EventHandler? Shown
    {
        add => Events.AddHandler(s_shownEvent, value);
        remove => Events.RemoveHandler(s_shownEvent, value);
    }

    public event EventHandler? Activated
    {
        add => Events.AddHandler(s_activatedEvent, value);
        remove => Events.RemoveHandler(s_activatedEvent, value);
    }

    public event EventHandler? Deactivate
    {
        add => Events.AddHandler(s_deactivateEvent, value);
        remove => Events.RemoveHandler(s_deactivateEvent, value);
    }

    public event FormClosingEventHandler? FormClosing
    {
        add => Events.AddHandler(s_formClosingEvent, value);
        remove => Events.RemoveHandler(s_formClosingEvent, value);
    }

    public event FormClosedEventHandler? FormClosed
    {
        add => Events.AddHandler(s_formClosedEvent, value);
        remove => Events.RemoveHandler(s_formClosedEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Form.Closing has been deprecated. Use Form.FormClosing instead.")]
    public event CancelEventHandler? Closing
    {
        add => Events.AddHandler(s_closingEvent, value);
        remove => Events.RemoveHandler(s_closingEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Form.Closed has been deprecated. Use Form.FormClosed instead.")]
    public event EventHandler? Closed
    {
        add => Events.AddHandler(s_closedEvent, value);
        remove => Events.RemoveHandler(s_closedEvent, value);
    }

    private static readonly object s_loadEvent = new();
    private static readonly object s_shownEvent = new();
    private static readonly object s_activatedEvent = new();
    private static readonly object s_deactivateEvent = new();
    private static readonly object s_formClosingEvent = new();
    private static readonly object s_formClosedEvent = new();
    private static readonly object s_closingEvent = new();
    private static readonly object s_closedEvent = new();
}
