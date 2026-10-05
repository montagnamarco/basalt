namespace System.Windows.Forms;

/// <summary>What a row and a column share: a header cell, a default style, and the Frozen/ReadOnly/Visible/Selected flags.</summary>
public class DataGridViewBand : DataGridViewElement, ICloneable, IDisposable
{
    private DataGridViewCellStyle? _defaultCellStyle;
    private DataGridViewHeaderCell? _headerCell;
    private bool _frozen;
    private bool _readOnly;
    private bool _selected;
    private bool _visible = true;
    private DataGridViewTriState _resizable = DataGridViewTriState.NotSet;
    private ContextMenuStrip? _contextMenuStrip;

    /// <summary>The index this band had in its collection the last time it was asked; -1 before it is added.</summary>
    internal int IndexCore = -1;

    /// <summary>Not public in Microsoft's own surface: a plain DataGridViewBand is never created directly, only DataGridViewRow/Column.</summary>
    internal DataGridViewBand()
    {
    }

    public virtual DataGridViewCellStyle DefaultCellStyle
    {
        get => _defaultCellStyle ??= new DataGridViewCellStyle();
        set => _defaultCellStyle = value;
    }

    /// <summary>
    /// Band's own backing field; Column and Row each override this to raise their own Changed
    /// event on the owning grid (ColumnContextMenuStripChanged/RowContextMenuStripChanged) - a
    /// plain DataGridViewBand is never instantiated directly, only through those two.
    /// </summary>
    public virtual ContextMenuStrip? ContextMenuStrip
    {
        get => _contextMenuStrip;
        set => _contextMenuStrip = value;
    }

    public Type DefaultHeaderCellType { get; set; } = typeof(DataGridViewHeaderCell);

    public virtual bool Displayed => DataGridView is not null && Visible;

    public virtual bool Frozen
    {
        get => _frozen;
        set => _frozen = value;
    }

    public bool HasDefaultCellStyle => _defaultCellStyle is not null;

    protected DataGridViewHeaderCell HeaderCellCore
    {
        get => _headerCell ??= (DataGridViewHeaderCell)Activator.CreateInstance(DefaultHeaderCellType)!;
        set => _headerCell = value;
    }

    public int Index => IndexCore;

    public virtual DataGridViewCellStyle? InheritedStyle => null;

    protected bool IsRow => this is DataGridViewRow;

    public virtual bool ReadOnly
    {
        get => _readOnly;
        set => _readOnly = value;
    }

    public virtual DataGridViewTriState Resizable
    {
        get => _resizable;
        set => _resizable = value;
    }

    public virtual bool Selected
    {
        get => _selected;
        set => _selected = value;
    }

    public object? Tag { get; set; }

    public virtual bool Visible
    {
        get => _visible;
        set => _visible = value;
    }

    public virtual object Clone()
    {
        var clone = (DataGridViewBand)Activator.CreateInstance(GetType())!;
        clone._frozen = _frozen;
        clone._readOnly = _readOnly;
        clone._visible = _visible;
        clone._resizable = _resizable;
        clone.DefaultHeaderCellType = DefaultHeaderCellType;
        if (_defaultCellStyle is not null) clone._defaultCellStyle = _defaultCellStyle.Clone();

        return clone;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~DataGridViewBand() => Dispose(false);

    protected virtual void Dispose(bool disposing)
    {
    }

    protected override void OnDataGridViewChanged()
    {
    }

    public override string ToString() => $"{GetType().Name}: Index={Index}";
}
