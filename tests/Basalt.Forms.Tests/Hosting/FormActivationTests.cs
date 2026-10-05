using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Hosting;

public class FormActivationTests
{
    [AvaloniaFact]
    public void ShowWithoutActivation_is_applied_to_the_native_host_without_focusing_its_canvas()
    {
        using var form = new PassiveForm();
        form.Show();
        try
        {
            var host = Assert.IsType<System.Windows.Forms.Internal.Hosting.HostWindow>(Avalonia.Controls.TopLevel.GetTopLevel(form.Canvas!));
            Assert.False(host.ShowActivated);
            Assert.False(form.Canvas.IsFocused);
        }
        finally { form.Close(); }
    }

    private sealed class PassiveForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
}
