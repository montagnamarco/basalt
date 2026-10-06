using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

public sealed partial class WinFormsDesignerDocument
{
    /// <summary>
    /// Writes "Me.Owner.Property = CType(resources.GetObject("key"), ImageType)",
    /// the way Visual Studio assigns a resource-backed Image or Icon, declaring the
    /// ComponentResourceManager the first time any control needs one.
    /// </summary>
    /// <param name="stripName">
    /// The owning ToolStrip's field name when <paramref name="ownerName"/> is a
    /// ToolStrip item rather than a designer-tracked control: those items are
    /// not in <see cref="Controls"/>, so the statement is written through
    /// <see cref="ToolStripItems"/> instead of the normal control lookup.
    /// </param>
    public string SetImageResource(string ownerName, string propertyName, string resourceKey,
        string imageTypeName, string? stripName = null)
    {
        ValidateIdentifier(propertyName);
        var (document, managerName) = EnsureComponentResourceManager();
        var expression = $"CType({managerName}.GetObject(\"{resourceKey}\"), {imageTypeName})";
        return stripName is null
            ? document.SetProperty(ownerName, propertyName, expression)
            : document.ToolStripItems.SetItemProperty(stripName, ownerName, propertyName, expression);
    }

    /// <summary>Removes the assignment statement an image resource was written as; the resx entry is removed separately.</summary>
    public string RemoveImageResource(string ownerName, string propertyName, string? stripName = null) =>
        stripName is null
            ? SetProperty(ownerName, propertyName, null)
            : ToolStripItems.SetItemProperty(stripName, ownerName, propertyName, null);

    /// <summary>
    /// The existing ComponentResourceManager local Visual Studio declares as the
    /// first statement of InitializeComponent, or a newly inserted one named
    /// "resources" when the method has none yet.
    /// </summary>
    private (WinFormsDesignerDocument Document, string ManagerName) EnsureComponentResourceManager()
    {
        var managers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in _initialize.Statements.OfType<LocalDeclarationStatementSyntax>())
        {
            AddTrustedManagers(declaration, managers);
        }

        if (managers.Count > 0)
        {
            return (this, managers.First());
        }

        const string managerName = "resources";
        var statement = $"Dim {managerName} As System.ComponentModel.ComponentResourceManager = " +
            $"New System.ComponentModel.ComponentResourceManager(GetType({ClassName}))";
        var position = _initialize.Statements.Count > 0
            ? _initialize.Statements[0].FullSpan.Start
            : WinFormsInitializeComponentSyntax.InsertionPoint(_initialize);
        var inserted = _text.WithChanges(new TextChange(
            new TextSpan(position, 0), $"        {statement}{NewLine}")).ToString();
        return (new WinFormsDesignerDocument(inserted), managerName);
    }
}
