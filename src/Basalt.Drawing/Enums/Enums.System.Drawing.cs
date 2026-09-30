// The enumerations of this namespace, with the names and values of Microsoft's
// System.Drawing.Common reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Drawing;

public enum ContentAlignment
{
    TopLeft = 1,
    TopCenter = 2,
    TopRight = 4,
    MiddleLeft = 16,
    MiddleCenter = 32,
    MiddleRight = 64,
    BottomLeft = 256,
    BottomCenter = 512,
    BottomRight = 1024,
}

public enum CopyPixelOperation
{
    Blackness = 66,
    CaptureBlt = 1073741824,
    DestinationInvert = 5570569,
    MergeCopy = 12583114,
    MergePaint = 12255782,
    NoMirrorBitmap = -2147483648,
    NotSourceCopy = 3342344,
    NotSourceErase = 1114278,
    PatCopy = 15728673,
    PatInvert = 5898313,
    PatPaint = 16452105,
    SourceAnd = 8913094,
    SourceCopy = 13369376,
    SourceErase = 4457256,
    SourceInvert = 6684742,
    SourcePaint = 15597702,
    Whiteness = 16711778,
}

[Flags]
public enum FontStyle
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikeout = 8,
}

public enum GraphicsUnit
{
    World = 0,
    Display = 1,
    Pixel = 2,
    Point = 3,
    Inch = 4,
    Document = 5,
    Millimeter = 6,
}

public enum RotateFlipType
{
    RotateNoneFlipNone = 0,
    Rotate90FlipNone = 1,
    Rotate180FlipNone = 2,
    Rotate270FlipNone = 3,
    RotateNoneFlipX = 4,
    Rotate90FlipX = 5,
    Rotate180FlipX = 6,
    Rotate270FlipX = 7,
    RotateNoneFlipY = 6,
    Rotate90FlipY = 7,
    Rotate180FlipY = 4,
    Rotate270FlipY = 5,
    RotateNoneFlipXY = 2,
    Rotate90FlipXY = 3,
    Rotate180FlipXY = 0,
    Rotate270FlipXY = 1,
}

public enum StockIconId
{
    DocumentNoAssociation = 0,
    DocumentWithAssociation = 1,
    Application = 2,
    Folder = 3,
    FolderOpen = 4,
    Drive525 = 5,
    Drive35 = 6,
    DriveRemovable = 7,
    DriveFixed = 8,
    DriveNet = 9,
    DriveNetDisabled = 10,
    DriveCD = 11,
    DriveRam = 12,
    World = 13,
    Server = 15,
    Printer = 16,
    MyNetwork = 17,
    Find = 22,
    Help = 23,
    Share = 28,
    Link = 29,
    SlowFile = 30,
    Recycler = 31,
    RecyclerFull = 32,
    MediaCDAudio = 40,
    Lock = 47,
    AutoList = 49,
    PrinterNet = 50,
    ServerShare = 51,
    PrinterFax = 52,
    PrinterFaxNet = 53,
    PrinterFile = 54,
    Stack = 55,
    MediaSVCD = 56,
    StuffedFolder = 57,
    DriveUnknown = 58,
    DriveDVD = 59,
    MediaDVD = 60,
    MediaDVDRAM = 61,
    MediaDVDRW = 62,
    MediaDVDR = 63,
    MediaDVDROM = 64,
    MediaCDAudioPlus = 65,
    MediaCDRW = 66,
    MediaCDR = 67,
    MediaCDBurn = 68,
    MediaBlankCD = 69,
    MediaCDROM = 70,
    AudioFiles = 71,
    ImageFiles = 72,
    VideoFiles = 73,
    MixedFiles = 74,
    FolderBack = 75,
    FolderFront = 76,
    Shield = 77,
    Warning = 78,
    Info = 79,
    Error = 80,
    Key = 81,
    Software = 82,
    Rename = 83,
    Delete = 84,
    MediaAudioDVD = 85,
    MediaMovieDVD = 86,
    MediaEnhancedCD = 87,
    MediaEnhancedDVD = 88,
    MediaHDDVD = 89,
    MediaBluRay = 90,
    MediaVCD = 91,
    MediaDVDPlusR = 92,
    MediaDVDPlusRW = 93,
    DesktopPC = 94,
    MobilePC = 95,
    Users = 96,
    MediaSmartMedia = 97,
    MediaCompactFlash = 98,
    DeviceCellPhone = 99,
    DeviceCamera = 100,
    DeviceVideoCamera = 101,
    DeviceAudioPlayer = 102,
    NetworkConnect = 103,
    Internet = 104,
    ZipFile = 105,
    Settings = 106,
    DriveHDDVD = 132,
    DriveBD = 133,
    MediaHDDVDROM = 134,
    MediaHDDVDR = 135,
    MediaHDDVDRAM = 136,
    MediaBDROM = 137,
    MediaBDR = 138,
    MediaBDRE = 139,
    ClusteredDrive = 140,
}

[Flags]
public enum StockIconOptions
{
    Default = 0,
    SmallIcon = 1,
    ShellIconSize = 4,
    LinkOverlay = 32768,
    Selected = 65536,
}

public enum StringAlignment
{
    Near = 0,
    Center = 1,
    Far = 2,
}

public enum StringDigitSubstitute
{
    User = 0,
    None = 1,
    National = 2,
    Traditional = 3,
}

[Flags]
public enum StringFormatFlags
{
    DirectionRightToLeft = 1,
    DirectionVertical = 2,
    FitBlackBox = 4,
    DisplayFormatControl = 32,
    NoFontFallback = 1024,
    MeasureTrailingSpaces = 2048,
    NoWrap = 4096,
    LineLimit = 8192,
    NoClip = 16384,
}

public enum StringTrimming
{
    None = 0,
    Character = 1,
    Word = 2,
    EllipsisCharacter = 3,
    EllipsisWord = 4,
    EllipsisPath = 5,
}

public enum StringUnit
{
    World = 0,
    Display = 1,
    Pixel = 2,
    Point = 3,
    Inch = 4,
    Document = 5,
    Millimeter = 6,
    Em = 32,
}
