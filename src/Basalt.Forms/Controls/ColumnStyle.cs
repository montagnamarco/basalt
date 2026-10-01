namespace System.Windows.Forms;

/// <summary>How wide one column of a <see cref="TableLayoutPanel"/> is: a fixed width, a share of the content, or a percentage of what is left.</summary>
public class ColumnStyle : TableLayoutStyle
{
    private float _width;

    public ColumnStyle()
    {
    }

    public ColumnStyle(SizeType sizeType)
    {
        SizeType = sizeType;
    }

    public ColumnStyle(SizeType sizeType, float width)
    {
        SizeType = sizeType;
        Width = width;
    }

    public float Width
    {
        get => _width;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Width must not be negative.");

            _width = value;
            Owner?.PerformLayout();
        }
    }
}
