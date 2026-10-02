using System.Runtime.Serialization;

namespace System.Windows.Forms;

public abstract partial class AxHost
{
    /// <summary>
    /// The bytes an ActiveX control's OcxState persists between runs: on
    /// Windows, whatever the control itself writes through IPersistStream
    /// or IPersistPropertyBag. There is no ActiveX control to ask, so this
    /// only keeps the bytes it is given - the round trip a designer needs
    /// to save and restore them still works, even though nothing here ever
    /// produces a control from them. The BinaryFormatter-style constructor
    /// and <see cref="ISerializable.GetObjectData"/> a deserializer would
    /// use both refuse, for the same reason <see cref="ImageListStreamer"/>
    /// does: nothing in Basalt reads or writes that format.
    /// </summary>
    public class State : IDisposable, ISerializable
    {
        private const string SerializationNotSupported =
            "AxHost.State keeps an ActiveX control's persisted bytes in memory only: it does not read or write the BinaryFormatter-encoded block Windows Forms stored in a .resx file.";

        private byte[] _buffer;
        private bool _disposed;

        public State(Stream ms, int storageType, bool manualUpdate, string? licKey)
        {
            ArgumentNullException.ThrowIfNull(ms);

            using var copy = new MemoryStream();
            ms.CopyTo(copy);
            _buffer = copy.ToArray();

            StorageType = storageType;
            ManualUpdate = manualUpdate;
            LicenseKey = licKey;
        }

        /// <summary>For <see cref="StateConverter"/>, round-tripping a resx-stored byte array back into a State.</summary>
        internal State(byte[] buffer)
        {
            _buffer = buffer;
        }

        /// <summary>
        /// The constructor a BinaryFormatter-style deserializer calls by
        /// reflection. Never reachable: nothing in Basalt produces the bytes
        /// it would read.
        /// </summary>
        protected State(SerializationInfo info, StreamingContext context)
        {
            throw new PlatformNotSupportedException(SerializationNotSupported);
        }

        internal int StorageType { get; }

        internal bool ManualUpdate { get; }

        internal string? LicenseKey { get; }

        internal byte[] GetBuffer() => _buffer;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing) _buffer = [];

            _disposed = true;
        }

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context) =>
            throw new PlatformNotSupportedException(SerializationNotSupported);
    }
}
