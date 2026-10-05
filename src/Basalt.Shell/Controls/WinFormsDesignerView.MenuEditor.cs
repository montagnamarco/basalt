using Avalonia.Controls;
using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private async Task EditMenuAsync(WinFormsDesignerSession session, Forms.MenuStrip menu, Window owner)
    {
        var original = session.Document;
        var name = session.NameOf(menu);
        var document = await Task.Run(() => new WinFormsMenuDocument(original.Source));
        WinFormsMenuEditor editor;
        try
        {
            editor = new WinFormsMenuEditor(document, name);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsCollectionUnsupported), exception);
        }
        if (!await editor.ShowDialog<bool>(owner))
        {
            return;
        }
        if (!ReferenceEquals(_session, session) || !ReferenceEquals(session.Document, original))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }
        var items = editor.Items;
        RunEditAsync(() => session.ApplyCollectionAsync(current => new WinFormsMenuDocument(current.Source).Set(name, items)));
    }
}
