// The enumerations of this namespace, with the names and values of Microsoft's
// System.Windows.Forms reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Windows.Forms.VisualStyles;

public enum BackgroundType
{
    ImageFile = 0,
    BorderFill = 1,
    None = 2,
}

public enum BooleanProperty
{
    Transparent = 2201,
    AutoSize = 2202,
    BorderOnly = 2203,
    Composited = 2204,
    BackgroundFill = 2205,
    GlyphTransparent = 2206,
    GlyphOnly = 2207,
    AlwaysShowSizingBar = 2208,
    MirrorImage = 2209,
    UniformSizing = 2210,
    IntegralSizing = 2211,
    SourceGrow = 2212,
    SourceShrink = 2213,
}

public enum BorderType
{
    Rectangle = 0,
    RoundedRectangle = 1,
    Ellipse = 2,
}

public enum CheckBoxState
{
    UncheckedNormal = 1,
    UncheckedHot = 2,
    UncheckedPressed = 3,
    UncheckedDisabled = 4,
    CheckedNormal = 5,
    CheckedHot = 6,
    CheckedPressed = 7,
    CheckedDisabled = 8,
    MixedNormal = 9,
    MixedHot = 10,
    MixedPressed = 11,
    MixedDisabled = 12,
}

public enum ColorProperty
{
    BorderColor = 3801,
    FillColor = 3802,
    TextColor = 3803,
    EdgeLightColor = 3804,
    EdgeHighlightColor = 3805,
    EdgeShadowColor = 3806,
    EdgeDarkShadowColor = 3807,
    EdgeFillColor = 3808,
    TransparentColor = 3809,
    GradientColor1 = 3810,
    GradientColor2 = 3811,
    GradientColor3 = 3812,
    GradientColor4 = 3813,
    GradientColor5 = 3814,
    ShadowColor = 3815,
    GlowColor = 3816,
    TextBorderColor = 3817,
    TextShadowColor = 3818,
    GlyphTextColor = 3819,
    GlyphTransparentColor = 3820,
    FillColorHint = 3821,
    BorderColorHint = 3822,
    AccentColorHint = 3823,
}

public enum ComboBoxState
{
    Normal = 1,
    Hot = 2,
    Pressed = 3,
    Disabled = 4,
    Focused = 5,
}

public enum ContentAlignment
{
    Left = 0,
    Center = 1,
    Right = 2,
}

[Flags]
public enum EdgeEffects
{
    None = 0,
    FillInterior = 2048,
    Flat = 16384,
    Soft = 4096,
    Mono = 32768,
}

public enum EdgeStyle
{
    Raised = 5,
    Sunken = 10,
    Etched = 6,
    Bump = 9,
}

[Flags]
public enum Edges
{
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Diagonal = 16,
}

public enum EnumProperty
{
    BackgroundType = 4001,
    BorderType = 4002,
    FillType = 4003,
    SizingType = 4004,
    HorizontalAlignment = 4005,
    ContentAlignment = 4006,
    VerticalAlignment = 4007,
    OffsetType = 4008,
    IconEffect = 4009,
    TextShadowType = 4010,
    ImageLayout = 4011,
    GlyphType = 4012,
    ImageSelectType = 4013,
    GlyphFontSizingType = 4014,
    TrueSizeScalingType = 4015,
}

public enum FilenameProperty
{
    ImageFile = 3001,
    ImageFile1 = 3002,
    ImageFile2 = 3003,
    ImageFile3 = 3004,
    ImageFile4 = 3005,
    ImageFile5 = 3006,
    StockImageFile = 3007,
    GlyphImageFile = 3008,
}

public enum FillType
{
    Solid = 0,
    VerticalGradient = 1,
    HorizontalGradient = 2,
    RadialGradient = 3,
    TileImage = 4,
}

public enum FontProperty
{
    TextFont = 210,
    GlyphFont = 2601,
}

public enum GlyphFontSizingType
{
    None = 0,
    Size = 1,
    Dpi = 2,
}

public enum GlyphType
{
    None = 0,
    ImageGlyph = 1,
    FontGlyph = 2,
}

public enum GroupBoxState
{
    Normal = 1,
    Disabled = 2,
}

public enum HitTestCode
{
    Nowhere = 0,
    Client = 1,
    Left = 10,
    Right = 11,
    Top = 12,
    Bottom = 15,
    TopLeft = 13,
    TopRight = 14,
    BottomLeft = 16,
    BottomRight = 17,
}

[Flags]
public enum HitTestOptions
{
    BackgroundSegment = 0,
    FixedBorder = 2,
    Caption = 4,
    ResizingBorderLeft = 16,
    ResizingBorderTop = 32,
    ResizingBorderRight = 64,
    ResizingBorderBottom = 128,
    ResizingBorder = 240,
    SizingTemplate = 256,
    SystemSizingMargins = 512,
}

public enum HorizontalAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
}

public enum IconEffect
{
    None = 0,
    Glow = 1,
    Shadow = 2,
    Pulse = 3,
    Alpha = 4,
}

public enum ImageOrientation
{
    Vertical = 0,
    Horizontal = 1,
}

public enum ImageSelectType
{
    None = 0,
    Size = 1,
    Dpi = 2,
}

public enum IntegerProperty
{
    ImageCount = 2401,
    AlphaLevel = 2402,
    BorderSize = 2403,
    RoundCornerWidth = 2404,
    RoundCornerHeight = 2405,
    GradientRatio1 = 2406,
    GradientRatio2 = 2407,
    GradientRatio3 = 2408,
    GradientRatio4 = 2409,
    GradientRatio5 = 2410,
    ProgressChunkSize = 2411,
    ProgressSpaceSize = 2412,
    Saturation = 2413,
    TextBorderSize = 2414,
    AlphaThreshold = 2415,
    Width = 2416,
    Height = 2417,
    GlyphIndex = 2418,
    TrueSizeStretchMark = 2419,
    MinDpi1 = 2420,
    MinDpi2 = 2421,
    MinDpi3 = 2422,
    MinDpi4 = 2423,
    MinDpi5 = 2424,
}

public enum MarginProperty
{
    SizingMargins = 3601,
    ContentMargins = 3602,
    CaptionMargins = 3603,
}

public enum OffsetType
{
    TopLeft = 0,
    TopRight = 1,
    TopMiddle = 2,
    BottomLeft = 3,
    BottomRight = 4,
    BottomMiddle = 5,
    MiddleLeft = 6,
    MiddleRight = 7,
    LeftOfCaption = 8,
    RightOfCaption = 9,
    LeftOfLastButton = 10,
    RightOfLastButton = 11,
    AboveLastButton = 12,
    BelowLastButton = 13,
}

public enum PointProperty
{
    Offset = 3401,
    TextShadowOffset = 3402,
    MinSize = 3403,
    MinSize1 = 3404,
    MinSize2 = 3405,
    MinSize3 = 3406,
    MinSize4 = 3407,
    MinSize5 = 3408,
}

public enum PushButtonState
{
    Normal = 1,
    Hot = 2,
    Pressed = 3,
    Disabled = 4,
    Default = 5,
}

public enum RadioButtonState
{
    UncheckedNormal = 1,
    UncheckedHot = 2,
    UncheckedPressed = 3,
    UncheckedDisabled = 4,
    CheckedNormal = 5,
    CheckedHot = 6,
    CheckedPressed = 7,
    CheckedDisabled = 8,
}

public enum ScrollBarArrowButtonState
{
    UpNormal = 1,
    UpHot = 2,
    UpPressed = 3,
    UpDisabled = 4,
    DownNormal = 5,
    DownHot = 6,
    DownPressed = 7,
    DownDisabled = 8,
    LeftNormal = 9,
    LeftHot = 10,
    LeftPressed = 11,
    LeftDisabled = 12,
    RightNormal = 13,
    RightHot = 14,
    RightPressed = 15,
    RightDisabled = 16,
}

public enum ScrollBarSizeBoxState
{
    RightAlign = 1,
    LeftAlign = 2,
}

public enum ScrollBarState
{
    Normal = 1,
    Hot = 2,
    Pressed = 3,
    Disabled = 4,
}

public enum SizingType
{
    FixedSize = 0,
    Stretch = 1,
    Tile = 2,
}

public enum StringProperty
{
    Text = 3201,
}

public enum TabItemState
{
    Normal = 1,
    Hot = 2,
    Selected = 3,
    Disabled = 4,
}

public enum TextBoxState
{
    Normal = 1,
    Hot = 2,
    Selected = 3,
    Disabled = 4,
    Readonly = 6,
    Assist = 7,
}

public enum TextShadowType
{
    None = 0,
    Single = 1,
    Continuous = 2,
}

public enum ToolBarState
{
    Normal = 1,
    Hot = 2,
    Pressed = 3,
    Disabled = 4,
    Checked = 5,
    HotChecked = 6,
}

public enum TrackBarThumbState
{
    Normal = 1,
    Hot = 2,
    Pressed = 3,
    Disabled = 5,
}

public enum TrueSizeScalingType
{
    None = 0,
    Size = 1,
    Dpi = 2,
}

public enum VerticalAlignment
{
    Top = 0,
    Center = 1,
    Bottom = 2,
}

[Flags]
public enum VisualStyleState
{
    NoneEnabled = 0,
    ClientAreaEnabled = 2,
    NonClientAreaEnabled = 1,
    ClientAndNonClientAreasEnabled = 3,
}
