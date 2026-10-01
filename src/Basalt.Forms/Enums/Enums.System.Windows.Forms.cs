// The enumerations of this namespace, with the names and values of Microsoft's
// System.Windows.Forms reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Windows.Forms;

public enum AccessibleEvents
{
    SystemSound = 1,
    SystemAlert = 2,
    SystemForeground = 3,
    SystemMenuStart = 4,
    SystemMenuEnd = 5,
    SystemMenuPopupStart = 6,
    SystemMenuPopupEnd = 7,
    SystemCaptureStart = 8,
    SystemCaptureEnd = 9,
    SystemMoveSizeStart = 10,
    SystemMoveSizeEnd = 11,
    SystemContextHelpStart = 12,
    SystemContextHelpEnd = 13,
    SystemDragDropStart = 14,
    SystemDragDropEnd = 15,
    SystemDialogStart = 16,
    SystemDialogEnd = 17,
    SystemScrollingStart = 18,
    SystemScrollingEnd = 19,
    SystemSwitchStart = 20,
    SystemSwitchEnd = 21,
    SystemMinimizeStart = 22,
    SystemMinimizeEnd = 23,
    Create = 32768,
    Destroy = 32769,
    Show = 32770,
    Hide = 32771,
    Reorder = 32772,
    Focus = 32773,
    Selection = 32774,
    SelectionAdd = 32775,
    SelectionRemove = 32776,
    SelectionWithin = 32777,
    StateChange = 32778,
    LocationChange = 32779,
    NameChange = 32780,
    DescriptionChange = 32781,
    ValueChange = 32782,
    ParentChange = 32783,
    HelpChange = 32784,
    DefaultActionChange = 32785,
    AcceleratorChange = 32786,
}

public enum AccessibleNavigation
{
    Down = 2,
    FirstChild = 7,
    LastChild = 8,
    Left = 3,
    Next = 5,
    Previous = 6,
    Right = 4,
    Up = 1,
}

public enum AccessibleRole
{
    Default = -1,
    None = 0,
    TitleBar = 1,
    MenuBar = 2,
    ScrollBar = 3,
    Grip = 4,
    Sound = 5,
    Cursor = 6,
    Caret = 7,
    Alert = 8,
    Window = 9,
    Client = 10,
    MenuPopup = 11,
    MenuItem = 12,
    ToolTip = 13,
    Application = 14,
    Document = 15,
    Pane = 16,
    Chart = 17,
    Dialog = 18,
    Border = 19,
    Grouping = 20,
    Separator = 21,
    ToolBar = 22,
    StatusBar = 23,
    Table = 24,
    ColumnHeader = 25,
    RowHeader = 26,
    Column = 27,
    Row = 28,
    Cell = 29,
    Link = 30,
    HelpBalloon = 31,
    Character = 32,
    List = 33,
    ListItem = 34,
    Outline = 35,
    OutlineItem = 36,
    PageTab = 37,
    PropertyPage = 38,
    Indicator = 39,
    Graphic = 40,
    StaticText = 41,
    Text = 42,
    PushButton = 43,
    CheckButton = 44,
    RadioButton = 45,
    ComboBox = 46,
    DropList = 47,
    ProgressBar = 48,
    Dial = 49,
    HotkeyField = 50,
    Slider = 51,
    SpinButton = 52,
    Diagram = 53,
    Animation = 54,
    Equation = 55,
    ButtonDropDown = 56,
    ButtonMenu = 57,
    ButtonDropDownGrid = 58,
    WhiteSpace = 59,
    PageTabList = 60,
    Clock = 61,
    SplitButton = 62,
    IpAddress = 63,
    OutlineButton = 64,
}

[Flags]
public enum AccessibleSelection
{
    None = 0,
    TakeFocus = 1,
    TakeSelection = 2,
    ExtendSelection = 4,
    AddSelection = 8,
    RemoveSelection = 16,
}

[Flags]
public enum AccessibleStates
{
    None = 0,
    Unavailable = 1,
    Selected = 2,
    Focused = 4,
    Pressed = 8,
    Checked = 16,
    Mixed = 32,
    Indeterminate = 32,
    ReadOnly = 64,
    HotTracked = 128,
    Default = 256,
    Expanded = 512,
    Collapsed = 1024,
    Busy = 2048,
    Floating = 4096,
    Marqueed = 8192,
    Animated = 16384,
    Invisible = 32768,
    Offscreen = 65536,
    Sizeable = 131072,
    Moveable = 262144,
    SelfVoicing = 524288,
    Focusable = 1048576,
    Selectable = 2097152,
    Linked = 4194304,
    Traversed = 8388608,
    MultiSelectable = 16777216,
    ExtSelectable = 33554432,
    AlertLow = 67108864,
    AlertMedium = 134217728,
    AlertHigh = 268435456,
    Protected = 536870912,
    HasPopup = 1073741824,
    Valid = 1073741823,
}

[Flags]
public enum AnchorStyles
{
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8,
    None = 0,
}

public enum Appearance
{
    Normal = 0,
    Button = 1,
}

[Flags]
public enum ArrangeDirection
{
    Down = 4,
    Left = 0,
    Right = 0,
    Up = 4,
}

[Flags]
public enum ArrangeStartingPosition
{
    BottomLeft = 0,
    BottomRight = 1,
    Hide = 8,
    TopLeft = 2,
    TopRight = 3,
}

public enum ArrowDirection
{
    Up = 1,
    Down = 17,
    Left = 0,
    Right = 16,
}

public enum AutoCompleteMode
{
    None = 0,
    Suggest = 1,
    Append = 2,
    SuggestAppend = 3,
}

public enum AutoCompleteSource
{
    FileSystem = 1,
    HistoryList = 2,
    RecentlyUsedList = 4,
    AllUrl = 6,
    AllSystemSources = 7,
    FileSystemDirectories = 32,
    CustomSource = 64,
    None = 128,
    ListItems = 256,
}

public enum AutoScaleMode
{
    None = 0,
    Font = 1,
    Dpi = 2,
    Inherit = 3,
}

public enum AutoSizeMode
{
    GrowAndShrink = 0,
    GrowOnly = 1,
}

public enum AutoValidate
{
    Disable = 0,
    EnablePreventFocusChange = 1,
    EnableAllowFocusChange = 2,
    Inherit = -1,
}

[Flags]
public enum BatteryChargeStatus
{
    High = 1,
    Low = 2,
    Critical = 4,
    Charging = 8,
    NoSystemBattery = 128,
    Unknown = 255,
}

public enum BindingCompleteContext
{
    ControlUpdate = 0,
    DataSourceUpdate = 1,
}

public enum BindingCompleteState
{
    Success = 0,
    DataError = 1,
    Exception = 2,
}

public enum BootMode
{
    Normal = 0,
    FailSafe = 1,
    FailSafeWithNetwork = 2,
}

[Flags]
public enum Border3DSide
{
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Middle = 2048,
    All = 2063,
}

public enum Border3DStyle
{
    Adjust = 8192,
    Bump = 9,
    Etched = 6,
    Flat = 16394,
    Raised = 5,
    RaisedInner = 4,
    RaisedOuter = 1,
    Sunken = 10,
    SunkenInner = 8,
    SunkenOuter = 2,
}

public enum BorderStyle
{
    None = 0,
    FixedSingle = 1,
    Fixed3D = 2,
}

[Flags]
public enum BoundsSpecified
{
    X = 1,
    Y = 2,
    Width = 4,
    Height = 8,
    Location = 3,
    Size = 12,
    All = 15,
    None = 0,
}

public enum ButtonBorderStyle
{
    None = 0,
    Dotted = 1,
    Dashed = 2,
    Solid = 3,
    Inset = 4,
    Outset = 5,
}

[Flags]
public enum ButtonState
{
    Checked = 1024,
    Flat = 16384,
    Inactive = 256,
    Normal = 0,
    Pushed = 512,
    All = 18176,
}

public enum CaptionButton
{
    Close = 0,
    Help = 4,
    Maximize = 2,
    Minimize = 1,
    Restore = 3,
}

public enum CharacterCasing
{
    Normal = 0,
    Upper = 1,
    Lower = 2,
}

public enum CheckState
{
    Unchecked = 0,
    Checked = 1,
    Indeterminate = 2,
}

public enum CloseReason
{
    None = 0,
    WindowsShutDown = 1,
    MdiFormClosing = 2,
    UserClosing = 3,
    TaskManagerClosing = 4,
    FormOwnerClosing = 5,
    ApplicationExitCall = 6,
}

public enum ColorDepth
{
    Depth4Bit = 4,
    Depth8Bit = 8,
    Depth16Bit = 16,
    Depth24Bit = 24,
    Depth32Bit = 32,
}

public enum ColumnHeaderAutoResizeStyle
{
    None = 0,
    HeaderSize = 1,
    ColumnContent = 2,
}

public enum ColumnHeaderStyle
{
    None = 0,
    Nonclickable = 1,
    Clickable = 2,
}

public enum ComboBoxStyle
{
    Simple = 0,
    DropDown = 1,
    DropDownList = 2,
}

[Flags]
public enum ControlStyles
{
    ContainerControl = 1,
    UserPaint = 2,
    Opaque = 4,
    ResizeRedraw = 16,
    FixedWidth = 32,
    FixedHeight = 64,
    StandardClick = 256,
    Selectable = 512,
    UserMouse = 1024,
    SupportsTransparentBackColor = 2048,
    StandardDoubleClick = 4096,
    AllPaintingInWmPaint = 8192,
    CacheText = 16384,
    EnableNotifyMessage = 32768,
    DoubleBuffer = 65536,
    OptimizedDoubleBuffer = 131072,
    UseTextForAccessibility = 262144,
    ApplyThemingImplicitly = 524288,
}

public enum ControlUpdateMode
{
    OnPropertyChanged = 0,
    Never = 1,
}

public enum DataGridLineStyle
{
    None = 0,
    Solid = 1,
}

public enum DataGridParentRowsLabelStyle
{
    None = 0,
    TableName = 1,
    ColumnName = 2,
    Both = 3,
}

public enum DataGridViewAdvancedCellBorderStyle
{
    NotSet = 0,
    None = 1,
    Single = 2,
    Inset = 3,
    InsetDouble = 4,
    Outset = 5,
    OutsetDouble = 6,
    OutsetPartial = 7,
}

public enum DataGridViewAutoSizeColumnMode
{
    NotSet = 0,
    None = 1,
    AllCells = 6,
    AllCellsExceptHeader = 4,
    DisplayedCells = 10,
    DisplayedCellsExceptHeader = 8,
    ColumnHeader = 2,
    Fill = 16,
}

public enum DataGridViewAutoSizeColumnsMode
{
    AllCells = 6,
    AllCellsExceptHeader = 4,
    DisplayedCells = 10,
    DisplayedCellsExceptHeader = 8,
    None = 1,
    ColumnHeader = 2,
    Fill = 16,
}

public enum DataGridViewAutoSizeRowMode
{
    AllCells = 3,
    AllCellsExceptHeader = 2,
    RowHeader = 1,
}

public enum DataGridViewAutoSizeRowsMode
{
    AllCells = 7,
    AllCellsExceptHeaders = 6,
    AllHeaders = 5,
    DisplayedCells = 11,
    DisplayedCellsExceptHeaders = 10,
    DisplayedHeaders = 9,
    None = 0,
}

public enum DataGridViewCellBorderStyle
{
    Custom = 0,
    Single = 1,
    Raised = 2,
    Sunken = 3,
    None = 4,
    SingleVertical = 5,
    RaisedVertical = 6,
    SunkenVertical = 7,
    SingleHorizontal = 8,
    RaisedHorizontal = 9,
    SunkenHorizontal = 10,
}

[Flags]
public enum DataGridViewCellStyleScopes
{
    None = 0,
    Cell = 1,
    Column = 2,
    Row = 4,
    DataGridView = 8,
    ColumnHeaders = 16,
    RowHeaders = 32,
    Rows = 64,
    AlternatingRows = 128,
}

public enum DataGridViewClipboardCopyMode
{
    Disable = 0,
    EnableWithAutoHeaderText = 1,
    EnableWithoutHeaderText = 2,
    EnableAlwaysIncludeHeaderText = 3,
}

public enum DataGridViewColumnHeadersHeightSizeMode
{
    EnableResizing = 0,
    DisableResizing = 1,
    AutoSize = 2,
}

public enum DataGridViewColumnSortMode
{
    NotSortable = 0,
    Automatic = 1,
    Programmatic = 2,
}

public enum DataGridViewComboBoxDisplayStyle
{
    ComboBox = 0,
    DropDownButton = 1,
    Nothing = 2,
}

public enum DataGridViewContentAlignment
{
    NotSet = 0,
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

[Flags]
public enum DataGridViewDataErrorContexts
{
    Formatting = 1,
    Display = 2,
    PreferredSize = 4,
    RowDeletion = 8,
    Parsing = 256,
    Commit = 512,
    InitialValueRestoration = 1024,
    LeaveControl = 2048,
    CurrentCellChange = 4096,
    Scroll = 8192,
    ClipboardContent = 16384,
}

public enum DataGridViewEditMode
{
    EditOnEnter = 0,
    EditOnKeystroke = 1,
    EditOnKeystrokeOrF2 = 2,
    EditOnF2 = 3,
    EditProgrammatically = 4,
}

[Flags]
public enum DataGridViewElementStates
{
    None = 0,
    Displayed = 1,
    Frozen = 2,
    ReadOnly = 4,
    Resizable = 8,
    ResizableSet = 16,
    Selected = 32,
    Visible = 64,
}

public enum DataGridViewHeaderBorderStyle
{
    Custom = 0,
    Single = 1,
    Raised = 2,
    Sunken = 3,
    None = 4,
}

public enum DataGridViewHitTestType
{
    None = 0,
    Cell = 1,
    ColumnHeader = 2,
    RowHeader = 3,
    TopLeftHeader = 4,
    HorizontalScrollBar = 5,
    VerticalScrollBar = 6,
}

public enum DataGridViewImageCellLayout
{
    NotSet = 0,
    Normal = 1,
    Stretch = 2,
    Zoom = 3,
}

[Flags]
public enum DataGridViewPaintParts
{
    None = 0,
    All = 127,
    Background = 1,
    Border = 2,
    ContentBackground = 4,
    ContentForeground = 8,
    ErrorIcon = 16,
    Focus = 32,
    SelectionBackground = 64,
}

public enum DataGridViewRowHeadersWidthSizeMode
{
    EnableResizing = 0,
    DisableResizing = 1,
    AutoSizeToAllHeaders = 2,
    AutoSizeToDisplayedHeaders = 3,
    AutoSizeToFirstHeader = 4,
}

public enum DataGridViewSelectionMode
{
    CellSelect = 0,
    FullRowSelect = 1,
    FullColumnSelect = 2,
    RowHeaderSelect = 3,
    ColumnHeaderSelect = 4,
}

public enum DataGridViewTriState
{
    NotSet = 0,
    True = 1,
    False = 2,
}

public enum DataSourceUpdateMode
{
    OnValidation = 0,
    OnPropertyChanged = 1,
    Never = 2,
}

public enum DateTimePickerFormat
{
    Long = 1,
    Short = 2,
    Time = 4,
    Custom = 8,
}

public enum Day
{
    Monday = 0,
    Tuesday = 1,
    Wednesday = 2,
    Thursday = 3,
    Friday = 4,
    Saturday = 5,
    Sunday = 6,
    Default = 7,
}

public enum DialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7,
    TryAgain = 10,
    Continue = 11,
}

public enum DockStyle
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    Fill = 5,
}

public enum DockingBehavior
{
    Never = 0,
    Ask = 1,
    AutoDock = 2,
}

public enum DragAction
{
    Continue = 0,
    Drop = 1,
    Cancel = 2,
}

[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = -2147483648,
    All = -2147483645,
}

[Flags]
public enum DrawItemState
{
    Checked = 8,
    ComboBoxEdit = 4096,
    Default = 32,
    Disabled = 4,
    Focus = 16,
    Grayed = 2,
    HotLight = 64,
    Inactive = 128,
    NoAccelerator = 256,
    NoFocusRect = 512,
    Selected = 1,
    None = 0,
}

public enum DrawMode
{
    Normal = 0,
    OwnerDrawFixed = 1,
    OwnerDrawVariable = 2,
}

public enum DropImageType
{
    Invalid = -1,
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Label = 6,
    Warning = 7,
    NoImage = 8,
}

public enum ErrorBlinkStyle
{
    BlinkIfDifferentError = 0,
    AlwaysBlink = 1,
    NeverBlink = 2,
}

public enum ErrorIconAlignment
{
    TopLeft = 0,
    TopRight = 1,
    MiddleLeft = 2,
    MiddleRight = 3,
    BottomLeft = 4,
    BottomRight = 5,
}

public enum FixedPanel
{
    None = 0,
    Panel1 = 1,
    Panel2 = 2,
}

public enum FlatStyle
{
    Flat = 0,
    Popup = 1,
    Standard = 2,
    System = 3,
}

public enum FlowDirection
{
    LeftToRight = 0,
    TopDown = 1,
    RightToLeft = 2,
    BottomUp = 3,
}

public enum FormBorderStyle
{
    None = 0,
    FixedSingle = 1,
    Fixed3D = 2,
    FixedDialog = 3,
    Sizable = 4,
    FixedToolWindow = 5,
    SizableToolWindow = 6,
}

public enum FormCornerPreference
{
    Default = 0,
    DoNotRound = 1,
    Round = 2,
    RoundSmall = 3,
}

public enum FormStartPosition
{
    Manual = 0,
    CenterScreen = 1,
    WindowsDefaultLocation = 2,
    WindowsDefaultBounds = 3,
    CenterParent = 4,
}

public enum FormWindowState
{
    Normal = 0,
    Minimized = 1,
    Maximized = 2,
}

public enum FrameStyle
{
    Dashed = 0,
    Thick = 1,
}

[Flags]
public enum GetChildAtPointSkip
{
    None = 0,
    Invisible = 1,
    Disabled = 2,
    Transparent = 4,
}

public enum GridItemType
{
    Property = 0,
    Category = 1,
    ArrayValue = 2,
    Root = 3,
}

public enum HelpNavigator
{
    Topic = -2147483647,
    TableOfContents = -2147483646,
    Index = -2147483645,
    Find = -2147483644,
    AssociateIndex = -2147483643,
    KeywordIndex = -2147483642,
    TopicId = -2147483641,
}

public enum HorizontalAlignment
{
    Left = 0,
    Right = 1,
    Center = 2,
}

public enum HtmlElementInsertionOrientation
{
    BeforeBegin = 0,
    AfterBegin = 1,
    BeforeEnd = 2,
    AfterEnd = 3,
}

public enum ImageLayout
{
    None = 0,
    Tile = 1,
    Center = 2,
    Stretch = 3,
    Zoom = 4,
}

public enum ImeMode
{
    Inherit = -1,
    NoControl = 0,
    On = 1,
    Off = 2,
    Disable = 3,
    Hiragana = 4,
    Katakana = 5,
    KatakanaHalf = 6,
    AlphaFull = 7,
    Alpha = 8,
    HangulFull = 9,
    Hangul = 10,
    Close = 11,
    OnHalf = 12,
}

public enum InsertKeyMode
{
    Default = 0,
    Insert = 1,
    Overwrite = 2,
}

public enum ItemActivation
{
    Standard = 0,
    OneClick = 1,
    TwoClick = 2,
}

public enum ItemBoundsPortion
{
    Entire = 0,
    Icon = 1,
    Label = 2,
    ItemOnly = 3,
}

[Flags]
public enum Keys
{
    KeyCode = 65535,
    Modifiers = -65536,
    None = 0,
    LButton = 1,
    RButton = 2,
    Cancel = 3,
    MButton = 4,
    XButton1 = 5,
    XButton2 = 6,
    Back = 8,
    Tab = 9,
    LineFeed = 10,
    Clear = 12,
    Return = 13,
    Enter = 13,
    ShiftKey = 16,
    ControlKey = 17,
    Menu = 18,
    Pause = 19,
    Capital = 20,
    CapsLock = 20,
    KanaMode = 21,
    HanguelMode = 21,
    HangulMode = 21,
    JunjaMode = 23,
    FinalMode = 24,
    HanjaMode = 25,
    KanjiMode = 25,
    Escape = 27,
    IMEConvert = 28,
    IMENonconvert = 29,
    IMEAccept = 30,
    IMEAceept = 30,
    IMEModeChange = 31,
    Space = 32,
    Prior = 33,
    PageUp = 33,
    Next = 34,
    PageDown = 34,
    End = 35,
    Home = 36,
    Left = 37,
    Up = 38,
    Right = 39,
    Down = 40,
    Select = 41,
    Print = 42,
    Execute = 43,
    Snapshot = 44,
    PrintScreen = 44,
    Insert = 45,
    Delete = 46,
    Help = 47,
    D0 = 48,
    D1 = 49,
    D2 = 50,
    D3 = 51,
    D4 = 52,
    D5 = 53,
    D6 = 54,
    D7 = 55,
    D8 = 56,
    D9 = 57,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    LWin = 91,
    RWin = 92,
    Apps = 93,
    Sleep = 95,
    NumPad0 = 96,
    NumPad1 = 97,
    NumPad2 = 98,
    NumPad3 = 99,
    NumPad4 = 100,
    NumPad5 = 101,
    NumPad6 = 102,
    NumPad7 = 103,
    NumPad8 = 104,
    NumPad9 = 105,
    Multiply = 106,
    Add = 107,
    Separator = 108,
    Subtract = 109,
    Decimal = 110,
    Divide = 111,
    F1 = 112,
    F2 = 113,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    F10 = 121,
    F11 = 122,
    F12 = 123,
    F13 = 124,
    F14 = 125,
    F15 = 126,
    F16 = 127,
    F17 = 128,
    F18 = 129,
    F19 = 130,
    F20 = 131,
    F21 = 132,
    F22 = 133,
    F23 = 134,
    F24 = 135,
    NumLock = 144,
    Scroll = 145,
    LShiftKey = 160,
    RShiftKey = 161,
    LControlKey = 162,
    RControlKey = 163,
    LMenu = 164,
    RMenu = 165,
    BrowserBack = 166,
    BrowserForward = 167,
    BrowserRefresh = 168,
    BrowserStop = 169,
    BrowserSearch = 170,
    BrowserFavorites = 171,
    BrowserHome = 172,
    VolumeMute = 173,
    VolumeDown = 174,
    VolumeUp = 175,
    MediaNextTrack = 176,
    MediaPreviousTrack = 177,
    MediaStop = 178,
    MediaPlayPause = 179,
    LaunchMail = 180,
    SelectMedia = 181,
    LaunchApplication1 = 182,
    LaunchApplication2 = 183,
    OemSemicolon = 186,
    Oem1 = 186,
    Oemplus = 187,
    Oemcomma = 188,
    OemMinus = 189,
    OemPeriod = 190,
    OemQuestion = 191,
    Oem2 = 191,
    Oem3 = 192,
    Oemtilde = 192,
    OemOpenBrackets = 219,
    Oem4 = 219,
    OemPipe = 220,
    Oem5 = 220,
    OemCloseBrackets = 221,
    Oem6 = 221,
    Oem7 = 222,
    OemQuotes = 222,
    Oem8 = 223,
    Oem102 = 226,
    OemBackslash = 226,
    ProcessKey = 229,
    Packet = 231,
    Attn = 246,
    Crsel = 247,
    Exsel = 248,
    EraseEof = 249,
    Play = 250,
    Zoom = 251,
    NoName = 252,
    Pa1 = 253,
    OemClear = 254,
    Shift = 65536,
    Control = 131072,
    Alt = 262144,
}

public enum LeftRightAlignment
{
    Left = 0,
    Right = 1,
}

public enum LinkBehavior
{
    SystemDefault = 0,
    AlwaysUnderline = 1,
    HoverUnderline = 2,
    NeverUnderline = 3,
}

public enum LinkState
{
    Normal = 0,
    Hover = 1,
    Active = 2,
    Visited = 4,
}

public enum ListViewAlignment
{
    Default = 0,
    Top = 2,
    Left = 1,
    SnapToGrid = 5,
}

public enum ListViewGroupCollapsedState
{
    Default = 0,
    Expanded = 1,
    Collapsed = 2,
}

[Flags]
public enum ListViewHitTestLocations
{
    None = 1,
    AboveClientArea = 256,
    BelowClientArea = 16,
    LeftOfClientArea = 64,
    RightOfClientArea = 32,
    Image = 2,
    StateImage = 512,
    Label = 4,
}

[Flags]
public enum ListViewItemStates
{
    Checked = 8,
    Default = 32,
    Focused = 16,
    Grayed = 2,
    Hot = 64,
    Indeterminate = 256,
    Marked = 128,
    Selected = 1,
    ShowKeyboardCues = 512,
}

public enum MaskFormat
{
    IncludePrompt = 1,
    IncludeLiterals = 2,
    IncludePromptAndLiterals = 3,
    ExcludePromptAndLiterals = 0,
}

public enum MdiLayout
{
    Cascade = 0,
    TileHorizontal = 1,
    TileVertical = 2,
    ArrangeIcons = 3,
}

public enum MenuGlyph
{
    Arrow = 0,
    Checkmark = 1,
    Bullet = 2,
    Min = 0,
    Max = 2,
}

public enum MenuMerge
{
    Add = 0,
    Replace = 1,
    MergeItems = 2,
    Remove = 3,
}

public enum MergeAction
{
    Append = 0,
    Insert = 1,
    Replace = 2,
    Remove = 3,
    MatchOnly = 4,
}

public enum MessageBoxButtons
{
    OK = 0,
    OKCancel = 1,
    AbortRetryIgnore = 2,
    YesNoCancel = 3,
    YesNo = 4,
    RetryCancel = 5,
    CancelTryContinue = 6,
}

public enum MessageBoxDefaultButton
{
    Button1 = 0,
    Button2 = 256,
    Button3 = 512,
    Button4 = 768,
}

public enum MessageBoxIcon
{
    None = 0,
    Hand = 16,
    Question = 32,
    Exclamation = 48,
    Asterisk = 64,
    Stop = 16,
    Error = 16,
    Warning = 48,
    Information = 64,
}

[Flags]
public enum MessageBoxOptions
{
    ServiceNotification = 2097152,
    DefaultDesktopOnly = 131072,
    RightAlign = 524288,
    RtlReading = 1048576,
}

[Flags]
public enum MouseButtons
{
    Left = 1048576,
    None = 0,
    Right = 2097152,
    Middle = 4194304,
    XButton1 = 8388608,
    XButton2 = 16777216,
}

public enum Orientation
{
    Horizontal = 0,
    Vertical = 1,
}

public enum PictureBoxSizeMode
{
    Normal = 0,
    StretchImage = 1,
    AutoSize = 2,
    CenterImage = 3,
    Zoom = 4,
}

public enum PowerLineStatus
{
    Offline = 0,
    Online = 1,
    Unknown = 255,
}

public enum PowerState
{
    Suspend = 0,
    Hibernate = 1,
}

public enum PreProcessControlState
{
    MessageProcessed = 0,
    MessageNeeded = 1,
    MessageNotNeeded = 2,
}

public enum ProgressBarStyle
{
    Blocks = 0,
    Continuous = 1,
    Marquee = 2,
}

[Flags]
public enum PropertySort
{
    NoSort = 0,
    Alphabetical = 1,
    Categorized = 2,
    CategorizedAlphabetical = 3,
}

[Flags]
public enum RichTextBoxFinds
{
    None = 0,
    WholeWord = 2,
    MatchCase = 4,
    NoHighlight = 8,
    Reverse = 16,
}

[Flags]
public enum RichTextBoxLanguageOptions
{
    AutoFont = 2,
    AutoFontSizeAdjust = 16,
    AutoKeyboard = 1,
    DualFont = 128,
    ImeAlwaysSendNotify = 8,
    ImeCancelComplete = 4,
    UIFonts = 32,
}

public enum RichTextBoxScrollBars
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3,
    ForcedHorizontal = 17,
    ForcedVertical = 18,
    ForcedBoth = 19,
}

public enum RichTextBoxSelectionAttribute
{
    Mixed = -1,
    None = 0,
    All = 1,
}

[Flags]
public enum RichTextBoxSelectionTypes
{
    Empty = 0,
    Text = 1,
    Object = 2,
    MultiChar = 4,
    MultiObject = 8,
}

public enum RichTextBoxStreamType
{
    RichText = 0,
    PlainText = 1,
    RichNoOleObjs = 2,
    TextTextOleObjs = 3,
    UnicodePlainText = 4,
}

public enum RichTextBoxWordPunctuations
{
    Level1 = 128,
    Level2 = 256,
    Custom = 512,
    All = 896,
}

public enum RightToLeft
{
    No = 0,
    Yes = 1,
    Inherit = 2,
}

public enum ScreenCaptureMode
{
    Allow = 0,
    HideContent = 1,
    HideWindow = 2,
}

public enum ScrollBars
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3,
}

public enum ScrollButton
{
    Down = 1,
    Left = 2,
    Right = 3,
    Up = 0,
    Min = 0,
    Max = 3,
}

public enum ScrollEventType
{
    SmallDecrement = 0,
    SmallIncrement = 1,
    LargeDecrement = 2,
    LargeIncrement = 3,
    ThumbPosition = 4,
    ThumbTrack = 5,
    First = 6,
    Last = 7,
    EndScroll = 8,
}

public enum ScrollOrientation
{
    HorizontalScroll = 0,
    VerticalScroll = 1,
}

public enum SearchDirectionHint
{
    Up = 38,
    Down = 40,
    Left = 37,
    Right = 39,
}

public enum SecurityIDType
{
    User = 1,
    Group = 2,
    Domain = 3,
    Alias = 4,
    WellKnownGroup = 5,
    DeletedAccount = 6,
    Invalid = 7,
    Unknown = 8,
    Computer = 9,
}

public enum SelectionMode
{
    None = 0,
    One = 1,
    MultiSimple = 2,
    MultiExtended = 3,
}

public enum Shortcut
{
    None = 0,
    CtrlA = 131137,
    CtrlB = 131138,
    CtrlC = 131139,
    CtrlD = 131140,
    CtrlE = 131141,
    CtrlF = 131142,
    CtrlG = 131143,
    CtrlH = 131144,
    CtrlI = 131145,
    CtrlJ = 131146,
    CtrlK = 131147,
    CtrlL = 131148,
    CtrlM = 131149,
    CtrlN = 131150,
    CtrlO = 131151,
    CtrlP = 131152,
    CtrlQ = 131153,
    CtrlR = 131154,
    CtrlS = 131155,
    CtrlT = 131156,
    CtrlU = 131157,
    CtrlV = 131158,
    CtrlW = 131159,
    CtrlX = 131160,
    CtrlY = 131161,
    CtrlZ = 131162,
    CtrlShiftA = 196673,
    CtrlShiftB = 196674,
    CtrlShiftC = 196675,
    CtrlShiftD = 196676,
    CtrlShiftE = 196677,
    CtrlShiftF = 196678,
    CtrlShiftG = 196679,
    CtrlShiftH = 196680,
    CtrlShiftI = 196681,
    CtrlShiftJ = 196682,
    CtrlShiftK = 196683,
    CtrlShiftL = 196684,
    CtrlShiftM = 196685,
    CtrlShiftN = 196686,
    CtrlShiftO = 196687,
    CtrlShiftP = 196688,
    CtrlShiftQ = 196689,
    CtrlShiftR = 196690,
    CtrlShiftS = 196691,
    CtrlShiftT = 196692,
    CtrlShiftU = 196693,
    CtrlShiftV = 196694,
    CtrlShiftW = 196695,
    CtrlShiftX = 196696,
    CtrlShiftY = 196697,
    CtrlShiftZ = 196698,
    F1 = 112,
    F2 = 113,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    F10 = 121,
    F11 = 122,
    F12 = 123,
    ShiftF1 = 65648,
    ShiftF2 = 65649,
    ShiftF3 = 65650,
    ShiftF4 = 65651,
    ShiftF5 = 65652,
    ShiftF6 = 65653,
    ShiftF7 = 65654,
    ShiftF8 = 65655,
    ShiftF9 = 65656,
    ShiftF10 = 65657,
    ShiftF11 = 65658,
    ShiftF12 = 65659,
    CtrlF1 = 131184,
    CtrlF2 = 131185,
    CtrlF3 = 131186,
    CtrlF4 = 131187,
    CtrlF5 = 131188,
    CtrlF6 = 131189,
    CtrlF7 = 131190,
    CtrlF8 = 131191,
    CtrlF9 = 131192,
    CtrlF10 = 131193,
    CtrlF11 = 131194,
    CtrlF12 = 131195,
    CtrlShiftF1 = 196720,
    CtrlShiftF2 = 196721,
    CtrlShiftF3 = 196722,
    CtrlShiftF4 = 196723,
    CtrlShiftF5 = 196724,
    CtrlShiftF6 = 196725,
    CtrlShiftF7 = 196726,
    CtrlShiftF8 = 196727,
    CtrlShiftF9 = 196728,
    CtrlShiftF10 = 196729,
    CtrlShiftF11 = 196730,
    CtrlShiftF12 = 196731,
    Ins = 45,
    CtrlIns = 131117,
    ShiftIns = 65581,
    Del = 46,
    CtrlDel = 131118,
    ShiftDel = 65582,
    AltRightArrow = 262183,
    AltLeftArrow = 262181,
    AltUpArrow = 262182,
    AltDownArrow = 262184,
    AltBksp = 262152,
    AltF1 = 262256,
    AltF2 = 262257,
    AltF3 = 262258,
    AltF4 = 262259,
    AltF5 = 262260,
    AltF6 = 262261,
    AltF7 = 262262,
    AltF8 = 262263,
    AltF9 = 262264,
    AltF10 = 262265,
    AltF11 = 262266,
    AltF12 = 262267,
    Alt0 = 262192,
    Alt1 = 262193,
    Alt2 = 262194,
    Alt3 = 262195,
    Alt4 = 262196,
    Alt5 = 262197,
    Alt6 = 262198,
    Alt7 = 262199,
    Alt8 = 262200,
    Alt9 = 262201,
    Ctrl0 = 131120,
    Ctrl1 = 131121,
    Ctrl2 = 131122,
    Ctrl3 = 131123,
    Ctrl4 = 131124,
    Ctrl5 = 131125,
    Ctrl6 = 131126,
    Ctrl7 = 131127,
    Ctrl8 = 131128,
    Ctrl9 = 131129,
    CtrlShift0 = 196656,
    CtrlShift1 = 196657,
    CtrlShift2 = 196658,
    CtrlShift3 = 196659,
    CtrlShift4 = 196660,
    CtrlShift5 = 196661,
    CtrlShift6 = 196662,
    CtrlShift7 = 196663,
    CtrlShift8 = 196664,
    CtrlShift9 = 196665,
}

public enum SizeGripStyle
{
    Auto = 0,
    Show = 1,
    Hide = 2,
}

public enum SizeType
{
    AutoSize = 0,
    Absolute = 1,
    Percent = 2,
}

public enum SortOrder
{
    None = 0,
    Ascending = 1,
    Descending = 2,
}

public enum StatusBarPanelAutoSize
{
    None = 1,
    Spring = 2,
    Contents = 3,
}

public enum StatusBarPanelBorderStyle
{
    None = 1,
    Raised = 2,
    Sunken = 3,
}

public enum StatusBarPanelStyle
{
    Text = 1,
    OwnerDraw = 2,
}

public enum StructFormat
{
    Ansi = 1,
    Unicode = 2,
    Auto = 3,
}

public enum SystemColorMode
{
    Classic = 0,
    System = 1,
    Dark = 2,
}

public enum SystemParameter
{
    DropShadow = 0,
    FlatMenu = 1,
    FontSmoothingContrastMetric = 2,
    FontSmoothingTypeMetric = 3,
    MenuFadeEnabled = 4,
    SelectionFade = 5,
    ToolTipAnimationMetric = 6,
    UIEffects = 7,
    CaretWidthMetric = 8,
    VerticalFocusThicknessMetric = 9,
    HorizontalFocusThicknessMetric = 10,
}

public enum TabAlignment
{
    Top = 0,
    Bottom = 1,
    Left = 2,
    Right = 3,
}

public enum TabAppearance
{
    Normal = 0,
    Buttons = 1,
    FlatButtons = 2,
}

public enum TabControlAction
{
    Selecting = 0,
    Selected = 1,
    Deselecting = 2,
    Deselected = 3,
}

public enum TabDrawMode
{
    Normal = 0,
    OwnerDrawFixed = 1,
}

public enum TabSizeMode
{
    Normal = 0,
    FillToRight = 1,
    Fixed = 2,
}

public enum TableLayoutPanelCellBorderStyle
{
    None = 0,
    Single = 1,
    Inset = 2,
    InsetDouble = 3,
    Outset = 4,
    OutsetDouble = 5,
    OutsetPartial = 6,
}

public enum TableLayoutPanelGrowStyle
{
    FixedSize = 0,
    AddRows = 1,
    AddColumns = 2,
}

public enum TaskDialogExpanderPosition
{
    AfterText = 0,
    AfterFootnote = 1,
}

public enum TaskDialogProgressBarState
{
    Normal = 0,
    Paused = 1,
    Error = 2,
    Marquee = 3,
    MarqueePaused = 4,
    None = 5,
}

public enum TaskDialogStartupLocation
{
    CenterScreen = 0,
    CenterOwner = 1,
}

public enum TextDataFormat
{
    Text = 0,
    UnicodeText = 1,
    Rtf = 2,
    Html = 3,
    CommaSeparatedValue = 4,
}

[Flags]
public enum TextFormatFlags
{
    Bottom = 8,
    EndEllipsis = 32768,
    ExpandTabs = 64,
    ExternalLeading = 512,
    Default = 0,
    HidePrefix = 1048576,
    HorizontalCenter = 1,
    Internal = 4096,
    Left = 0,
    ModifyString = 65536,
    NoClipping = 256,
    NoPrefix = 2048,
    NoFullWidthCharacterBreak = 524288,
    PathEllipsis = 16384,
    PrefixOnly = 2097152,
    Right = 2,
    RightToLeft = 131072,
    SingleLine = 32,
    TextBoxControl = 8192,
    Top = 0,
    VerticalCenter = 4,
    WordBreak = 16,
    WordEllipsis = 262144,
    PreserveGraphicsClipping = 16777216,
    PreserveGraphicsTranslateTransform = 33554432,
    GlyphOverhangPadding = 0,
    NoPadding = 268435456,
    LeftAndRightPadding = 536870912,
}

public enum TextImageRelation
{
    Overlay = 0,
    ImageBeforeText = 4,
    TextBeforeImage = 8,
    ImageAboveText = 1,
    TextAboveImage = 2,
}

public enum TickStyle
{
    None = 0,
    TopLeft = 1,
    BottomRight = 2,
    Both = 3,
}

public enum ToolBarAppearance
{
    Normal = 0,
    Flat = 1,
}

public enum ToolBarButtonStyle
{
    PushButton = 1,
    ToggleButton = 2,
    Separator = 3,
    DropDownButton = 4,
}

public enum ToolBarTextAlign
{
    Underneath = 0,
    Right = 1,
}

public enum ToolStripDropDownCloseReason
{
    AppFocusChange = 0,
    AppClicked = 1,
    ItemClicked = 2,
    Keyboard = 3,
    CloseCalled = 4,
}

public enum ToolStripDropDownDirection
{
    AboveLeft = 0,
    AboveRight = 1,
    BelowLeft = 2,
    BelowRight = 3,
    Left = 4,
    Right = 5,
    Default = 7,
}

public enum ToolStripGripDisplayStyle
{
    Horizontal = 0,
    Vertical = 1,
}

public enum ToolStripGripStyle
{
    Hidden = 0,
    Visible = 1,
}

public enum ToolStripItemAlignment
{
    Left = 0,
    Right = 1,
}

public enum ToolStripItemDisplayStyle
{
    None = 0,
    Text = 1,
    Image = 2,
    ImageAndText = 3,
}

public enum ToolStripItemImageScaling
{
    None = 0,
    SizeToFit = 1,
}

public enum ToolStripItemOverflow
{
    Never = 0,
    Always = 1,
    AsNeeded = 2,
}

public enum ToolStripItemPlacement
{
    Main = 0,
    Overflow = 1,
    None = 2,
}

public enum ToolStripLayoutStyle
{
    StackWithOverflow = 0,
    HorizontalStackWithOverflow = 1,
    VerticalStackWithOverflow = 2,
    Flow = 3,
    Table = 4,
}

public enum ToolStripManagerRenderMode
{
    Custom = 0,
    System = 1,
    Professional = 2,
}

public enum ToolStripRenderMode
{
    Custom = 0,
    System = 1,
    Professional = 2,
    ManagerRenderMode = 3,
}

[Flags]
public enum ToolStripStatusLabelBorderSides
{
    All = 15,
    Bottom = 8,
    Left = 1,
    Right = 4,
    Top = 2,
    None = 0,
}

public enum ToolStripTextDirection
{
    Inherit = 0,
    Horizontal = 1,
    Vertical90 = 2,
    Vertical270 = 3,
}

public enum ToolTipIcon
{
    None = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

[Flags]
public enum TreeNodeStates
{
    Checked = 8,
    Default = 32,
    Focused = 16,
    Grayed = 2,
    Hot = 64,
    Indeterminate = 256,
    Marked = 128,
    Selected = 1,
    ShowKeyboardCues = 512,
}

public enum TreeViewAction
{
    Unknown = 0,
    ByKeyboard = 1,
    ByMouse = 2,
    Collapse = 3,
    Expand = 4,
}

public enum TreeViewDrawMode
{
    Normal = 0,
    OwnerDrawText = 1,
    OwnerDrawAll = 2,
}

[Flags]
public enum TreeViewHitTestLocations
{
    None = 1,
    Image = 2,
    Label = 4,
    Indent = 8,
    AboveClientArea = 256,
    BelowClientArea = 512,
    LeftOfClientArea = 2048,
    RightOfClientArea = 1024,
    RightOfLabel = 32,
    StateImage = 64,
    PlusMinus = 16,
}

[Flags]
public enum UICues
{
    ShowFocus = 1,
    ShowKeyboard = 2,
    Shown = 3,
    ChangeFocus = 4,
    ChangeKeyboard = 8,
    Changed = 12,
    None = 0,
}

public enum UnhandledExceptionMode
{
    Automatic = 0,
    ThrowException = 1,
    CatchException = 2,
}

[Flags]
public enum ValidationConstraints
{
    None = 0,
    Selectable = 1,
    Enabled = 2,
    Visible = 4,
    TabStop = 8,
    ImmediateChildren = 16,
}

public enum View
{
    LargeIcon = 0,
    Details = 1,
    SmallIcon = 2,
    List = 3,
    Tile = 4,
}

public enum WebBrowserEncryptionLevel
{
    Insecure = 0,
    Mixed = 1,
    Unknown = 2,
    Bit40 = 3,
    Bit56 = 4,
    Fortezza = 5,
    Bit128 = 6,
}

public enum WebBrowserReadyState
{
    Uninitialized = 0,
    Loading = 1,
    Loaded = 2,
    Interactive = 3,
    Complete = 4,
}

public enum WebBrowserRefreshOption
{
    Normal = 0,
    IfExpired = 1,
    Continue = 2,
    Completely = 3,
}
