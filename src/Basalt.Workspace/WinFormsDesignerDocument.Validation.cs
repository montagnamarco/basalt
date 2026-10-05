using Basalt.Core.Localization;

namespace Basalt.Workspace;

public sealed partial class WinFormsDesignerDocument
{
    public WinFormsCollectionDocument Collections { get; }

    private void ValidateControlHierarchy()
    {
        var controls = Controls.ToDictionary(control => control.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var control in Controls)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = control;
            while (true)
            {
                if (!visited.Add(current.Name)) throw new FormatException(Localizer.Get(StringKeys.WinFormsDesignerHierarchyCycle));
                if (current.ParentName.Equals("Me", StringComparison.OrdinalIgnoreCase)
                    || !controls.TryGetValue(current.ParentName, out current!)) break;
            }
        }
    }
}
