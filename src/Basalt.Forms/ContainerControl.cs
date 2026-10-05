using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// A control that holds others and manages focus among them: it knows its
/// active control, moves through its children with Tab, and scales them
/// when its font differs from the one they were designed with.
/// </summary>
public partial class ContainerControl : ScrollableControl, IContainerControl
{
    private Control? _activeControl;
    private SizeF _autoScaleDimensions = SizeF.Empty;
    private AutoScaleMode _autoScaleMode = AutoScaleMode.Inherit;
    private AutoValidate _autoValidate = AutoValidate.Inherit;

    public ContainerControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint, false);
        SetStyle(ControlStyles.ContainerControl, true);
    }

    /// <summary>The child (or deeper descendant) that is active in this container.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? ActiveControl
    {
        get => _activeControl;
        set => SetActiveControl(value);
    }

    /// <summary>
    /// The binding context: a container that has none, of its own or from its
    /// parent, creates one, so controls bound on a form share the form's
    /// currency managers without the application creating a context.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override BindingContext? BindingContext
    {
        get
        {
            var context = base.BindingContext;
            if (context is not null) return context;

            context = new BindingContext();
            base.BindingContext = context;

            return context;
        }
        set => base.BindingContext = value;
    }

    /// <summary>The form this container is on, or null; for a form, its parent form.</summary>
    [Browsable(false)]
    public Form? ParentForm => FindForm();

    bool IContainerControl.ActivateControl(Control active) => ActivateControl(active);

    /// <summary>
    /// Makes the control active: Leave and validation on what was active,
    /// Enter on the new one. False when validation kept the old control.
    /// </summary>
    internal bool ActivateControl(Control control)
    {
        if (!Contains(control)) return false;

        if (!MoveEnteredControl(control))
        {
            // Validation failed: the focus goes back to where it was.
            if (_activeControl is { CanFocus: true } previous) previous.SetWindowFocus();

            return false;
        }

        _activeControl = control;
        for (var current = this; current.GetContainerControl() is ContainerControl outer && outer.Contains(current); current = outer)
            outer._activeControl = current;
        UpdateDefaultButton();

        return true;
    }

    /// <summary>Activates the control and then gives it the focus, as setting ActiveControl does.</summary>
    internal void SetActiveControl(Control? value)
    {
        if (value is not null && (value == this || !Contains(value))) throw new ArgumentException("Control cannot be activated: it is not a child of this container.", nameof(value));

        if (value == _activeControl)
        {
            if (value is not null && value.CanFocus && !value.Focused) value.SetWindowFocus();

            return;
        }

        if (value is null)
        {
            _activeControl = null;
            UpdateDefaultButton();
            return;
        }

        if (!ActivateControl(value)) return;

        if (value.CanFocus && !value.Focused) value.SetWindowFocus();
    }

    /// <summary>Forgets an active control that is leaving this container.</summary>
    internal void ForgetActive(Control removed)
    {
        if (_activeControl is { } active && (active == removed || removed.Contains(active)))
        {
            _activeControl = null;
            UpdateDefaultButton();
        }
    }

    protected virtual void UpdateDefaultButton() => ((ContainerControl?)FindForm())?.UpdateDefaultButton();

    /// <summary>Tab and Shift+Tab move through the children; arrows move among them when nothing else wants them.</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & (Keys.Alt | Keys.Control)) == Keys.None)
        {
            var keyCode = keyData & Keys.KeyCode;

            if (keyCode == Keys.Tab && ProcessTabKey((keyData & Keys.Shift) == Keys.None)) return true;

            // Arrows the focused control did not want move among its siblings, as the
            // dialog manager moves among the buttons of a dialog or the radio buttons of a group.
            if (keyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down && ProcessArrowKey(keyCode is Keys.Right or Keys.Down)) return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <summary>
    /// The next control among the active control's siblings, tab stop or not,
    /// wrapping at the ends: the arrow keys of a dialog.
    /// </summary>
    private bool ProcessArrowKey(bool forward)
    {
        var group = _activeControl?.Parent ?? this;

        return group.SelectNextControl(_activeControl, forward, tabStopOnly: false, nested: false, wrap: true);
    }

    /// <summary>Selects the next control in tab order, without wrapping: the form wraps.</summary>
    protected virtual bool ProcessTabKey(bool forward) => SelectNextControl(_activeControl, forward, true, true, false);

    /// <summary>A character that is not for the focused control may be a mnemonic of a child.</summary>
    protected override bool ProcessDialogChar(char charCode)
    {
        if (GetContainerControl() is null && charCode != ' ' && ProcessMnemonic(charCode)) return true;

        return base.ProcessDialogChar(charCode);
    }

    /// <summary>Offers the mnemonic to every visible, enabled descendant, starting after the active control.</summary>
    protected internal override bool ProcessMnemonic(char charCode)
    {
        if (!HasChildren) return false;

        var start = _activeControl;
        var control = start;

        do
        {
            control = GetNextControl(control, true);
            if (control is null) continue;

            if (control.CanSelectForMnemonic() && control.ProcessMnemonic(charCode)) return true;
        }
        while (control != start && control is not null);

        return false;
    }

    protected override void Select(bool directed, bool forward)
    {
        var correctParent = true;

        if (Parent?.GetContainerControl() is { } container)
        {
            container.ActiveControl = this;
            correctParent = container.ActiveControl == this;
        }

        if (directed && correctParent) SelectNextControl(null, forward, true, true, false);
    }

    /// <summary>Validates the active control, as leaving it would.</summary>
    public bool Validate() => Validate(false);

    public bool Validate(bool checkAutoValidate) => _activeControl is null || _activeControl.ValidateThis();

    /// <summary>Validates every selectable child that causes validation; false when one cancelled.</summary>
    public virtual bool ValidateChildren() => ValidateChildren(ValidationConstraints.Selectable);

    public virtual bool ValidateChildren(ValidationConstraints validationConstraints)
    {
        var valid = true;

        foreach (var child in Controls.Snapshot())
        {
            if ((validationConstraints & ValidationConstraints.Selectable) != 0 && !child.CanSelect) continue;
            if ((validationConstraints & ValidationConstraints.Enabled) != 0 && !child.Enabled) continue;
            if ((validationConstraints & ValidationConstraints.Visible) != 0 && !child.Visible) continue;
            if ((validationConstraints & ValidationConstraints.TabStop) != 0 && !child.TabStop) continue;

            if (child.CausesValidation && !child.ValidateThis()) valid = false;
        }

        return valid;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual AutoValidate AutoValidate
    {
        get => _autoValidate;
        set
        {
            if (value == _autoValidate) return;

            _autoValidate = value;
            OnAutoValidateChanged(EventArgs.Empty);
        }
    }

    protected virtual void OnAutoValidateChanged(EventArgs e) => ((EventHandler?)Events[s_autoValidateEvent])?.Invoke(this, e);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public event EventHandler? AutoValidateChanged
    {
        add => Events.AddHandler(s_autoValidateEvent, value);
        remove => Events.RemoveHandler(s_autoValidateEvent, value);
    }

    private static readonly object s_autoValidateEvent = new();

    /// <summary>The dimensions (average character size, or dpi) the container was designed with.</summary>
    [Localizable(true)]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public SizeF AutoScaleDimensions
    {
        get => _autoScaleDimensions;
        set
        {
            if (value.Width < 0 || value.Height < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "AutoScaleDimensions cannot be negative.");

            _autoScaleDimensions = value;
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public AutoScaleMode AutoScaleMode
    {
        get => _autoScaleMode;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(AutoScaleMode));

            _autoScaleMode = value;
        }
    }

    /// <summary>The dimensions now: the average character size of the font, or 96 dpi.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public SizeF CurrentAutoScaleDimensions => EffectiveAutoScaleMode switch
    {
        AutoScaleMode.Font => FontScaling.AverageCharacterSize(Font),
        AutoScaleMode.Dpi => new SizeF(96f, 96f),
        _ => _autoScaleDimensions,
    };

    /// <summary>How much the children must grow: current dimensions over designed ones.</summary>
    protected SizeF AutoScaleFactor
    {
        get
        {
            var current = CurrentAutoScaleDimensions;
            var designed = _autoScaleDimensions;

            if (designed.IsEmpty || current.IsEmpty) return new SizeF(1f, 1f);

            return new SizeF(current.Width / designed.Width, current.Height / designed.Height);
        }
    }

    private AutoScaleMode EffectiveAutoScaleMode
    {
        get
        {
            if (_autoScaleMode != AutoScaleMode.Inherit) return _autoScaleMode;

            return (Parent?.GetContainerControl() as ContainerControl)?.EffectiveAutoScaleMode ?? AutoScaleMode.None;
        }
    }

    /// <summary>Scales the children now by the auto-scale factor, and records the current dimensions as designed.</summary>
    public void PerformAutoScale()
    {
        var factor = AutoScaleFactor;
        if (factor.Width != 1f || factor.Height != 1f) Scale(factor);

        if (EffectiveAutoScaleMode != AutoScaleMode.None && !_autoScaleDimensions.IsEmpty) _autoScaleDimensions = CurrentAutoScaleDimensions;
    }

    /// <summary>The last ResumeLayout of InitializeComponent: the moment WinForms scales a designed container.</summary>
    internal override void OnLayoutResuming(bool performLayout)
    {
        if (_autoScaleMode != AutoScaleMode.None && _autoScaleMode != AutoScaleMode.Inherit && !_autoScaleDimensions.IsEmpty)
        {
            PerformAutoScale();
        }

        base.OnLayoutResuming(performLayout);
    }
}
