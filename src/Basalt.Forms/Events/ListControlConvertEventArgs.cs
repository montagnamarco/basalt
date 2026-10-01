namespace System.Windows.Forms;

public class ListControlConvertEventArgs : ConvertEventArgs
{
    public ListControlConvertEventArgs(object? value, Type? desiredType, object? listItem)
        : base(value, desiredType)
    {
        ListItem = listItem;
    }

    public object? ListItem { get; }
}
