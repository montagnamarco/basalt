using System.ComponentModel;

namespace System.Windows.Forms;

public partial class ScrollableControl
{
    /// <summary>
    /// The legacy .NET 1.1 way to set <see cref="Control.Padding"/> one edge
    /// (or all four) at a time; a fresh one is handed out on every read, so it
    /// is always the control's current padding, never a stale copy.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [TypeConverter(typeof(DockPaddingEdgesConverter))]
    public DockPaddingEdges DockPadding => new(this);

    public class DockPaddingEdges : ICloneable
    {
        private readonly ScrollableControl? _owner;
        private int _left;
        private int _top;
        private int _right;
        private int _bottom;

        internal DockPaddingEdges(ScrollableControl owner)
        {
            _owner = owner;

            var padding = owner.Padding;
            _left = padding.Left;
            _top = padding.Top;
            _right = padding.Right;
            _bottom = padding.Bottom;
        }

        private DockPaddingEdges(int left, int top, int right, int bottom)
        {
            _left = left;
            _top = top;
            _right = right;
            _bottom = bottom;
        }

        /// <summary>Setting it gives every edge the same padding; reading it answers the left edge's.</summary>
        public int All
        {
            get => _left;
            set
            {
                _left = _top = _right = _bottom = value;
                Apply();
            }
        }

        public int Left
        {
            get => _left;
            set
            {
                _left = value;
                Apply();
            }
        }

        public int Top
        {
            get => _top;
            set
            {
                _top = value;
                Apply();
            }
        }

        public int Right
        {
            get => _right;
            set
            {
                _right = value;
                Apply();
            }
        }

        public int Bottom
        {
            get => _bottom;
            set
            {
                _bottom = value;
                Apply();
            }
        }

        private void Apply()
        {
            if (_owner is { } owner) owner.Padding = new Padding(_left, _top, _right, _bottom);
        }

        /// <summary>A detached copy: its own setters change nothing on the control that made the original.</summary>
        object ICloneable.Clone() => new DockPaddingEdges(_left, _top, _right, _bottom);

        public override bool Equals(object? other) =>
            other is DockPaddingEdges edges && edges._left == _left && edges._top == _top && edges._right == _right && edges._bottom == _bottom;

        public override int GetHashCode() => HashCode.Combine(_left, _top, _right, _bottom);

        public override string ToString() => $"{{Left={_left},Top={_top},Right={_right},Bottom={_bottom}}}";
    }

    /// <summary>Shows All, Left, Top, Right and Bottom as sub-properties of DockPadding in a property grid.</summary>
    public class DockPaddingEdgesConverter : TypeConverter
    {
        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(DockPaddingEdges), attributes);
    }
}
