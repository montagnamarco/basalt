using System.Drawing;

namespace System.Windows.Forms.Internal;

/// <summary>Builds the controls for the current page, replacing whatever was there before.</summary>
internal sealed partial class TaskDialogForm
{
    private Rectangle _iconBounds;
    private TaskDialogIcon? _drawnIcon;
    private Rectangle _footnoteIconBounds;
    private TaskDialogIcon? _drawnFootnoteIcon;

    private void BuildContent()
    {
        _suspended = true;
        SuspendLayout();
        Controls.Clear();

        Text = _page.Caption ?? string.Empty;
        MinimizeBox = _page.AllowMinimize;
        MaximizeBox = false;
        RightToLeft = _page.RightToLeftLayout ? RightToLeft.Yes : RightToLeft.No;

        _drawnIcon = _page.Icon is { HasImage: true } icon ? icon : null;
        var textLeft = _drawnIcon is null ? Edge : Edge + IconSize + IconGap;
        var top = Edge;

        if (_drawnIcon is not null) _iconBounds = new Rectangle(Edge, top, IconSize, IconSize);

        top = AddTextRow(_page.Heading, bold: true, textLeft, top);
        top = AddTextRow(_page.Text, bold: false, textLeft, top);

        var contentLeft = Edge;
        top = Math.Max(top, _drawnIcon is null ? top : Edge + IconSize) + RowGap;

        top = AddProgressBar(contentLeft, top);
        top = AddRadioButtons(contentLeft, top);
        top = AddExpandedArea(contentLeft, top, TaskDialogExpanderPosition.AfterText);
        top = AddVerification(contentLeft, top);
        top = AddFootnote(contentLeft, top);
        top = AddExpandedArea(contentLeft, top, TaskDialogExpanderPosition.AfterFootnote);
        top = AddExpanderToggle(contentLeft, top);

        var width = Math.Max(ContentWidth, textLeft + Edge);
        top += RowGap;
        top = AddButtons(width, top);

        ClientSize = new Size(width, top + Edge);

        ResumeLayout(false);
        _suspended = false;
        Invalidate();
    }

    /// <summary>A heading or main text label, word-wrapped to the content width; returns the next free top.</summary>
    private int AddTextRow(string? text, bool bold, int left, int top)
    {
        if (string.IsNullOrEmpty(text)) return top;

        var font = bold ? new Font(Font, FontStyle.Bold) : Font;
        var size = TextRenderer.MeasureText(text, font, new Size(ContentWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var label = new Label { AutoSize = false, Text = text, Font = font, UseMnemonic = false, Location = new Point(left, top), Size = size };
        Controls.Add(label);

        return top + size.Height + (bold ? RowGap / 2 : 0);
    }

    private int AddProgressBar(int left, int top)
    {
        var bar = _page.ProgressBar;
        if (bar is null || bar.State == TaskDialogProgressBarState.None) return top;

        var control = new ProgressBar
        {
            Location = new Point(left, top),
            Size = new Size(ContentWidth, 16),
            Minimum = bar.Minimum,
            Maximum = bar.Maximum,
            Value = Math.Clamp(bar.Value, bar.Minimum, bar.Maximum),
            Style = bar.State is TaskDialogProgressBarState.Marquee or TaskDialogProgressBarState.MarqueePaused
                ? ProgressBarStyle.Marquee
                : ProgressBarStyle.Blocks,
            MarqueeAnimationSpeed = bar.MarqueeSpeed,
        };
        Controls.Add(control);

        return top + control.Height + RowGap;
    }

    private int AddRadioButtons(int left, int top)
    {
        foreach (var radioButton in _page.RadioButtons)
        {
            var size = TextRenderer.MeasureText(radioButton.Text ?? string.Empty, Font);
            var control = new RadioButton
            {
                AutoSize = false,
                Text = radioButton.Text,
                Enabled = radioButton.Enabled,
                Checked = radioButton.Checked,
                Location = new Point(left, top),
                Size = new Size(ContentWidth, Math.Max(size.Height, 18)),
            };
            control.CheckedChanged += (_, _) => radioButton.Checked = control.Checked;
            Controls.Add(control);

            top += control.Height + 2;
        }

        return _page.RadioButtons.Count > 0 ? top + RowGap - 2 : top;
    }

    private int AddVerification(int left, int top)
    {
        var verification = _page.Verification;
        if (verification is null) return top;

        var size = TextRenderer.MeasureText(verification.Text ?? string.Empty, Font);
        var control = new CheckBox
        {
            AutoSize = false,
            Text = verification.Text,
            Checked = verification.Checked,
            Location = new Point(left, top),
            Size = new Size(ContentWidth, Math.Max(size.Height, 18)),
        };
        control.CheckedChanged += (_, _) => verification.Checked = control.Checked;
        Controls.Add(control);

        return top + control.Height + RowGap;
    }

    private int AddFootnote(int left, int top)
    {
        var footnote = _page.Footnote;
        if (footnote is null || string.IsNullOrEmpty(footnote.Text)) return top;

        _drawnFootnoteIcon = footnote.Icon is { HasImage: true } icon ? icon : null;
        var textLeft = _drawnFootnoteIcon is null ? left : left + FootnoteIconSize + 6;

        var size = TextRenderer.MeasureText(footnote.Text, Font, new Size(ContentWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var label = new Label { AutoSize = false, Text = footnote.Text, UseMnemonic = false, Location = new Point(textLeft, top), Size = size };
        Controls.Add(label);

        if (_drawnFootnoteIcon is not null) _footnoteIconBounds = new Rectangle(left, top, FootnoteIconSize, FootnoteIconSize);

        return top + Math.Max(size.Height, _drawnFootnoteIcon is null ? 0 : FootnoteIconSize) + RowGap;
    }

    private int AddExpanderToggle(int left, int top)
    {
        var expander = _page.Expander;
        if (expander is null) return top;

        var text = expander.Expanded
            ? expander.ExpandedButtonText ?? "Hide details"
            : expander.CollapsedButtonText ?? "Show details";

        var size = TextRenderer.MeasureText(text, Font);
        var toggle = new Button { Text = text, Location = new Point(left, top), Size = new Size(size.Width + 16, 23) };
        toggle.Click += (_, _) => expander.Expanded = !expander.Expanded;
        Controls.Add(toggle);

        return top + toggle.Height + RowGap;
    }

    private int AddExpandedArea(int left, int top, TaskDialogExpanderPosition position)
    {
        var expander = _page.Expander;
        if (expander is null || !expander.Expanded || expander.Position != position || string.IsNullOrEmpty(expander.Text)) return top;

        var size = TextRenderer.MeasureText(expander.Text, Font, new Size(ContentWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var label = new Label { AutoSize = false, Text = expander.Text, UseMnemonic = false, Location = new Point(left, top), Size = size };
        Controls.Add(label);

        return top + size.Height + RowGap;
    }
}
