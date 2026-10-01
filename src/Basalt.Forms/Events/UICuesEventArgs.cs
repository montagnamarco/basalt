namespace System.Windows.Forms;

/// <summary>Which keyboard cues (focus rectangles, mnemonic underlines) are now shown.</summary>
public class UICuesEventArgs : EventArgs
{
    public UICuesEventArgs(UICues uicues)
    {
        Changed = uicues & UICues.Changed;
        Shown = uicues & UICues.Shown;
    }

    private UICues Shown { get; }

    public UICues Changed { get; }

    public bool ShowFocus => (Shown & UICues.ShowFocus) != 0;

    public bool ShowKeyboard => (Shown & UICues.ShowKeyboard) != 0;

    public bool ChangeFocus => (Changed & UICues.ChangeFocus) != 0;

    public bool ChangeKeyboard => (Changed & UICues.ChangeKeyboard) != 0;
}
