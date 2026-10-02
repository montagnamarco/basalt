namespace System.Windows.Forms;

/// <summary>
/// Carries a drag's data and position to DragEnter, DragOver and DragDrop.
/// <see cref="X"/> and <see cref="Y"/> are screen coordinates, as the pointer
/// may be over a different control than the one being dragged.
/// </summary>
public class DragEventArgs : EventArgs
{
    public DragEventArgs(IDataObject? data, int keyState, int x, int y, DragDropEffects allowedEffect, DragDropEffects effect)
    {
        Data = data;
        KeyState = keyState;
        X = x;
        Y = y;
        AllowedEffect = allowedEffect;
        Effect = effect;
    }

    public DragEventArgs(
        IDataObject? data,
        int keyState,
        int x,
        int y,
        DragDropEffects allowedEffect,
        DragDropEffects effect,
        DropImageType dropImageType,
        string? message,
        string? messageReplacementToken)
        : this(data, keyState, x, y, allowedEffect, effect)
    {
        DropImageType = dropImageType;
        Message = message;
        MessageReplacementToken = messageReplacementToken;
    }

    public IDataObject? Data { get; }

    public int KeyState { get; }

    public int X { get; }

    public int Y { get; }

    public DragDropEffects AllowedEffect { get; }

    /// <summary>What the target chooses to do; the source's GiveFeedback and the final DoDragDrop result read this.</summary>
    public DragDropEffects Effect { get; set; }

    public DropImageType DropImageType { get; set; }

    public string? Message { get; set; }

    public string? MessageReplacementToken { get; set; }
}
