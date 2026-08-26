using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Basalt.Vb6.Runtime;

/// <summary>
/// What every Visual Basic 6 control had.
/// </summary>
/// <remarks>
/// A wrapper over an Avalonia control rather than a control of its own: the
/// drawing, the input and the layout are Avalonia's, and this is the surface
/// the translated code names. Twenty-year-old code says txtNome.Text and
/// cmdOk.Enabled, and both have to mean what they meant.
///
/// Positions and sizes are in twips, because that is what the .frm holds and
/// what the code arithmetic assumes: a form that moved a control by 120 moved
/// it by an eighth of an inch, and a runtime measuring in pixels would move it
/// eight times too far.
/// </remarks>
public abstract class Vb6Control
{
    /// <summary>Twips to device-independent pixels.</summary>
    public const double TwipsPerPixel = 15.0;

    /// <summary>The Avalonia control underneath.</summary>
    public abstract Control Native { get; }

    /// <summary>The name the form gave this control.</summary>
    public string Name
    {
        get => Native.Name ?? "";
        set => Native.Name = value;
    }

    public double Left
    {
        get => Canvas.GetLeft(Native) * TwipsPerPixel;
        set => Canvas.SetLeft(Native, value / TwipsPerPixel);
    }

    public double Top
    {
        get => Canvas.GetTop(Native) * TwipsPerPixel;
        set => Canvas.SetTop(Native, value / TwipsPerPixel);
    }

    public double Width
    {
        get => Native.Width * TwipsPerPixel;
        set => Native.Width = value / TwipsPerPixel;
    }

    public double Height
    {
        get => Native.Height * TwipsPerPixel;
        set => Native.Height = value / TwipsPerPixel;
    }

    public bool Enabled
    {
        get => Native.IsEnabled;
        set => Native.IsEnabled = value;
    }

    public bool Visible
    {
        get => Native.IsVisible;
        set => Native.IsVisible = value;
    }

    public object? Tag
    {
        get => Native.Tag;
        set => Native.Tag = value;
    }

    /// <summary>
    /// Colours, as Visual Basic 6 wrote them.
    /// </summary>
    /// <remarks>
    /// A VB6 colour is a 32-bit BGR value, not RGB: &amp;HFF is red there and
    /// blue everywhere else. A form that set BackColor from a literal would
    /// come out with its colours swapped without this.
    /// </remarks>
    public int BackColor
    {
        get => ToVb6(BackgroundOf());
        set => SetBackground(FromVb6(value));
    }

    public int ForeColor
    {
        get => ToVb6(ForegroundOf());
        set => SetForeground(FromVb6(value));
    }

    /// <summary>Gives this control the keyboard.</summary>
    public void SetFocus() => Native.Focus();

    /// <summary>Draws it again, which Avalonia does on its own.</summary>
    /// <remarks>
    /// Kept because Visual Basic 6 code calls it constantly and leaving it out
    /// would fail the translation for a call that needs to do nothing.
    /// </remarks>
    public void Refresh() => Native.InvalidateVisual();

    protected virtual IBrush? BackgroundOf() =>
        Native is TemplatedControl templated ? templated.Background : null;

    protected virtual void SetBackground(IBrush brush)
    {
        if (Native is TemplatedControl templated) templated.Background = brush;
    }

    protected virtual IBrush? ForegroundOf() =>
        Native is TemplatedControl templated ? templated.Foreground : null;

    protected virtual void SetForeground(IBrush brush)
    {
        if (Native is TemplatedControl templated) templated.Foreground = brush;
    }

    /// <summary>An Avalonia brush from a Visual Basic 6 colour.</summary>
    internal static IBrush FromVb6(int colour) =>
        new SolidColorBrush(Color.FromRgb(
            (byte)(colour & 0xFF),
            (byte)((colour >> 8) & 0xFF),
            (byte)((colour >> 16) & 0xFF)));

    /// <summary>A Visual Basic 6 colour from a brush.</summary>
    internal static int ToVb6(IBrush? brush)
    {
        if (brush is not ISolidColorBrush solid) return 0;

        var c = solid.Color;

        return c.R | (c.G << 8) | (c.B << 16);
    }
}

/// <summary>A control the author types into.</summary>
public sealed class Vb6TextBox : Vb6Control
{
    private readonly TextBox _native = new();

    public override Control Native => _native;

    public string Text
    {
        get => _native.Text ?? "";
        set => _native.Text = value;
    }

    /// <summary>Raised when the text has changed.</summary>
    public event EventHandler? Change;

    public Vb6TextBox() =>
        _native.TextChanged += (_, _) => Change?.Invoke(this, EventArgs.Empty);
}

/// <summary>A button.</summary>
public sealed class Vb6CommandButton : Vb6Control
{
    private readonly Button _native = new();

    public override Control Native => _native;

    /// <summary>The text on the button.</summary>
    /// <remarks>
    /// Caption, not Content: this is the name twenty-year-old code uses, and
    /// the whole point of the wrapper is that it still works.
    /// </remarks>
    public string Caption
    {
        get => _native.Content?.ToString() ?? "";
        set => _native.Content = value;
    }

    public event EventHandler? Click;

    public Vb6CommandButton() =>
        _native.Click += (_, _) => Click?.Invoke(this, EventArgs.Empty);
}

/// <summary>Text on the form that nobody types into.</summary>
public sealed class Vb6Label : Vb6Control
{
    private readonly TextBlock _native = new() { VerticalAlignment = VerticalAlignment.Center };

    public override Control Native => _native;

    public string Caption
    {
        get => _native.Text ?? "";
        set => _native.Text = value;
    }

    protected override IBrush? BackgroundOf() => _native.Background;
    protected override void SetBackground(IBrush brush) => _native.Background = brush;
    protected override IBrush? ForegroundOf() => _native.Foreground;
    protected override void SetForeground(IBrush brush) => _native.Foreground = brush;
}

/// <summary>A box that is either ticked or not.</summary>
public sealed class Vb6CheckBox : Vb6Control
{
    private readonly CheckBox _native = new();

    public override Control Native => _native;

    public string Caption
    {
        get => _native.Content?.ToString() ?? "";
        set => _native.Content = value;
    }

    /// <summary>
    /// Ticked or not, as Visual Basic 6 counted it.
    /// </summary>
    /// <remarks>
    /// 0 and 1, not False and True: VB6 had a third state and code tests
    /// Value = 1 rather than Value = True. Returning a Boolean would make
    /// every such test compile and never match.
    /// </remarks>
    public int Value
    {
        get => _native.IsChecked == true ? 1 : 0;
        set => _native.IsChecked = value != 0;
    }

    public event EventHandler? Click;

    public Vb6CheckBox()
    {
        _native.IsCheckedChanged += (_, _) => Click?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>One of a set, of which only one can be chosen.</summary>
public sealed class Vb6OptionButton : Vb6Control
{
    private readonly RadioButton _native = new();

    public override Control Native => _native;

    public string Caption
    {
        get => _native.Content?.ToString() ?? "";
        set => _native.Content = value;
    }

    public bool Value
    {
        get => _native.IsChecked == true;
        set => _native.IsChecked = value;
    }

    public event EventHandler? Click;

    public Vb6OptionButton() =>
        _native.IsCheckedChanged += (_, _) => Click?.Invoke(this, EventArgs.Empty);
}

/// <summary>A list of things to choose from.</summary>
public sealed class Vb6ListBox : Vb6Control
{
    private readonly ListBox _native = new();
    private readonly List<string> _items = [];

    public override Control Native => _native;

    public Vb6ListBox() => _native.ItemsSource = _items;

    /// <summary>Puts an item in the list.</summary>
    public void AddItem(string item)
    {
        _items.Add(item);

        // Reassigned rather than relying on the list: a plain List raises
        // nothing when it changes, so the control would show the items it had
        // when it was bound and never any of the ones added afterwards.
        _native.ItemsSource = null;
        _native.ItemsSource = _items;
    }

    /// <summary>Empties it.</summary>
    public void Clear()
    {
        _items.Clear();
        _native.ItemsSource = null;
        _native.ItemsSource = _items;
    }

    public int ListCount => _items.Count;

    /// <summary>
    /// Which item is chosen, counted from zero.
    /// </summary>
    /// <remarks>
    /// -1 when none is, which is what Visual Basic 6 returned and what code
    /// tests for before reading the text.
    /// </remarks>
    public int ListIndex
    {
        get => _native.SelectedIndex;
        set => _native.SelectedIndex = value;
    }

    public string Text => _native.SelectedItem?.ToString() ?? "";

    public string List(int index) =>
        index >= 0 && index < _items.Count ? _items[index] : "";

    public event EventHandler? Click;

    public Vb6ListBox WithEvents()
    {
        _native.SelectionChanged += (_, _) => Click?.Invoke(this, EventArgs.Empty);

        return this;
    }
}

/// <summary>A box holding other controls.</summary>
public sealed class Vb6Frame : Vb6Control
{
    private readonly HeaderedContentControl _native = new();
    private readonly Canvas _inside = new();

    public override Control Native => _native;

    public Vb6Frame() => _native.Content = _inside;

    public string Caption
    {
        get => _native.Header?.ToString() ?? "";
        set => _native.Header = value;
    }

    /// <summary>Puts a control inside this frame.</summary>
    public void Add(Vb6Control control) => _inside.Children.Add(control.Native);
}
