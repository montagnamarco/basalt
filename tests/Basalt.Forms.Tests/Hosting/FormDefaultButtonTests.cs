using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Basalt.Forms.Tests.Hosting;

public class FormDefaultButtonTests
{
    [AvaloniaFact]
    public void Focused_button_replaces_accept_default_and_text_focus_restores_it()
    {
        using var form = new Form();
        var accept = new ObservingButton();
        var focused = new ObservingButton();
        var text = new TextBox();
        form.Controls.Add(accept);
        form.Controls.Add(focused);
        form.Controls.Add(text);
        form.AcceptButton = accept;
        form.Show();
        try
        {
            Assert.True(accept.Default);
            focused.Focus();
            Assert.True(focused.Default);
            Assert.False(accept.Default);
            var clicked = false;
            focused.Click += (_, _) => clicked = true;
            form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.True(clicked);
            text.Focus();
            Assert.False(focused.Default);
            Assert.True(accept.Default);
        }
        finally { form.Close(); }
    }

    private sealed class ObservingButton : Button
    {
        internal bool Default => IsDefault;
    }

    [AvaloniaFact]
    public void Deeply_nested_focus_updates_every_container_and_the_form_default()
    {
        using var form = new Form();
        var outer = new UserControl();
        var inner = new UserControl();
        var button = new ObservingButton();
        inner.Controls.Add(button);
        outer.Controls.Add(inner);
        form.Controls.Add(outer);
        form.Show();
        try
        {
            Assert.True(button.Focus());
            Assert.Same(outer, form.ActiveControl);
            Assert.Same(inner, outer.ActiveControl);
            Assert.Same(button, inner.ActiveControl);
            Assert.True(button.Default);
            inner.Controls.Remove(button);
            Assert.False(button.Default);
        }
        finally { form.Close(); button.Dispose(); }
    }
}
