using System.Drawing;

namespace System.Windows.Forms.Internal;

/// <summary>Lays out the page's buttons: a row of ordinary buttons, or a stack of command links.</summary>
internal sealed partial class TaskDialogForm
{
    private Button? _implicitCancelButton;

    private int AddButtons(int formWidth, int top)
    {
        var buttons = _page.Buttons.Where(b => b.Visible).ToList();
        if (_page.Buttons.OfType<TaskDialogCommandLinkButton>().Any()) return AddCommandLinks(buttons, top);

        return AddButtonRow(buttons, formWidth, top);
    }

    private int AddCommandLinks(List<TaskDialogButton> buttons, int top)
    {
        Button? defaultControl = null;
        Button? cancelControl = null;

        foreach (var button in buttons)
        {
            var link = (TaskDialogCommandLinkButton)button;
            var text = string.IsNullOrEmpty(link.DescriptionText) ? link.Text : $"{link.Text}\n{link.DescriptionText}";
            var control = new Button
            {
                Text = text,
                Enabled = link.Enabled,
                Location = new Point(Edge, top),
                Size = new Size(ContentWidth, string.IsNullOrEmpty(link.DescriptionText) ? 30 : 45),
            };
            control.Click += (_, _) => ButtonClicked(link);
            Controls.Add(control);

            // Standard buttons (TaskDialogButton.OK and friends) are a new instance on every
            // access, so DefaultButton must be matched by kind (Equals), not by reference.
            if (button.Equals(_page.DefaultButton)) defaultControl = control;
            if (button.Equals(TaskDialogButton.Cancel)) cancelControl = control;

            top += control.Height + 4;
        }

        FinishButtons(defaultControl, cancelControl, buttons);
        return top;
    }

    private int AddButtonRow(List<TaskDialogButton> buttons, int formWidth, int top)
    {
        var controls = new List<Button>();
        Button? defaultControl = null;
        Button? cancelControl = null;

        foreach (var button in buttons)
        {
            var size = TextRenderer.MeasureText(button.Text ?? string.Empty, Font);
            var control = new Button
            {
                Text = button.Text,
                Enabled = button.Enabled,
                Size = new Size(Math.Max(size.Width + 20, 75), 23),
            };
            control.Click += (_, _) => ButtonClicked(button);
            controls.Add(control);

            // Standard buttons (TaskDialogButton.OK and friends) are a new instance on every
            // access, so DefaultButton must be matched by kind (Equals), not by reference.
            if (button.Equals(_page.DefaultButton)) defaultControl = control;
            if (button.Equals(TaskDialogButton.Cancel)) cancelControl = control;
        }

        var rowWidth = controls.Sum(c => c.Width) + Math.Max(0, controls.Count - 1) * ButtonGap;
        var left = Math.Max(Edge, formWidth - Edge - rowWidth);

        foreach (var control in controls)
        {
            control.Location = new Point(left, top);
            Controls.Add(control);
            left += control.Width + ButtonGap;
        }

        FinishButtons(defaultControl, cancelControl, buttons);
        return top + 23;
    }

    /// <summary>Wires the default and cancel buttons, adding a hidden cancel action when the page allows it but has no Cancel button.</summary>
    private void FinishButtons(Button? defaultControl, Button? cancelControl, List<TaskDialogButton> buttons)
    {
        AcceptButton = defaultControl;
        ActiveControl = defaultControl;

        if (cancelControl is not null)
        {
            CancelButton = cancelControl;
        }
        else if (_page.AllowCancel)
        {
            _implicitCancelButton = new Button();
            _implicitCancelButton.Click += (_, _) => { ClickedButton = TaskDialogButton.Cancel; Close(); };
            CancelButton = _implicitCancelButton;
        }
        else
        {
            CancelButton = null;
        }

        ControlBox = cancelControl is not null || _page.AllowCancel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_drawnIcon is not null) _drawnIcon.Draw(e.Graphics, _iconBounds);
        if (_drawnFootnoteIcon is not null) _drawnFootnoteIcon.Draw(e.Graphics, _footnoteIconBounds);

        base.OnPaint(e);
    }
}
