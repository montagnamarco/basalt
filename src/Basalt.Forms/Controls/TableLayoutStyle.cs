using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>How one column or row of a <see cref="TableLayoutPanel"/> is sized.</summary>
public abstract class TableLayoutStyle
{
    private SizeType _sizeType = SizeType.AutoSize;

    protected TableLayoutStyle()
    {
    }

    [DefaultValue(SizeType.AutoSize)]
    public SizeType SizeType
    {
        get => _sizeType;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(SizeType));

            _sizeType = value;
            Owner?.PerformLayout();
        }
    }

    /// <summary>The panel this style belongs to, so a change asks it to lay out again; null before it is added.</summary>
    internal TableLayoutPanel? Owner { get; set; }
}
