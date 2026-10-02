using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>Typing through SendKeys, delivered through the same key messages real input uses.</summary>
public class SendKeysTests
{
    [AvaloniaFact]
    public void Send_types_plain_text_into_the_focused_control()
    {
        var form = new Form();
        var textBox = new TextBox { Parent = form };
        form.Show();
        textBox.Focus();

        SendKeys.Send("hello");

        Assert.Equal("hello", textBox.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void A_braced_key_name_can_be_repeated_with_a_count()
    {
        var form = new Form();
        var textBox = new TextBox { Parent = form, Text = "hello" };
        form.Show();
        textBox.Focus();
        textBox.SelectionStart = textBox.Text.Length;

        SendKeys.Send("{BACKSPACE 3}");

        Assert.Equal("he", textBox.Text);

        form.Close();
    }

    [AvaloniaFact]
    public void A_held_modifier_combines_with_the_key_it_is_held_for()
    {
        var form = new Form();
        var seen = Keys.None;
        form.KeyDown += (_, e) => seen = e.KeyData;
        form.Show();

        SendKeys.Send("^a");

        Assert.Equal(Keys.A | Keys.Control, seen);

        form.Close();
    }

    [AvaloniaFact]
    public void A_held_modifier_is_still_applied_to_punctuation()
    {
        var form = new Form();
        var seen = Keys.None;
        form.KeyPress += (_, _) => seen = Control.ModifierKeys;
        form.Show();

        SendKeys.Send("^!");

        Assert.Equal(Keys.Control, seen);

        form.Close();
    }

    [AvaloniaFact]
    public void A_group_holds_the_modifier_for_every_key_inside_it()
    {
        var form = new Form();
        var seen = new List<Keys>();
        form.KeyDown += (_, e) => seen.Add(e.KeyData);
        form.Show();

        SendKeys.Send("^(ab)");

        Assert.Equal([Keys.A | Keys.Control, Keys.B | Keys.Control], seen);

        form.Close();
    }

    [AvaloniaFact]
    public void Tilde_sends_Enter()
    {
        var form = new Form();
        var raised = false;
        form.KeyDown += (_, e) => raised = e.KeyCode == Keys.Enter;
        form.Show();

        SendKeys.Send("~");

        Assert.True(raised);

        form.Close();
    }
}
