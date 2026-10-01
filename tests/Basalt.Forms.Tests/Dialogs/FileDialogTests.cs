using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;

namespace Basalt.Forms.Tests.Dialogs;

/// <summary>
/// CommonDialog, FileDialog, OpenFileDialog, SaveFileDialog and
/// FolderBrowserDialog, driven through a fake IFilePickerService so no real
/// picker ever shows. FilePickers.Current is swapped for each test and always
/// restored, since it is a process-wide seam other tests could also rely on.
/// </summary>
public class FileDialogTests
{
    [Fact]
    public void A_plain_dialog_maps_RunDialog_true_to_OK_and_false_to_Cancel()
    {
        var dialog = new ProbeDialog { ReturnValue = true };
        Assert.Equal(DialogResult.OK, dialog.ShowDialog());
        Assert.True(dialog.RunDialogCalled);

        dialog.ReturnValue = false;
        Assert.Equal(DialogResult.Cancel, dialog.ShowDialog());
    }

    [Fact]
    public void HookProc_is_not_supported_on_this_platform()
    {
        var dialog = new ProbeDialog();
        Assert.Throws<PlatformNotSupportedException>(() => dialog.CallHookProc());
    }

    [Fact]
    public void Filter_parses_description_and_pattern_pairs_including_semicolon_lists()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FilesToReturn = [ExistingFile()];

        var dialog = new OpenFileDialog
        {
            Filter = "Text files|*.txt|Images|*.png;*.jpg|All files|*.*",
            FilterIndex = 2,
            CheckFileExists = false,
            CheckPathExists = false,
        };

        dialog.ShowDialog();

        Assert.NotNull(swap.Fake.LastOpenRequest);
        var request = swap.Fake.LastOpenRequest!;
        Assert.Equal(3, request.Filters.Count);
        Assert.Equal("Text files", request.Filters[0].Name);
        Assert.Equal(["*.txt"], request.Filters[0].Patterns);
        Assert.Equal("Images", request.Filters[1].Name);
        Assert.Equal(["*.png", "*.jpg"], request.Filters[1].Patterns);
        Assert.Equal("All files", request.Filters[2].Name);
        Assert.Equal(["*.*"], request.Filters[2].Patterns);
        Assert.Equal(2, request.FilterIndex);
    }

    [Fact]
    public void An_odd_number_of_filter_segments_throws_ArgumentException()
    {
        var dialog = new OpenFileDialog();

        Assert.Throws<ArgumentException>(() => dialog.Filter = "Text files|*.txt|Images");
    }

    [Fact]
    public void An_empty_filter_sends_no_filter_restriction()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FilesToReturn = [ExistingFile()];

        var dialog = new OpenFileDialog { CheckFileExists = false, CheckPathExists = false };
        dialog.ShowDialog();

        Assert.NotNull(swap.Fake.LastOpenRequest);
        Assert.Empty(swap.Fake.LastOpenRequest!.Filters);
    }

    [Fact]
    public void AddExtension_appends_DefaultExt_when_the_chosen_name_has_none()
    {
        using var swap = FakePickers.Swap();
        var withoutExtension = Path.Combine(Path.GetTempPath(), "basalt-save-" + Guid.NewGuid().ToString("N"));
        swap.Fake.FileToReturn = withoutExtension;

        var dialog = new SaveFileDialog { DefaultExt = "txt", AddExtension = true, CheckPathExists = false };

        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(withoutExtension + ".txt", dialog.FileName);
    }

    [Fact]
    public void AddExtension_false_leaves_the_chosen_name_untouched()
    {
        using var swap = FakePickers.Swap();
        var withoutExtension = Path.Combine(Path.GetTempPath(), "basalt-save-" + Guid.NewGuid().ToString("N"));
        swap.Fake.FileToReturn = withoutExtension;

        var dialog = new SaveFileDialog { DefaultExt = "txt", AddExtension = false, CheckPathExists = false };

        dialog.ShowDialog();

        Assert.Equal(withoutExtension, dialog.FileName);
    }

    [Fact]
    public void A_FileOk_handler_that_cancels_turns_the_result_into_Cancel()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FilesToReturn = [ExistingFile()];

        var dialog = new OpenFileDialog { CheckFileExists = false, CheckPathExists = false };
        dialog.FileOk += (_, e) => e.Cancel = true;

        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.Cancel, result);
    }

    [Fact]
    public void A_FileOk_handler_that_does_not_cancel_leaves_the_result_OK()
    {
        using var swap = FakePickers.Swap();
        var file = ExistingFile();
        swap.Fake.FilesToReturn = [file];

        var dialog = new OpenFileDialog { CheckFileExists = false, CheckPathExists = false };
        var raised = false;
        dialog.FileOk += (_, e) => raised = true;

        var result = dialog.ShowDialog();

        Assert.True(raised);
        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(file, dialog.FileName);
    }

    [Fact]
    public void CheckFileExists_cancels_the_dialog_when_the_picked_file_is_gone()
    {
        using var swap = FakePickers.Swap();
        var missing = Path.Combine(Path.GetTempPath(), "basalt-missing-" + Guid.NewGuid().ToString("N") + ".txt");
        swap.Fake.FilesToReturn = [missing];

        var dialog = new OpenFileDialog(); // CheckFileExists defaults to true

        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal(string.Empty, dialog.FileName);
    }

    [Fact]
    public void CheckFileExists_accepts_a_file_that_exists_and_maps_the_result()
    {
        using var swap = FakePickers.Swap();
        var file = ExistingFile();
        swap.Fake.FilesToReturn = [file];

        var dialog = new OpenFileDialog();

        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(file, dialog.FileName);
        Assert.Equal([file], dialog.FileNames);
    }

    [Fact]
    public void Multiselect_false_asks_the_picker_for_a_single_file_only()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FilesToReturn = [ExistingFile()];

        var dialog = new OpenFileDialog { CheckFileExists = false, CheckPathExists = false };
        dialog.ShowDialog();

        Assert.NotNull(swap.Fake.LastOpenRequest);
        Assert.False(swap.Fake.LastOpenRequest!.AllowMultiple);
    }

    [Fact]
    public void Multiselect_true_reports_every_file_the_picker_returned()
    {
        using var swap = FakePickers.Swap();
        var first = ExistingFile();
        var second = ExistingFile();
        swap.Fake.FilesToReturn = [first, second];

        var dialog = new OpenFileDialog { Multiselect = true };
        var result = dialog.ShowDialog();

        Assert.NotNull(swap.Fake.LastOpenRequest);
        Assert.True(swap.Fake.LastOpenRequest!.AllowMultiple);
        Assert.Equal(DialogResult.OK, result);
        Assert.Equal([first, second], dialog.FileNames);
        Assert.Equal(first, dialog.FileName);
    }

    [Fact]
    public void SafeFileNames_strips_the_directory_from_every_selected_file()
    {
        using var swap = FakePickers.Swap();
        var file = ExistingFile();
        swap.Fake.FilesToReturn = [file];

        var dialog = new OpenFileDialog();
        dialog.ShowDialog();

        Assert.Equal(Path.GetFileName(file), dialog.SafeFileName);
        Assert.Equal([Path.GetFileName(file)], dialog.SafeFileNames);
    }

    [Fact]
    public void Title_and_InitialDirectory_reach_the_picker_request()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FileToReturn = Path.Combine(Path.GetTempPath(), "basalt-save-" + Guid.NewGuid().ToString("N") + ".txt");

        var dialog = new SaveFileDialog
        {
            Title = "Save project",
            InitialDirectory = Path.GetTempPath(),
            CheckPathExists = false,
        };

        dialog.ShowDialog();

        Assert.NotNull(swap.Fake.LastSaveRequest);
        var request = swap.Fake.LastSaveRequest!;
        Assert.Equal("Save project", request.Title);
        Assert.Equal(Path.GetTempPath(), request.InitialDirectory);
    }

    [Fact]
    public void SaveFileDialog_forwards_OverwritePrompt_to_the_picker_request()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FileToReturn = Path.Combine(Path.GetTempPath(), "basalt-save-" + Guid.NewGuid().ToString("N") + ".txt");

        var dialog = new SaveFileDialog { OverwritePrompt = false, CheckPathExists = false };
        dialog.ShowDialog();
        Assert.NotNull(swap.Fake.LastSaveRequest);
        Assert.False(swap.Fake.LastSaveRequest!.ShowOverwritePrompt);

        dialog.OverwritePrompt = true;
        dialog.ShowDialog();
        Assert.NotNull(swap.Fake.LastSaveRequest);
        Assert.True(swap.Fake.LastSaveRequest!.ShowOverwritePrompt);
    }

    [Fact]
    public void A_cancelled_picker_returns_DialogResult_Cancel_and_keeps_FileName_empty()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FilesToReturn = []; // the user closed the picker without choosing anything

        var dialog = new OpenFileDialog();
        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal(string.Empty, dialog.FileName);
    }

    [Fact]
    public void FolderBrowserDialog_maps_the_chosen_folder_to_SelectedPath()
    {
        using var swap = FakePickers.Swap();
        var folder = Path.GetTempPath();
        swap.Fake.FoldersToReturn = [folder];

        var dialog = new FolderBrowserDialog { Description = "Choose a folder" };
        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.OK, result);
        Assert.Equal(folder, dialog.SelectedPath);
        Assert.Equal([folder], dialog.SelectedPaths);
        Assert.NotNull(swap.Fake.LastFolderRequest);
        Assert.False(swap.Fake.LastFolderRequest!.AllowMultiple);
    }

    [Fact]
    public void FolderBrowserDialog_Multiselect_reaches_the_picker_request()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FoldersToReturn = [Path.GetTempPath()];

        var dialog = new FolderBrowserDialog { Multiselect = true };
        dialog.ShowDialog();

        Assert.NotNull(swap.Fake.LastFolderRequest);
        Assert.True(swap.Fake.LastFolderRequest!.AllowMultiple);
    }

    [Fact]
    public void A_cancelled_folder_picker_returns_DialogResult_Cancel()
    {
        using var swap = FakePickers.Swap();
        swap.Fake.FoldersToReturn = [];

        var dialog = new FolderBrowserDialog();
        var result = dialog.ShowDialog();

        Assert.Equal(DialogResult.Cancel, result);
        Assert.Equal(string.Empty, dialog.SelectedPath);
    }

    [Fact]
    public void Reset_restores_OpenFileDialog_to_its_constructor_defaults()
    {
        var dialog = new OpenFileDialog { FileName = "x.txt", Filter = "A|*.a", FilterIndex = 3, Multiselect = true, CheckFileExists = false };

        dialog.Reset();

        Assert.Equal(string.Empty, dialog.FileName);
        Assert.Equal(string.Empty, dialog.Filter);
        Assert.Equal(1, dialog.FilterIndex);
        Assert.False(dialog.Multiselect);
        Assert.True(dialog.CheckFileExists);
    }

    private static string ExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "basalt-open-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "basalt");
        return path;
    }

    /// <summary>A minimal CommonDialog to exercise ShowDialog/RunDialog/HookProc without any file dialog behaviour.</summary>
    private sealed class ProbeDialog : CommonDialog
    {
        public bool RunDialogCalled { get; private set; }
        public bool ReturnValue { get; set; } = true;

        public override void Reset()
        {
        }

        protected override bool RunDialog(IntPtr hwndOwner)
        {
            RunDialogCalled = true;
            return ReturnValue;
        }

        public IntPtr CallHookProc() => HookProc(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [Fact]
    public void Without_DefaultExt_a_saved_name_takes_the_extension_of_the_chosen_filter()
    {
        using var pickers = FakePickers.Swap();
        pickers.Fake.FileToReturn = Path.Combine(Path.GetTempPath(), "report");
        using var dialog = new SaveFileDialog { Filter = "Text|*.txt|Log|*.log;*.out", FilterIndex = 2 };

        dialog.ShowDialog();

        Assert.Equal(Path.Combine(Path.GetTempPath(), "report.log"), dialog.FileName);
    }

    [Fact]
    public void A_wildcard_filter_leaves_the_name_as_typed()
    {
        using var pickers = FakePickers.Swap();
        pickers.Fake.FileToReturn = Path.Combine(Path.GetTempPath(), "report");
        using var dialog = new SaveFileDialog { Filter = "All|*.*" };

        dialog.ShowDialog();

        Assert.Equal(Path.Combine(Path.GetTempPath(), "report"), dialog.FileName);
    }

    /// <summary>Substitutes FilePickers.Current with a fake for one test and restores it, even if the test throws.</summary>
    private sealed class FakePickers : IDisposable
    {
        public readonly FakeFilePickerService Fake = new();

        public static FakePickers Swap()
        {
            var swap = new FakePickers();
            FilePickers.Current = swap.Fake;
            return swap;
        }

        public void Dispose() => FilePickers.Current = new FilePickers();
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string[] FilesToReturn { get; set; } = [];
        public string? FileToReturn { get; set; }
        public string[] FoldersToReturn { get; set; } = [];
        public FilePickerRequest? LastOpenRequest { get; private set; }
        public FilePickerRequest? LastSaveRequest { get; private set; }
        public FolderPickerRequest? LastFolderRequest { get; private set; }

        public string[] PickOpenFiles(HostWindow? owner, FilePickerRequest request)
        {
            LastOpenRequest = request;
            return FilesToReturn;
        }

        public string? PickSaveFile(HostWindow? owner, FilePickerRequest request)
        {
            LastSaveRequest = request;
            return FileToReturn;
        }

        public string[] PickFolders(HostWindow? owner, FolderPickerRequest request)
        {
            LastFolderRequest = request;
            return FoldersToReturn;
        }
    }
}
