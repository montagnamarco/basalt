using System.ComponentModel;
using System.Drawing;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>
/// Controls and components the user's own project defines, and the boundary that keeps their
/// failures out of the IDE.
/// </summary>
internal sealed partial class WinFormsDesignerSession
{
    private readonly List<string> _userCodeFailures = [];

    // Every failure line already handed out since the design context last changed, so a control that
    // fails again on each edit is reported once and not on every rebuild of the preview. A build clears
    // the Output panel, so a line handed out before a build is handed out again after it.
    private readonly HashSet<string> _reportedFailures = new(StringComparer.Ordinal);
    private readonly List<string> _unreportedFailures = [];

    /// <summary>
    /// Failures in the project's own code found while building the preview, one line each. Empty when
    /// every project control was created and drawn.
    /// </summary>
    public IReadOnlyList<string> UserCodeFailures => _userCodeFailures;

    /// <summary>
    /// The failures found since the last call, one line each, ready for the Output panel. Each line is
    /// returned once for a design context, so the Output panel shows a failure once however often the
    /// preview is rebuilt.
    /// </summary>
    public IReadOnlyList<string> TakeUnreportedUserCodeFailures()
    {
        var taken = _unreportedFailures.ToArray();
        _unreportedFailures.Clear();
        return taken;
    }

    /// <summary>
    /// Switches the preview to the assemblies of a newer build. The preview is rebuilt on the
    /// new context before this returns, so the caller can unload the previous one afterwards:
    /// the controls that came from it are disposed by then.
    /// </summary>
    /// <remarks>
    /// A rebuild on the new context can throw, because project code runs while the preview is built (a
    /// control's Controls.Add, or a type that cannot load). The new preview is not shown until it is built, so
    /// the surface never sees it: a failed rebuild puts the shown preview back as it was, the form the surface
    /// holds included, disposes what the failed rebuild made, and rethrows so the caller releases the new context.
    /// The shown preview was built on the previous context, which is still loaded, so nothing is rebuilt.
    /// </remarks>
    internal void UseDesignContext(WinFormsDesignAssemblyContext? context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var previousContext = _designContext;
        var shown = CapturePreview();
        _designContext = context;

        // The rebuilt preview records every failure of the new context again, so lines still waiting
        // from the old one are dropped rather than written twice.
        ClearFailureRecords();
        try
        {
            Replace(Document);
        }
        catch
        {
            // Replace has already put the shown preview back; only the context and the failure lines are left to restore.
            _designContext = previousContext;
            RestoreFailureRecords(shown);
            throw;
        }
    }

    /// <summary>
    /// What the shown preview is made of, so that a failed rebuild can put it back as it was. The state BuildPreview
    /// clears or refills is included, so the restored preview answers property edits as before.
    /// </summary>
    private sealed record PreviewSnapshot(Forms.Form Form, Dictionary<string, Forms.Control> Controls,
        Dictionary<string, IComponent> Components, Font[] Fonts, string[] Reported, string[] Unreported,
        (string Control, string Property)[] Unresolved, (string Control, string Property)[] Resource,
        IReadOnlyList<string> Problems, string[] UserCodeFailures);

    private PreviewSnapshot CapturePreview() => new(Form,
        new Dictionary<string, Forms.Control>(_controls, StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IComponent>(_components, StringComparer.OrdinalIgnoreCase),
        _previewFonts.ToArray(), _reportedFailures.ToArray(), _unreportedFailures.ToArray(),
        _unresolvedProperties.ToArray(), _resourceProperties.ToArray(), PreviewProblems, _userCodeFailures.ToArray());

    private void ClearFailureRecords()
    {
        _reportedFailures.Clear();
        _unreportedFailures.Clear();
    }

    private void RestoreFailureRecords(PreviewSnapshot shown)
    {
        ClearFailureRecords();
        _reportedFailures.UnionWith(shown.Reported);
        _unreportedFailures.AddRange(shown.Unreported);
    }

    /// <summary>
    /// Disposes what a failed rebuild made, and puts the shown preview's form, controls, components and fonts
    /// back in the session, so that they are the ones the surface holds.
    /// </summary>
    private void ReleasePartialPreview(PreviewSnapshot shown)
    {
        var partialForm = Form;
        var partialControls = _controls.Values.ToArray();
        var partialComponents = _components.Values.ToArray();
        var partialFonts = _previewFonts.ToArray();

        Form = shown.Form;
        RestoreMap(_controls, shown.Controls);
        RestoreMap(_components, shown.Components);
        _previewFonts.Clear();
        _previewFonts.AddRange(shown.Fonts);
        _unresolvedProperties.Clear();
        _unresolvedProperties.UnionWith(shown.Unresolved);
        _resourceProperties.Clear();
        _resourceProperties.UnionWith(shown.Resource);
        _userCodeFailures.Clear();
        _userCodeFailures.AddRange(shown.UserCodeFailures);
        PreviewProblems = shown.Problems;

        if (partialForm is not null && !ReferenceEquals(partialForm, shown.Form)) partialForm.Dispose();
        foreach (var control in partialControls.Where(control => !shown.Controls.ContainsValue(control))) control.Dispose();
        DisposeComponentPreviews(partialComponents.Where(component => !shown.Components.ContainsValue(component)));
        foreach (var font in partialFonts.Where(font => !shown.Fonts.Contains(font))) font.Dispose();
    }

    private static void RestoreMap<TValue>(Dictionary<string, TValue> map, Dictionary<string, TValue> saved)
        where TValue : notnull
    {
        map.Clear();
        foreach (var pair in saved) map[pair.Key] = pair.Value;
    }

    /// <summary>
    /// Whether the designer places the component: one of the built-in Forms components, or a
    /// component of the project's own design context.
    /// </summary>
    internal bool AcceptsComponent(Type type) =>
        IsSupportedComponentType(type) || _designContext?.Contains(type) == true;

    /// <summary>
    /// Creates the preview control for a model. A project control whose constructor throws, or
    /// whose OnPaint throws when drawn once, becomes a placeholder label with the same name, so
    /// the form still opens and the rest of the IDE keeps working.
    /// </summary>
    /// <remarks>
    /// The paint is drawn once here, the creation boundary, with the control's default state. A
    /// failure that only shows after a property changes is not caught here, and an infinite loop
    /// in user code still hangs the IDE: the owner accepted that trade-off for running user code
    /// in-process rather than in a separate process.
    /// </remarks>
    private Forms.Control CreateControl(WinFormsDesignerControl model, Type type)
    {
        var isProjectType = !WinFormsDesignTypes.IsFormsType(type);
        Forms.Control control;
        try
        {
            control = (Forms.Control)Activator.CreateInstance(type)!;
        }
        catch (Exception exception) when (isProjectType)
        {
            return Placeholder(model, type, exception);
        }

        if (isProjectType)
        {
            try
            {
                PaintOnce(control);
            }
            catch (Exception exception)
            {
                control.Dispose();
                return Placeholder(model, type, exception);
            }
        }

        control.Name = model.Name;

        // The site comes after the creation paint, so that paint still fails into the placeholder above.
        // Given earlier, Basalt.Forms would draw the error frame for it and the placeholder would never show.
        // Each paint after the creation paint is then contained, and reported through the sink.
        if (isProjectType) control.Site = new WinFormsPreviewSite(control, model.Name, PaintFailureSink);
        return control;
    }

    /// <summary>Records a project component's failure to create, so the Output panel shows it.</summary>
    private void RecordComponentFailure(WinFormsDesignerComponent model, Type type, Exception exception)
    {
        if (WinFormsDesignTypes.IsFormsType(type)) return;
        RecordFailure(Localizer.Get(StringKeys.WinFormsDesignerUserCodeFailed,
            model.Name, type.FullName, exception.GetBaseException().Message));
    }

    private void RecordFailure(string line)
    {
        _userCodeFailures.Add(line);
        if (_reportedFailures.Add(line)) _unreportedFailures.Add(line);
    }

    private Forms.IDesignTimePaintFailureSink? _paintFailureSink;

    /// <summary>
    /// The designer's end of a preview control's paint failure, for the control's site to offer as a service.
    /// Basalt.Forms draws the control's error frame itself and calls this once per failing paint; the line
    /// then reaches Output with the others, at the next flush (a placement, a build).
    /// </summary>
    internal Forms.IDesignTimePaintFailureSink PaintFailureSink => _paintFailureSink ??= new PaintFailureRecorder(this);

    /// <summary>
    /// Records a paint failure of a preview control. Basalt.Forms reports a failure through the site of the control's nearest
    /// sited ancestor, so a built-in child of a project control reaches here too. Its failure is the project's code only when
    /// that ancestor is a project control: a built-in control on the form itself is not the project's code, as
    /// RecordComponentFailure leaves built-in components out.
    /// </summary>
    internal void RecordPaintFailure(Forms.Control control, Exception exception)
    {
        var owner = NearestSitedControl(control) ?? control;
        if (WinFormsDesignTypes.IsFormsType(owner.GetType())) return;

        RecordFailure(Localizer.Get(StringKeys.WinFormsDesignerUserCodeFailed,
            control.Name, control.GetType().FullName, exception.GetBaseException().Message));
    }

    /// <summary>The control itself or its nearest ancestor that has a site, or null when none has one.</summary>
    private static Forms.Control? NearestSitedControl(Forms.Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current.Site is not null) return current;
        }
        return null;
    }

    private sealed class PaintFailureRecorder(WinFormsDesignerSession session) : Forms.IDesignTimePaintFailureSink
    {
        public void PaintFailed(Forms.Control control, Exception exception) => session.RecordPaintFailure(control, exception);
    }

    private Forms.Control Placeholder(WinFormsDesignerControl model, Type type, Exception exception)
    {
        var message = exception.GetBaseException().Message;
        RecordFailure(Localizer.Get(StringKeys.WinFormsDesignerUserCodeFailed, model.Name, type.FullName, message));
        return new PlaceholderLabel(model.Name, Localizer.Get(StringKeys.WinFormsDesignerPlaceholder, type.Name, message));
    }

    /// <summary>
    /// The label that stands for a project control which failed. The designer writes the control's Text
    /// (by default its name) onto every control it places, which would hide the reason the placeholder
    /// exists; this label keeps the reason, so the user reads it on the surface.
    /// </summary>
    private sealed class PlaceholderLabel : Forms.Label
    {
        public PlaceholderLabel(string name, string reason)
        {
            Name = name;
            BorderStyle = Forms.BorderStyle.FixedSingle;

            // The base setter, because the override below ignores what the designer writes.
            base.Text = reason;
        }

        public override string Text
        {
            get => base.Text;
            set { }
        }
    }

    /// <summary>Draws the control into a bitmap, as the surface would, so that an OnPaint which throws is found now.</summary>
    private static void PaintOnce(Forms.Control control)
    {
        var width = Math.Max(1, control.Width);
        var height = Math.Max(1, control.Height);
        using var bitmap = new Bitmap(width, height);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
    }
}
