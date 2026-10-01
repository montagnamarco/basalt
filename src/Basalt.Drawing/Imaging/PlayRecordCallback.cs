namespace System.Drawing.Imaging;

/// <summary>Called by <see cref="Metafile.PlayRecord"/> for a single record; unused here because
/// playing a record into a Windows device context has no Skia equivalent.</summary>
public delegate void PlayRecordCallback(EmfPlusRecordType recordType, int flags, int dataSize, IntPtr recordData);
