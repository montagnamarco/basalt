using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The colours and border a <see cref="ButtonBase"/> paints when its
/// <see cref="ButtonBase.FlatStyle"/> is <see cref="FlatStyle.Flat"/> or
/// <see cref="FlatStyle.Popup"/>. Each colour defaults to <see cref="Color.Empty"/>,
/// meaning "let the button pick its own colour for that state"; Microsoft's
/// designer never lets code construct one directly, so the constructor stays
/// internal and the owner hands out the single instance it lazily creates.
/// </summary>
public class FlatButtonAppearance
{
    private readonly ButtonBase _owner;
    private Color _borderColor = Color.Empty;
    private int _borderSize = 1;
    private Color _checkedBackColor = Color.Empty;
    private Color _mouseDownBackColor = Color.Empty;
    private Color _mouseOverBackColor = Color.Empty;

    internal FlatButtonAppearance(ButtonBase owner) => _owner = owner;

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            if (_borderColor == value) return;

            _borderColor = value;
            _owner.Invalidate();
        }
    }

    public int BorderSize
    {
        get => _borderSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_borderSize == value) return;

            _borderSize = value;
            _owner.Invalidate();
        }
    }

    public Color CheckedBackColor
    {
        get => _checkedBackColor;
        set
        {
            if (_checkedBackColor == value) return;

            _checkedBackColor = value;
            _owner.Invalidate();
        }
    }

    public Color MouseDownBackColor
    {
        get => _mouseDownBackColor;
        set
        {
            if (_mouseDownBackColor == value) return;

            _mouseDownBackColor = value;
            _owner.Invalidate();
        }
    }

    public Color MouseOverBackColor
    {
        get => _mouseOverBackColor;
        set
        {
            if (_mouseOverBackColor == value) return;

            _mouseOverBackColor = value;
            _owner.Invalidate();
        }
    }
}
