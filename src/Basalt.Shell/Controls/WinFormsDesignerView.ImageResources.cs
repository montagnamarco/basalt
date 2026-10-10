using Basalt.Core.Localization;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    /// <summary>
    /// Refuses an image edit before its dialog opens while a culture is shown. The dialog lets the user pick a file
    /// and import it into the project's resources, so refusing only when the session applies the edit would import a
    /// file for nothing. Returns true when the refusal was shown and the dialog must not open.
    /// </summary>
    private bool RefuseImageEditBeforeDialog(WinFormsDesignerSession session)
    {
        if (session.ImageEditRefusal is not { } refusal) return false;
        ShowError(new InvalidOperationException(refusal));
        return true;
    }

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
            // The session asks for a new resx only when it has none readable. A file that exists but did not parse
            // keeps its values: writing the empty template over it would destroy them, so the edit is refused instead.
            if (ResourceFileExists(path))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerResourcesNotReplaced));
            }
            var buffer = await _createCode(path, WinFormsResourceDocumentTemplate.Empty);
            if (_disposed || !ReferenceEquals(session, _session))
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }
            TrackResourceBuffer(buffer);
            return buffer.Text;
        };
    }

    /// <summary>
    /// Whether the form's neutral .resx exists, open in the host or on disk. The buffer lookup mirrors
    /// <c>BindResourceEditing</c>, so the answer is the same one the host gives the resource edits.
    /// </summary>
    private bool ResourceFileExists(string path) =>
        (_findCode is null ? _resourceBuffer : _findCode(path)) is not null || File.Exists(path);
}
