using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// One sheet of a <see cref="TabControl"/>: an ordinary panel whose Text is
/// the caption drawn on its tab, shown only while it is the control's
/// <see cref="TabControl.SelectedTab"/>.
/// </summary>
[ToolboxItem(false)]
[DesignTimeVisible(false)]
[DefaultEvent("Click")]
[DefaultProperty("Text")]
public partial class TabPage : Panel
{
    public TabPage()
    {
    }

    public TabPage(string? text)
        : this()
    {
        Text = text;
    }

    /// <summary>The page's picture in its owner's <see cref="TabControl.ImageList"/>, or -1 for none.</summary>
    [Localizable(true)]
    [DefaultValue(-1)]
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (value == _imageIndex) return;

            _imageIndex = value;
            InvalidateOwnerTabStrip();
        }
    }

    /// <summary>The page's picture in its owner's <see cref="TabControl.ImageList"/>, found by key instead of index.</summary>
    [Localizable(true)]
    [DefaultValue("")]
    public string ImageKey
    {
        get => _imageKey;
        set
        {
            value ??= string.Empty;
            if (value == _imageKey) return;

            _imageKey = value;
            InvalidateOwnerTabStrip();
        }
    }

    /// <summary>The text of the tooltip shown over this page's tab.</summary>
    [DefaultValue("")]
    [Localizable(true)]
    public string ToolTipText
    {
        get => _toolTipText;
        set => _toolTipText = value ?? string.Empty;
    }

    /// <summary>Whether the page's background follows the visual style of the tab strip instead of <see cref="Control.BackColor"/>.</summary>
    [DefaultValue(false)]
    public bool UseVisualStyleBackColor { get; set; }

    private int _imageIndex = -1;
    private string _imageKey = string.Empty;
    private string _toolTipText = string.Empty;

    /// <summary>Asks the owning TabControl, if any, to redraw its strip: an image or key changed what a tab shows.</summary>
    private void InvalidateOwnerTabStrip()
    {
        if (Parent is TabControl owner) owner.Invalidate();
    }

    protected override ControlCollection CreateControlsInstance() => new TabPageControlCollection(this);

    protected override void OnPaintBackground(PaintEventArgs e) => base.OnPaintBackground(e);

    /// <summary>Raised by the owning TabControl when this page becomes the selected tab.</summary>
    internal void RaiseEnter(EventArgs e) => OnEnter(e);

    /// <summary>Raised by the owning TabControl when another page becomes selected.</summary>
    internal void RaiseLeave(EventArgs e) => OnLeave(e);

    protected override void OnEnter(EventArgs e) => base.OnEnter(e);

    protected override void OnLeave(EventArgs e) => base.OnLeave(e);

    /// <summary>The nearest TabPage that contains the component, or null when it is in none.</summary>
    public static TabPage GetTabPageOfComponent(object? comp)
    {
        for (var control = comp as Control; control is not null; control = control.Parent)
        {
            if (control is TabPage page) return page;
        }

        return null!;
    }

    public override string ToString() => $"{base.ToString()}, Text: {Text}";

    /// <summary>The children of a TabPage: an ordinary collection, kept as its own type only for designer identity.</summary>
    public class TabPageControlCollection : ControlCollection
    {
        public TabPageControlCollection(TabPage owner)
            : base(owner)
        {
        }

        public override void Add(Control? value) => base.Add(value);
    }
}
