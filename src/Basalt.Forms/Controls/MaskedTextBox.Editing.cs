using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Everything that moves the mask forward: the keys that type, delete, and
/// paste, each one going through <see cref="MaskedTextProvider"/> rather
/// than the inherited editor, which is then just told what to display.
/// </summary>
public partial class MaskedTextBox
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _provider is null) return;

        if (e.KeyCode == Keys.Delete && !e.Shift && !e.Control)
        {
            if (!ReadOnly) DeleteMaskedSelectionOrChar(backwards: false);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Insert && !e.Control && !e.Shift && _insertKeyMode == InsertKeyMode.Default)
        {
            _overwriteMode = !_overwriteMode;
            OnIsOverwriteModeChanged(EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // The shortcuts run the same commands as calls from code, so the two can never differ.
        if ((e.Control && e.KeyCode == Keys.C) || (e.Control && e.KeyCode == Keys.Insert))
        {
            CopyCore();
            e.Handled = true;
            return;
        }

        if ((e.Control && e.KeyCode == Keys.X) || (e.Shift && e.KeyCode == Keys.Delete))
        {
            CutCore();
            e.Handled = true;
            return;
        }

        if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
        {
            PasteCore();
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Z)
        {
            // The inherited editor's one-level undo buffer bypasses the provider entirely: letting it
            // run would leave the two out of step, so the shortcut is swallowed instead of wired up.
            e.Handled = true;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled || _provider is null) return;

        if (ReadOnly)
        {
            e.Handled = true;
            return;
        }

        if (e.KeyChar == '\b')
        {
            DeleteMaskedSelectionOrChar(backwards: true);
            e.Handled = true;
            return;
        }

        // Other control characters ('\r', '\t', Ctrl+letter) type no mask character.
        if (char.IsControl(e.KeyChar)) return;

        ReplaceSelectionWithMaskedInput(e.KeyChar.ToString());
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_provider is not null && _hidePromptOnLeave) SyncDisplayText(SelectionStart);
    }

    protected override void OnValidating(CancelEventArgs e)
    {
        // The control's own checks run before the event: a Validating subscriber sees the final
        // Cancel state base.OnValidating raises the event with, and can still clear it.
        if (_provider is not null)
        {
            if (_hidePromptOnLeave) SyncDisplayText(SelectionStart, showPrompt: false);

            if (_validatingType is not null)
            {
                var args = RunTypeValidation();
                if (!args.IsValidInput || args.Cancel) e.Cancel = true;
            }
        }

        base.OnValidating(e);
    }

    /// <summary>Replaces the current selection (or inserts/overwrites at the caret) through the provider, one character at a time so <see cref="RejectInputOnFirstFailure"/> can stop early or let the rest through.</summary>
    private void ReplaceSelectionWithMaskedInput(string input)
    {
        if (_provider is null) return;

        var caret = SelectionStart;
        if (SelectionLength > 0) _provider.RemoveAt(caret, caret + SelectionLength - 1, out caret, out _);

        foreach (var character in input)
        {
            var ok = IsOverwriteMode
                ? _provider.Replace(character, caret, out var testPosition, out var hint)
                : _provider.InsertAt(character, caret, out testPosition, out hint);

            if (ok)
            {
                caret = testPosition + 1;
                continue;
            }

            RaiseMaskInputRejected(testPosition, hint);
            if (_rejectInputOnFirstFailure) break;
        }

        SyncDisplayText(caret);
    }

    /// <summary>Removes the selection, or one character before (Backspace) or at (Delete) the caret.</summary>
    private void DeleteMaskedSelectionOrChar(bool backwards)
    {
        if (_provider is null) return;

        if (SelectionLength > 0)
        {
            _provider.RemoveAt(SelectionStart, SelectionStart + SelectionLength - 1, out var afterRemove, out _);
            SyncDisplayText(afterRemove);
            return;
        }

        var position = backwards ? SelectionStart - 1 : SelectionStart;
        if (position < 0) return;

        if (!_provider.RemoveAt(position, position, out var testPosition, out var hint)) RaiseMaskInputRejected(testPosition, hint);

        SyncDisplayText(position);
    }

    /// <summary>Puts the selected range, formatted per <see cref="CutCopyMaskFormat"/>, on the clipboard.</summary>
    // Code and menus that call Cut, Copy, Paste, Undo or AppendText edit through the provider too.

    internal override bool CanUndoCore => false;

    internal override void UndoCore()
    {
        // Undo is not supported: the provider keeps no history to return to.
    }

    internal override void CopyCore() => CopySelectionToClipboard();

    internal override void CutCore()
    {
        if (_provider is null)
        {
            base.CutCore();
            return;
        }

        if (SelectionLength == 0 || ReadOnly || IsPassword) return;

        CopySelectionToClipboard();
        DeleteMaskedSelectionOrChar(backwards: false);
    }

    internal override void PasteCore()
    {
        if (_provider is null)
        {
            base.PasteCore();
            return;
        }

        if (!ReadOnly) ReplaceSelectionWithMaskedInput(Clipboard.GetText());
    }

    internal override void AppendTextCore(string text)
    {
        if (_provider is null)
        {
            base.AppendTextCore(text);
            return;
        }

        // Appending continues after the last character typed, not after the trailing prompts.
        Select(_provider.LastAssignedPosition + 1, 0);
        ReplaceSelectionWithMaskedInput(text);
    }

    private void CopySelectionToClipboard()
    {
        // A password box never gives its characters away, as TextBoxBase.Copy.
        if (_provider is null || SelectionLength == 0 || IsPassword) return;

        var includePrompt = (_cutCopyMaskFormat & MaskFormat.IncludePrompt) != 0;
        var includeLiterals = (_cutCopyMaskFormat & MaskFormat.IncludeLiterals) != 0;
        var text = _provider.ToString(includePrompt, includeLiterals, SelectionStart, SelectionLength);

        if (text.Length > 0) Clipboard.SetText(text);
    }

    /// <summary>Pushes the provider's current content into the inherited editor, which is what is actually painted, and places the caret.</summary>
    private void SyncDisplayText(int caret, bool showPrompt = true)
    {
        if (_provider is null)
        {
            base.Text = string.Empty;
            return;
        }

        base.Text = _provider.ToString(showPrompt, true);
        Select(Math.Clamp(caret, 0, base.Text.Length), 0);
    }

    private string FormatText(MaskFormat format)
    {
        if (_provider is null) return base.Text;

        var includePrompt = (format & MaskFormat.IncludePrompt) != 0;
        var includeLiterals = (format & MaskFormat.IncludeLiterals) != 0;

        return _provider.ToString(includePrompt, includeLiterals);
    }

    private void SetFullText(string? value)
    {
        value ??= string.Empty;

        if (_provider is null)
        {
            base.Text = value;
            return;
        }

        if (!_provider.Set(value, out var testPosition, out var hint)) RaiseMaskInputRejected(testPosition, hint);

        SyncDisplayText(0);
    }

    /// <summary>A mask change starts the provider over: positions and literals may no longer line up with the old content.</summary>
    private void RebuildProvider()
    {
        if (_mask.Length == 0)
        {
            _provider = null;
            base.Text = string.Empty;
            return;
        }

        _provider = CreateProvider();
        SyncDisplayText(0);
    }

    /// <summary>Culture, AsciiOnly and AllowPromptAsInput can only be set through the provider's constructor: this rebuilds it with the same mask, replaying the raw characters the user had typed so far.</summary>
    private void RebuildProviderPreservingContent()
    {
        if (_provider is null) return;

        var rawInput = _provider.ToString(false, false);
        var replacement = CreateProvider();
        replacement.Set(rawInput, out _, out _);

        _provider = replacement;
        SyncDisplayText(0);
    }

    private MaskedTextProvider CreateProvider()
    {
        // MaskedTextProvider's six-argument constructor orders its two bools as (allowPromptAsInput, ..., restrictToAscii),
        // not (restrictToAscii, ..., allowPromptAsInput): verified against the BCL type, since its surface is undocumented here.
        var provider = new MaskedTextProvider(_mask, _culture, _allowPromptAsInput, _promptChar, _passwordChar, _asciiOnly)
        {
            ResetOnPrompt = _resetOnPrompt,
            ResetOnSpace = _resetOnSpace,
            SkipLiterals = _skipLiterals,
        };

        return provider;
    }

    private void RaiseMaskInputRejected(int position, MaskedTextResultHint hint) => MaskInputRejected?.Invoke(this, new MaskInputRejectedEventArgs(position, hint));

    /// <summary>Converts <see cref="Text"/> to <see cref="ValidatingType"/>, if any, and raises <see cref="TypeValidationCompleted"/> with the result.</summary>
    private TypeValidationEventArgs RunTypeValidation()
    {
        bool isValid;
        object? returnValue;
        string? message = null;

        if (_validatingType is null)
        {
            isValid = true;
            returnValue = Text;
        }
        else if (_provider is not null && !_provider.MaskCompleted)
        {
            // An unfilled mask cannot be a valid instance of the target type, whatever the formatted Text looks like.
            isValid = false;
            returnValue = null;
            message = "The input does not fill the mask.";
        }
        else
        {
            try
            {
                returnValue = Convert.ChangeType(Text, _validatingType, _formatProvider ?? _culture);
                isValid = true;
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                returnValue = null;
                isValid = false;
                message = exception.Message;
            }
        }

        var args = new TypeValidationEventArgs(_validatingType, isValid, returnValue, message);
        TypeValidationCompleted?.Invoke(this, args);

        return args;
    }
}
