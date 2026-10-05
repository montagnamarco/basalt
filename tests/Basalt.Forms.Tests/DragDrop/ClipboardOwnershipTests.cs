using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Input;

namespace Basalt.Forms.Tests.DragDrop;

[Collection(SharedStateCollection.Name)]
public class ClipboardOwnershipTests
{
    [Fact]
    public void External_writer_replaces_all_previous_local_formats()
    {
        var platform = new FakeClipboard();
        PlatformClipboard.UseClipboardForTests(platform);
        try
        {
            var ours = new System.Windows.Forms.DataObject();
            ours.SetData("Basalt.Live", new object());
            ours.SetText("inside");
            Clipboard.SetDataObject(ours);

            Assert.True(Clipboard.ContainsData("Basalt.Live"));
            var textBox = new TextBox { Text = "fresh" };
            textBox.SelectAll();
            textBox.Copy();
            Assert.Equal("fresh", Clipboard.GetText());
            Assert.False(Clipboard.ContainsData("Basalt.Live"));

            Clipboard.SetDataObject(ours);
            Assert.True(Clipboard.ContainsData("Basalt.Live"));
            platform.ReplaceWithExternalText("outside");

            Assert.False(Clipboard.ContainsData("Basalt.Live"));
            Assert.Equal("outside", Clipboard.GetText());
        }
        finally
        {
            Clipboard.Clear();
            PlatformClipboard.UseClipboardForTests(null);
        }
    }

    private sealed class FakeClipboard : IClipboardAccess
    {
        private IAsyncDataTransfer? _content;
        private bool _owned;

        public Task SetDataAsync(IAsyncDataTransfer? dataTransfer)
        {
            _content = dataTransfer;
            _owned = true;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            _content = null;
            _owned = false;
            return Task.CompletedTask;
        }

        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() =>
            Task.FromResult(_owned ? _content : null);

        public Task<IAsyncDataTransfer?> TryGetDataAsync()
        {
            if (_content is null) return Task.FromResult<IAsyncDataTransfer?>(null);
            var copy = ClipboardDataTransfer.Export(ClipboardDataTransfer.Import(_content));
            return Task.FromResult<IAsyncDataTransfer?>(copy);
        }

        public void ReplaceWithExternalText(string text)
        {
            var content = new DataTransfer();
            content.Add(DataTransferItem.CreateText(text));
            _content = content;
            _owned = false;
        }
    }
}
