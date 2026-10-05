using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private EditorDocumentViewModel ResolveTransactionDocument(EditorDocumentViewModel original)
    {
        if (_findCode is null) return original;
        return _findCode(original.FilePath)
            ?? throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
    }
}
