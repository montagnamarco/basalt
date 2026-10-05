using Basalt.Core.Localization;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    private async Task DeleteSelectedControlsAsync(IReadOnlyList<Forms.Control> selectedControls)
    {
        var session = _session!;
        var selection = selectedControls.ToArray();
        var removed = session.DeletedControlNames(selection);
        if (removed.Count == 0) return;
        if (_getCode is null || (!File.Exists(CompanionPath) && _findCode?.Invoke(CompanionPath) is null))
        {
            await session.DeleteControlsAsync(selection);
            return;
        }

        var code = await _getCode(CompanionPath);
        var before = code.Text;
        var model = session.Document;
        var transaction = await Task.Run(() =>
        {
            var after = WinFormsEventHandlers.RemoveControls(before, model.ClassName, removed);
            var remaining = model.Controls.Select(control => control.Name)
                .Concat(model.Components.Select(component => component.Name))
                .Where(name => !removed.Contains(name)).Prepend("Me");
            var bindings = remaining.ToDictionary(name => name,
                name => WinFormsEventHandlers.Read(after, model.ClassName, name),
                StringComparer.OrdinalIgnoreCase);
            return (After: after, Bindings: bindings);
        });

        var originalBindings = new Dictionary<string, IReadOnlyDictionary<string, string>>(_handlers,
            StringComparer.OrdinalIgnoreCase);
        void Restore(string expected, string replacement,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindings)
        {
            var current = ResolveTransactionDocument(code);
            if (current.Text != expected)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            current.Text = replacement;
            _handlers.Clear();
            foreach (var (field, handlers) in bindings) _handlers[field] = handlers;
        }

        await session.DeleteControlsAsync(selection,
            () => Restore(before, transaction.After, transaction.Bindings),
            () => Restore(transaction.After, before, originalBindings),
            () => Restore(before, transaction.After, transaction.Bindings));
    }
}
