using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerSession
{
    /// <summary>
    /// Sets a Button/PictureBox/Form Image, BackgroundImage, InitialImage, ErrorImage
    /// or Icon the way Visual Studio does: a resx entry holding the raw file bytes,
    /// and an InitializeComponent line reading it back through a ComponentResourceManager.
    /// One undo step covers both files, matching every other resource edit.
    /// </summary>
    internal Task SetImageResourcePropertyAsync(Forms.Control control, string property, byte[] bytes, bool isIcon) =>
        SetImageResourceCoreAsync(NameOf(control), property, null, bytes, isIcon);

    /// <summary>Removes both the resx entry and the InitializeComponent statement it is read from.</summary>
    internal Task ClearImageResourcePropertyAsync(Forms.Control control, string property) =>
        ClearImageResourceCoreAsync(NameOf(control), property, null);

    /// <summary>The ToolStrip item equivalent: items are not in <see cref="Controls"/>, so the owning strip is given separately.</summary>
    internal Task SetToolStripItemImageResourceAsync(string stripName, string itemName, string property, byte[] bytes, bool isIcon) =>
        SetImageResourceCoreAsync(itemName, property, stripName, bytes, isIcon, isMenu: false);

    internal Task ClearToolStripItemImageResourceAsync(string stripName, string itemName, string property) =>
        ClearImageResourceCoreAsync(itemName, property, stripName, isMenu: false);

    /// <summary>The MenuStrip item equivalent, nested through <see cref="WinFormsDesignerDocument.Menus"/> rather
    /// than <see cref="WinFormsDesignerDocument.ToolStripItems"/>'s flat collection.</summary>
    internal Task SetMenuItemImageResourceAsync(string stripName, string itemName, string property, byte[] bytes, bool isIcon) =>
        SetImageResourceCoreAsync(itemName, property, stripName, bytes, isIcon, isMenu: true);

    internal Task ClearMenuItemImageResourceAsync(string stripName, string itemName, string property) =>
        ClearImageResourceCoreAsync(itemName, property, stripName, isMenu: true);

    /// <summary>
    /// Creates the resx a form does not have yet, the way Visual Studio does the
    /// first time a picture property is set: it writes the header and returns the
    /// new, still-empty document. Null when the host offers no way to create files.
    /// </summary>
    internal Func<Task<string>>? CreateResourceDocument { get; set; }

    /// <summary>
    /// Sets a control's Image/BackgroundImage/Icon property to a My Project resource the way
    /// Visual Studio's "Project resource file" choice does: a plain
    /// "Me.Owner.Property = Global.RootNamespace.My.Resources.Resources.Name" assignment, with
    /// no resx entry on the form itself. A resx entry the property already had (the user
    /// switching away from a Local resource) is removed in the same commit, one undo step.
    ///
    /// Undo note: Resources.resx, Resources.Designer.vb and the imported file under the
    /// project's own Resources folder (written by
    /// <see cref="Basalt.Workspace.WinFormsProjectResources.Import"/> before this method ever
    /// runs) are not part of that undo step and are not reverted by it, matching Visual Studio:
    /// "Edit > Undo" there reverts the statement that used an imported resource, never the
    /// import itself.
    /// </summary>
    internal Task SetProjectImageResourcePropertyAsync(Forms.Control control, string property, string rootNamespace, string resourceName) =>
        SetProjectImageResourceCoreAsync(NameOf(control), property, null, rootNamespace, resourceName);

    /// <summary>The ToolStrip item equivalent of <see cref="SetProjectImageResourcePropertyAsync"/>.</summary>
    internal Task SetToolStripItemProjectImageResourceAsync(string stripName, string itemName, string property,
        string rootNamespace, string resourceName) =>
        SetProjectImageResourceCoreAsync(itemName, property, stripName, rootNamespace, resourceName, isMenu: false);

    /// <summary>The MenuStrip item equivalent of <see cref="SetProjectImageResourcePropertyAsync"/>.</summary>
    internal Task SetMenuItemProjectImageResourceAsync(string stripName, string itemName, string property,
        string rootNamespace, string resourceName) =>
        SetProjectImageResourceCoreAsync(itemName, property, stripName, rootNamespace, resourceName, isMenu: true);

    private async Task SetProjectImageResourceCoreAsync(string ownerName, string property, string? stripName,
        string rootNamespace, string resourceName, bool isMenu = false)
    {
        var original = Document;
        var beforeResources = _resources;
        var key = ResourceKeyFor(ownerName, property);
        // An explicit, empty <RootNamespace> (see WinFormsProjectResources.RootNamespace) drops
        // out of the qualified name entirely rather than leaving a stray "Global..My.Resources":
        // Visual Basic treats that project as having no root namespace at all.
        var expression = string.IsNullOrEmpty(rootNamespace)
            ? $"Global.My.Resources.Resources.{resourceName}"
            : $"Global.{rootNamespace}.My.Resources.Resources.{resourceName}";

        if (key is null || beforeResources is null)
        {
            // Nothing stored as a local resource to remove: a plain property assignment.
            await ApplyAsync(document => stripName is null
                ? document.SetProperty(ownerName, property, expression)
                : isMenu
                    ? document.Menus.SetItemProperty(stripName, ownerName, property, expression)
                    : document.ToolStripItems.SetItemProperty(stripName, ownerName, property, expression));
            return;
        }

        if (PrepareResourceEdit is not null)
        {
            await PrepareResourceEdit(beforeResources.Source);
        }
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, _resources))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }

        var edited = await Task.Run(() =>
        {
            var resources = new WinFormsResourceDocument(beforeResources.Remove(key));
            var document = new WinFormsDesignerDocument(stripName is null
                ? original.SetProperty(ownerName, property, expression)
                : isMenu
                    ? original.Menus.SetItemProperty(stripName, ownerName, property, expression)
                    : original.ToolStripItems.SetItemProperty(stripName, ownerName, property, expression));
            return (Document: document, Resources: resources);
        });

        await CommitDocumentAndResourcesAsync(original, beforeResources, edited.Document, edited.Resources);
    }

    private async Task SetImageResourceCoreAsync(string ownerName, string property, string? stripName, byte[] bytes,
        bool isIcon, bool isMenu = false)
    {
        var original = Document;
        var beforeResources = _resources;
        if (beforeResources is null)
        {
            beforeResources = await CreateResourcesAsync();
        }
        else if (PrepareResourceEdit is not null)
        {
            await PrepareResourceEdit(beforeResources.Source);
        }
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, _resources))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }

        var edited = await Task.Run(() =>
        {
            // The form itself keys its own resources as "$this", the way Visual
            // Studio writes Form.Icon; every other owner uses its field name.
            var prefix = ownerName.Equals("Me", StringComparison.OrdinalIgnoreCase) ? "$this" : ownerName;
            var key = ResourceKeyFor(ownerName, property) ?? $"{prefix}.{property}";
            var (value, typeName, mimeType) = WinFormsImageResources.Encode(bytes, isIcon);
            var resources = new WinFormsResourceDocument(beforeResources.SetValue(key, value, typeName, mimeType));
            var imageTypeName = isIcon ? "System.Drawing.Icon" : "System.Drawing.Image";
            var document = new WinFormsDesignerDocument(
                original.SetImageResource(ownerName, property, key, imageTypeName, stripName, isMenu));
            return (Document: document, Resources: resources);
        });

        await CommitDocumentAndResourcesAsync(original, beforeResources, edited.Document, edited.Resources);
    }

    /// <summary>
    /// Creating the file is not itself an undoable step, the same way Visual
    /// Studio never offers to undo "the project gained a .resx": the resx stays
    /// on disk even if the image assignment that prompted it is undone.
    /// </summary>
    private async Task<WinFormsResourceDocument> CreateResourcesAsync()
    {
        if (CreateResourceDocument is null)
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
        }
        var created = await CreateResourceDocument();
        var resources = new WinFormsResourceDocument(created);
        _resources = resources;
        return resources;
    }

    private async Task ClearImageResourceCoreAsync(string ownerName, string property, string? stripName, bool isMenu = false)
    {
        var original = Document;
        var beforeResources = _resources;
        var key = ResourceKeyFor(ownerName, property);
        if (beforeResources is null || key is null)
        {
            // Nothing stored as a resource: only a bare assignment, if any, to remove.
            var cleared = new WinFormsDesignerDocument(original.RemoveImageResource(ownerName, property, stripName, isMenu));
            if (cleared.Source == original.Source) return;
            await ApplyAsync(_ => cleared.Source);
            return;
        }

        if (PrepareResourceEdit is not null)
        {
            await PrepareResourceEdit(beforeResources.Source);
        }
        if (!ReferenceEquals(original, Document) || !ReferenceEquals(beforeResources, _resources))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }

        var edited = await Task.Run(() =>
        {
            var resources = new WinFormsResourceDocument(beforeResources.Remove(key));
            var document = new WinFormsDesignerDocument(original.RemoveImageResource(ownerName, property, stripName, isMenu));
            return (Document: document, Resources: resources);
        });

        await CommitDocumentAndResourcesAsync(original, beforeResources, edited.Document, edited.Resources);
    }

    /// <summary>
    /// The atomic resx-plus-code commit every other resource edit uses
    /// (<see cref="ApplyPropertyChangesAsync"/>), duplicated rather than shared so this
    /// file never has to change that already-tested method's signature.
    /// </summary>
    private async Task CommitDocumentAndResourcesAsync(WinFormsDesignerDocument originalDocument,
        WinFormsResourceDocument? originalResources, WinFormsDesignerDocument editedDocument, WinFormsResourceDocument? editedResources)
    {
        if (!ReferenceEquals(originalDocument, Document) || !ReferenceEquals(originalResources, _resources))
        {
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        }
        var resourceChanged = originalResources?.Source != editedResources?.Source;
        if (!resourceChanged && editedDocument.Source == originalDocument.Source) return;

        void RestoreResources(WinFormsResourceDocument? expected, WinFormsResourceDocument? replacement)
        {
            if (_resources?.Source != expected?.Source)
            {
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            }
            if (expected is not null && replacement is not null)
            {
                CommitResourceSource?.Invoke(expected.Source, replacement.Source);
            }
            _resources = replacement;
        }

        await ApplyAsync(_ => editedDocument.Source,
            resourceChanged ? () => RestoreResources(originalResources, editedResources) : null,
            resourceChanged ? () => RestoreResources(editedResources, originalResources) : null,
            resourceChanged ? () => RestoreResources(originalResources, editedResources) : null,
            commitUnchangedSource: resourceChanged,
            undoPrepare: resourceChanged ? () => PrepareResourceEdit?.Invoke(editedResources!.Source) ?? Task.CompletedTask : null,
            redoPrepare: resourceChanged ? () => PrepareResourceEdit?.Invoke(originalResources!.Source) ?? Task.CompletedTask : null);
    }
}
