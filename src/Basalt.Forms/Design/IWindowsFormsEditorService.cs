namespace System.Windows.Forms.Design;

/// <summary>What a UITypeEditor with UITypeEditorEditStyle.DropDown or Modal asks the host for: a control hosted in a drop-down under the property row, or a plain modal dialog.</summary>
public interface IWindowsFormsEditorService
{
    void CloseDropDown();

    void DropDownControl(Control control);

    DialogResult ShowDialog(Form dialog);
}
