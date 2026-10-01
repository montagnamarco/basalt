namespace System.Windows.Forms.VisualStyles;

/// <summary>The font metrics a visual style reports; a plain record of values.</summary>
public struct TextMetrics
{
    public int Height { get; set; }

    public int Ascent { get; set; }

    public int Descent { get; set; }

    public int InternalLeading { get; set; }

    public int ExternalLeading { get; set; }

    public int AverageCharWidth { get; set; }

    public int MaxCharWidth { get; set; }

    public int Weight { get; set; }

    public int Overhang { get; set; }

    public int DigitizedAspectX { get; set; }

    public int DigitizedAspectY { get; set; }

    public char FirstChar { get; set; }

    public char LastChar { get; set; }

    public char DefaultChar { get; set; }

    public char BreakChar { get; set; }

    public bool Italic { get; set; }

    public bool Underlined { get; set; }

    public bool StruckOut { get; set; }

    public TextMetricsPitchAndFamilyValues PitchAndFamily { get; set; }

    public TextMetricsCharacterSet CharSet { get; set; }
}
