using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Controls;

public class ControlMessageDeclarationsTests
{
    [Fact]
    public void ReflectedSelectionNotificationRaisesTheExistingListEvent()
    {
        using var list = new MessageListBox();
        var count = 0;
        list.SelectedIndexChanged += (_, _) => count++;
        list.Deliver(0x2111, 1 << 16);
        Assert.Equal(1, count);
        list.Deliver(0x2111, 2 << 16);
        Assert.Equal(1, count);
    }

    [Fact]
    public void FormCollectionUsesBaseParentingAndRejectsTopLevelChildren()
    {
        using var form = new Form();
        Assert.IsType<Form.ControlCollection>(form.Controls);
        using var child = new Button();
        form.Controls.Add(child);
        Assert.Same(form, child.Parent);
        form.Controls.Remove(child);
        Assert.Null(child.Parent);
        using var otherForm = new Form();
        Assert.Throws<ArgumentException>(() => form.Controls.Add(otherForm));
    }

    [AvaloniaFact]
    public void ExceptionDialogShowsTheActualExceptionAndDismisses()
    {
        var error = new InvalidOperationException("Example failure");
        using var dialog = new ThreadExceptionDialog(error);
        Assert.Contains(dialog.Controls.Cast<Control>(), control => control is Label && control.Text == error.Message);
        var details = Assert.Single(dialog.Controls.Cast<Control>().OfType<TextBox>());
        Assert.Equal(error.ToString(), details.Text);
        Assert.True(details.ReadOnly);
        Assert.NotNull(dialog.AcceptButton);
        dialog.Show();
        dialog.AcceptButton!.PerformClick();
        Assert.Equal(DialogResult.OK, dialog.DialogResult);
    }

    private sealed class MessageListBox : ListBox
    {
        public void Deliver(int kind, int parameter)
        {
            var message = Message.Create(IntPtr.Zero, kind, (IntPtr)parameter, IntPtr.Zero);
            WndProc(ref message);
        }
    }
}
