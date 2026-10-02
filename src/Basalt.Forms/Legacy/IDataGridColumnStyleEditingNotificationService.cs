namespace System.Windows.Forms;

// Not part of the obsolete DataGrid family: Microsoft ships this interface
// without [Obsolete]. It only exists here because DataGridColumnStyle's
// public signature implements it; kept plain and functional, not a shim.
public interface IDataGridColumnStyleEditingNotificationService
{
    void ColumnStartedEditing(Control editingControl);
}
