namespace System.Windows.Forms;

/// <summary>How tall one row of a <see cref="TableLayoutPanel"/> is: a fixed height, a share of the content, or a percentage of what is left.</summary>
public class RowStyle : TableLayoutStyle
{
    private float _height;

    public RowStyle()
    {
    }

    public RowStyle(SizeType sizeType)
    {
        SizeType = sizeType;
    }

    public RowStyle(SizeType sizeType, float height)
    {
        SizeType = sizeType;
        Height = height;
    }

    public float Height
    {
        get => _height;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Height must not be negative.");

            _height = value;
            Owner?.PerformLayout();
        }
    }
}
