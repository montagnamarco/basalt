using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    /// <summary>
    /// Wires the session's one-time resx creation hook: the first Image or Icon
    /// a form picks up must land in a brand-new "Form1.resx" next to the
    /// "Form1.vb" it belongs to, exactly where Visual Studio would have put it.
    /// </summary>
    private void BindImageResourceCreation(WinFormsDesignerSession session)
    {
        session.CreateResourceDocument = async () =>
        {
            if (_createCode is null)
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
            }
            var path = Path.ChangeExtension(CompanionPath, ".resx");
            var buffer = await _createCode(path, WinFormsResourceDocumentTemplate.Empty);
            if (_disposed || !ReferenceEquals(session, _session))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }
            TrackResourceBuffer(buffer);
            return buffer.Text;
        };
    }
}
