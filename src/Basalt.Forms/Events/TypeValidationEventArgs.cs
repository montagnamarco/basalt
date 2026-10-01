namespace System.Windows.Forms;

public class TypeValidationEventArgs : EventArgs
{
    public TypeValidationEventArgs(Type? validatingType, bool isValidInput, object? returnValue, string? message)
    {
        ValidatingType = validatingType;
        IsValidInput = isValidInput;
        ReturnValue = returnValue;
        Message = message;
    }

    public bool Cancel { get; set; }

    public bool IsValidInput { get; }

    public string? Message { get; }

    public object? ReturnValue { get; }

    public Type? ValidatingType { get; }
}
