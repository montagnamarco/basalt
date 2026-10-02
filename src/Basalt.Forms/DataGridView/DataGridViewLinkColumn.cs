using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A column of hyperlink cells; LinkColor/VisitedLinkColor/LinkBehavior/TrackVisitedState/UseColumnTextForLinkValue forward to the cell template.</summary>
public class DataGridViewLinkColumn : DataGridViewColumn
{
    public DataGridViewLinkColumn()
        : base(new DataGridViewLinkCell())
    {
    }

    public Color ActiveLinkColor
    {
        get => LinkCellTemplate.ActiveLinkColor;
        set => LinkCellTemplate.ActiveLinkColor = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override DataGridViewCell? CellTemplate
    {
        get => base.CellTemplate;
        set
        {
            if (value is not null and not DataGridViewLinkCell)
            {
                throw new InvalidCastException("CellTemplate must be a DataGridViewLinkCell or a type derived from it.");
            }

            base.CellTemplate = value;
        }
    }

    [DefaultValue(LinkBehavior.SystemDefault)]
    public LinkBehavior LinkBehavior
    {
        get => LinkCellTemplate.LinkBehavior;
        set => LinkCellTemplate.LinkBehavior = value;
    }

    public Color LinkColor
    {
        get => LinkCellTemplate.LinkColor;
        set => LinkCellTemplate.LinkColor = value;
    }

    [DefaultValue(null)]
    public string? Text { get; set; }

    [DefaultValue(true)]
    public bool TrackVisitedState
    {
        get => LinkCellTemplate.TrackVisitedState;
        set => LinkCellTemplate.TrackVisitedState = value;
    }

    [DefaultValue(false)]
    public bool UseColumnTextForLinkValue
    {
        get => LinkCellTemplate.UseColumnTextForLinkValue;
        set => LinkCellTemplate.UseColumnTextForLinkValue = value;
    }

    public Color VisitedLinkColor
    {
        get => LinkCellTemplate.VisitedLinkColor;
        set => LinkCellTemplate.VisitedLinkColor = value;
    }

    private DataGridViewLinkCell LinkCellTemplate =>
        (DataGridViewLinkCell?)CellTemplate ?? throw new InvalidOperationException("CellTemplate cannot be null.");

    public override object Clone()
    {
        var clone = (DataGridViewLinkColumn)base.Clone();
        clone.Text = Text;

        return clone;
    }

    public override string ToString() => $"DataGridViewLinkColumn {{ Name={Name}, Index={Index} }}";
}
