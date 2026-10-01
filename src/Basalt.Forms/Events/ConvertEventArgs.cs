namespace System.Windows.Forms;

public class ConvertEventArgs : EventArgs
{
    public ConvertEventArgs(object? value, Type? desiredType)
    {
        Value = value;
        DesiredType = desiredType;
    }

    public Type? DesiredType { get; }

    public object? Value { get; set; }
}
