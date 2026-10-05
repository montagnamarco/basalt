using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed partial class WinFormsDesignerDocument
{
    public int FieldPosition(string name)
    {
        return _class.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declarators).SelectMany(declarator => declarator.Names)
            .Single(identifier => identifier.Identifier.ValueText.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Identifier.SpanStart;
    }

    public void ValidateFieldRename(string oldName, string newName)
    {
        if (!SyntaxFacts.IsValidIdentifier(newName))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerRenameMustBeIdentifier), nameof(newName));
        if (DeclaredNames.Any(name => name.Equals(newName, StringComparison.OrdinalIgnoreCase)
            && !name.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(Localizer.Get(StringKeys.WinFormsDesignerRenameAlreadyUsed), nameof(newName));
        _ = FieldPosition(oldName);
    }
}
