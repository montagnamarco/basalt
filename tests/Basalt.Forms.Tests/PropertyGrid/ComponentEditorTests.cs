using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>
/// ComponentEditorPage's load/save/dirty state machine, ComponentEditorForm
/// wiring pages into a real TreeView + page host + OK/Cancel/Apply, and
/// WindowsFormsComponentEditor.EditComponent showing that form modally.
/// </summary>
public class ComponentEditorTests
{
    private sealed class Widget : Component
    {
        public string Value { get; set; } = "initial";
    }

    private sealed class RecordingPage : ComponentEditorPage
    {
        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public int ApplyCompleteCount { get; private set; }

        public string LoadedValue { get; private set; } = "";

        public string ValueToSave { get; set; } = "from page";

        protected override void LoadComponent() => LoadedValue = ((Widget)GetSelectedComponent()).Value;

        protected override void SaveComponent()
        {
            SaveCount++;
            ((Widget)GetSelectedComponent()).Value = ValueToSave;
        }

        public override void OnApplyComplete() => ApplyCompleteCount++;

        // LoadComponent is private above; expose how many times it actually ran through this counter instead.
        protected override void ReloadComponent()
        {
            LoadCount++;
            base.ReloadComponent();
        }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class OtherPage : ComponentEditorPage
    {
        public override string Title => "Other";

        protected override void LoadComponent()
        {
        }

        protected override void SaveComponent()
        {
        }
    }

    [Fact]
    public void Activate_only_loads_once_until_SetComponent_asks_for_a_reload()
    {
        var widget = new Widget { Value = "hello" };
        var page = new RecordingPage();
        page.SetComponent(widget);

        page.Activate();
        widget.Value = "changed behind the page's back";
        page.Activate(); // No SetComponent since the first Activate: must not reload.

        Assert.Equal("hello", page.LoadedValue);
        Assert.Equal(0, page.LoadCount); // ReloadComponent never ran; the only load was the first-activate LoadComponent call.
    }

    [Fact]
    public void SetComponent_after_first_activation_forces_a_reload_on_the_next_Activate()
    {
        var first = new Widget { Value = "first" };
        var second = new Widget { Value = "second" };
        var page = new RecordingPage();

        page.SetComponent(first);
        page.Activate();
        Assert.Equal("first", page.LoadedValue);

        page.SetComponent(second);
        page.Activate();

        Assert.Equal("second", page.LoadedValue);
        Assert.Equal(1, page.LoadCount); // ReloadComponent, not the first LoadComponent, ran exactly once.
    }

    [Fact]
    public void ApplyChanges_saves_the_component_and_raises_OnApplyComplete()
    {
        var widget = new Widget();
        var page = new RecordingPage { ValueToSave = "applied" };
        page.SetComponent(widget);

        page.ApplyChanges();

        Assert.Equal("applied", widget.Value);
        Assert.Equal(1, page.SaveCount);
        Assert.Equal(1, page.ApplyCompleteCount);
    }

    [Fact]
    public void Deactivate_applies_changes_when_CommitOnDeactivate_is_true()
    {
        var widget = new Widget();
        var page = new RecordingPage { ValueToSave = "committed" };
        page.SetComponent(widget);
        page.Activate();

        page.Deactivate();

        Assert.Equal("committed", widget.Value);
        Assert.Equal(1, page.SaveCount);
    }

    [Fact]
    public void Deactivate_does_not_apply_when_CommitOnDeactivate_is_false()
    {
        var widget = new Widget();
        var page = new RecordingPage { ValueToSave = "should not land", CommitOnDeactivate = false };
        page.SetComponent(widget);
        page.Activate();

        page.Deactivate();

        Assert.Equal(0, page.SaveCount);
        Assert.Equal("initial", widget.Value);
    }

    [AvaloniaFact]
    public void ComponentEditorForm_switching_pages_commits_the_page_being_left()
    {
        var widget = new Widget();
        var form = new ComponentEditorForm(widget, [typeof(RecordingPage), typeof(OtherPage)]);

        Dispatcher.UIThread.Post(() =>
        {
            var selector = (TreeView)form.Controls.Cast<Control>().First(c => c is TreeView);
            selector.SelectedNode = selector.Nodes[1]; // leaving RecordingPage commits it (CommitOnDeactivate defaults true).

            var cancel = form.Controls.Cast<Control>().OfType<FlowLayoutPanel>().First()
                .Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel");
            cancel.PerformClick();
        });

        var result = form.ShowForm();

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal("from page", widget.Value); // RecordingPage's default ValueToSave, committed by the page switch alone.
    }

    [Fact]
    public void Disposing_the_form_disposes_every_page_including_ones_never_shown()
    {
        var widget = new Widget();
        var form = new ComponentEditorForm(widget, [typeof(RecordingPage), typeof(OtherPage)]);

        // Never shown, never selected: no page was ever activated, so only the form's
        // own disposal of its children can reach them.
        var recordingPage = (RecordingPage)form.Pages[0];
        form.Dispose();

        Assert.True(recordingPage.WasDisposed);
    }

    [AvaloniaFact]
    public void Selecting_a_different_initial_page_and_cancelling_immediately_applies_nothing()
    {
        var widget = new Widget();
        var editor = new InitialPageEditor();

        Dispatcher.UIThread.Post(() =>
        {
            var form = Application.OpenForms.Cast<Form>().Last();
            var cancel = form.Controls.Cast<Control>().OfType<FlowLayoutPanel>().First()
                .Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "Cancel");
            cancel.PerformClick();
        });

        var applied = editor.EditComponent(widget, owner: null);

        Assert.False(applied);
        Assert.Equal("initial", widget.Value); // RecordingPage.SaveComponent, which would write "from page", never ran.
    }

    private sealed class InitialPageEditor : WindowsFormsComponentEditor
    {
        protected override Type[] GetComponentEditorPages() => [typeof(RecordingPage), typeof(OtherPage)];

        protected override int GetInitialComponentEditorPageIndex() => 1;
    }

    [AvaloniaFact]
    public void WindowsFormsComponentEditor_EditComponent_shows_the_form_and_reports_whether_anything_was_applied()
    {
        var widget = new Widget();
        var editor = new RecordingEditor();

        Dispatcher.UIThread.Post(() =>
        {
            var form = Application.OpenForms.Cast<Form>().Last();
            var ok = form.Controls.Cast<Control>().OfType<FlowLayoutPanel>().First()
                .Controls.Cast<Control>().OfType<Button>().First(b => b.Text == "OK");
            ok.PerformClick();
        });

        var applied = editor.EditComponent(widget, owner: null);

        Assert.True(applied);
        Assert.Equal("from page", widget.Value);
    }

    [Fact]
    public void WindowsFormsComponentEditor_EditComponent_returns_false_when_there_are_no_pages()
    {
        var editor = new EmptyEditor();

        Assert.False(editor.EditComponent(new Widget(), owner: null));
    }

    private sealed class RecordingEditor : WindowsFormsComponentEditor
    {
        protected override Type[] GetComponentEditorPages() => [typeof(RecordingPage)];
    }

    private sealed class EmptyEditor : WindowsFormsComponentEditor
    {
    }
}
