using System.Windows.Forms.Layout;
using System.Collections;
using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Measuring and drawing the strip, the keyboard and mouse that move the
/// selection through it, and the two collections TabControl exposes: its
/// children restricted to TabPage, and the convenience TabPages wrapper over
/// that same restricted collection.
/// </summary>
public partial class TabControl
{
    /// <summary>The height one row of tabs takes: a fixed ItemSize height, or the font's line plus the vertical padding.</summary>
    private int RowHeight()
    {
        if (_sizeMode == TabSizeMode.Fixed && _itemSize.Height > 0) return _itemSize.Height;

        return FontHeight + _padding.Y * 2;
    }

    /// <summary>The width a tab's own content asks for: its text plus the horizontal padding, never a fixed size.</summary>
    private int NaturalTabWidth(TabPage page)
    {
        var textSize = TextRenderer.MeasureText(page.Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        return textSize.Width + _padding.X * 2;
    }

    /// <summary>The width a tab is actually drawn at: ItemSize.Width in Fixed mode, its own content otherwise.</summary>
    private int TabWidth(TabPage page)
    {
        if (_sizeMode == TabSizeMode.Fixed && _itemSize.Width > 0) return _itemSize.Width;

        return NaturalTabWidth(page);
    }

    /// <summary>What ItemSize answers when nothing was ever assigned to it: the widest tab's own size.</summary>
    private Size NaturalItemSize()
    {
        var width = 0;
        foreach (var page in TabPagesSnapshot()) width = Math.Max(width, NaturalTabWidth(page));

        return new Size(width, RowHeight());
    }

    /// <summary>
    /// Every tab's rectangle, in client coordinates and at its unraised
    /// size, packed left to right and wrapped into further rows when
    /// Multiline is set and a row would otherwise overflow the client width.
    /// The whole strip starts <see cref="SelectedTabRaise"/> pixels down, so
    /// the selected tab has somewhere to bulge into above row zero.
    /// </summary>
    private Rectangle[] LayOutTabs(out int rowCount)
    {
        var pages = TabPagesSnapshot();
        var rects = new Rectangle[pages.Length];
        rowCount = 1;

        if (pages.Length == 0) return rects;

        var rowHeight = RowHeight();
        var availableWidth = Math.Max(ClientSize.Width, 1);
        var x = 0;
        var row = 0;

        for (var i = 0; i < pages.Length; i++)
        {
            var width = TabWidth(pages[i]);

            if (_multiline && x > 0 && x + width > availableWidth)
            {
                row++;
                x = 0;
            }

            rects[i] = new Rectangle(x, SelectedTabRaise + row * rowHeight, width, rowHeight);
            x += width;
        }

        rowCount = row + 1;

        return rects;
    }

    /// <summary>
    /// The page area before its own border is cut away: the client area
    /// minus the strip, on whichever side <see cref="Alignment"/> puts it.
    /// </summary>
    private Rectangle PageFrameRectangle()
    {
        var client = ClientRectangle;
        var rowCount = Math.Max(RowCount, 1);
        var stripExtent = rowCount * RowHeight() + SelectedTabRaise;

        return _alignment switch
        {
            TabAlignment.Bottom => new Rectangle(client.X, client.Y, client.Width, Math.Max(client.Height - stripExtent, 0)),
            TabAlignment.Left => new Rectangle(client.X + stripExtent, client.Y, Math.Max(client.Width - stripExtent, 0), client.Height),
            TabAlignment.Right => new Rectangle(client.X, client.Y, Math.Max(client.Width - stripExtent, 0), client.Height),
            _ => new Rectangle(client.X, client.Y + stripExtent, client.Width, Math.Max(client.Height - stripExtent, 0)),
        };
    }

    /// <summary>
    /// Paints the strip and the page's frame: native in WinForms (no
    /// OnPaint override there), so this runs from <see
    /// cref="Control.DefaultPaint"/> instead.
    /// </summary>
    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;

        using var backBrush = new SolidBrush(SystemColors.Control);
        graphics.FillRectangle(backBrush, ClientRectangle);

        var pageFrame = PageFrameRectangle();
        ControlPaint.DrawBorder3D(graphics, pageFrame, Border3DStyle.Raised);

        // Left, right and bottom strips are a stretch goal: only the default, top-aligned strip draws its tabs here.
        if (_alignment != TabAlignment.Top) return;

        var rects = LayOutTabs(out _);
        var pages = TabPagesSnapshot();

        for (var i = 0; i < pages.Length; i++) DrawTab(graphics, pages[i], rects[i], i == _selectedIndex, pageFrame);
    }

    /// <summary>
    /// One tab: its classic raised bevel, grown upward by <see
    /// cref="SelectedTabRaise"/> and joined to the page when it is the
    /// selected one, with its caption centred inside.
    /// </summary>
    private void DrawTab(Graphics graphics, TabPage page, Rectangle rect, bool selected, Rectangle pageFrame)
    {
        var tabRect = selected
            ? new Rectangle(rect.X, rect.Y - SelectedTabRaise, rect.Width, rect.Height + SelectedTabRaise)
            : rect;

        using var faceBrush = new SolidBrush(SystemColors.Control);
        graphics.FillRectangle(faceBrush, tabRect);
        ControlPaint.DrawBorder3D(graphics, tabRect, Border3DStyle.Raised);

        if (selected)
        {
            // Erases the page's top border directly under the selected tab, so the two look like one piece.
            var joinRect = new Rectangle(
                tabRect.Left + PageBorderThickness,
                pageFrame.Top - 1,
                Math.Max(tabRect.Width - PageBorderThickness * 2, 0),
                PageBorderThickness + 1);

            graphics.FillRectangle(faceBrush, joinRect);
        }

        var textColor = Enabled ? ForeColor : SystemColors.GrayText;
        TextRenderer.DrawText(graphics, page.Text, Font, tabRect, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    /// <summary>A press on the strip, before OnMouseDown: takes the focus, as a native control does, and selects the hit tab.</summary>
    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (CanSelect) Focus();
        if (e.Button != MouseButtons.Left) return;

        var rects = LayOutTabs(out _);
        for (var i = 0; i < rects.Length; i++)
        {
            if (!rects[i].Contains(e.Location)) continue;

            SelectedIndex = i;
            break;
        }
    }

    /// <summary>
    /// The keys TabControl manages itself, which must reach <see
    /// cref="OnKeyDown"/> instead of moving the focus or a dialog's default
    /// button: Ctrl+Tab and Ctrl+PageUp/Down cycle pages regardless of
    /// direction, Left/Right do too when the strip itself has the focus.
    /// </summary>
    protected override bool IsInputKey(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        var modifiers = keyData & (Keys.Control | Keys.Shift | Keys.Alt);

        if (modifiers == Keys.Control && code is Keys.Tab or Keys.PageUp or Keys.PageDown) return true;
        if (modifiers == (Keys.Control | Keys.Shift) && code == Keys.Tab) return true;
        if (modifiers == Keys.None && code is Keys.Left or Keys.Right) return true;

        return base.IsInputKey(keyData);
    }

    /// <summary>
    /// Real WinForms manages this navigation in code (OnKeyDown is
    /// overridden there too), rather than through the native strip.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs ke)
    {
        base.OnKeyDown(ke);
        if (ke.Handled) return;

        switch (ke.KeyCode)
        {
            case Keys.Tab when ke.Control:
                CycleSelection(forward: !ke.Shift);
                ke.Handled = true;
                break;

            case Keys.PageDown when ke.Control:
                CycleSelection(forward: true);
                ke.Handled = true;
                break;

            case Keys.PageUp when ke.Control:
                CycleSelection(forward: false);
                ke.Handled = true;
                break;

            case Keys.Right when !ke.Control && !ke.Alt:
                CycleSelection(forward: true);
                ke.Handled = true;
                break;

            case Keys.Left when !ke.Control && !ke.Alt:
                CycleSelection(forward: false);
                ke.Handled = true;
                break;
        }
    }

    /// <summary>Moves the selection one page forward or back, wrapping past either end.</summary>
    private void CycleSelection(bool forward)
    {
        var count = TabPages.Count;
        if (count == 0) return;

        var next = _selectedIndex + (forward ? 1 : -1);
        if (next < 0) next = count - 1;
        else if (next >= count) next = 0;

        SelectedIndex = next;
    }

    /// <summary>Padding and a fixed ItemSize are metrics, not child controls: PerformAutoScale would otherwise leave them behind.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void ScaleCore(float dx, float dy)
    {
        base.ScaleCore(dx, dy);

        _padding = new Point((int)Math.Round(_padding.X * dx), (int)Math.Round(_padding.Y * dy));
        if (!_itemSize.IsEmpty) _itemSize = new Size((int)Math.Round(_itemSize.Width * dx), (int)Math.Round(_itemSize.Height * dy));
    }

    // Declared as the base Control.ControlCollection, exactly as Microsoft's TabControl does: the override
    // returns a TabControl.ControlCollection at runtime without widening its declared return type.
    protected override Control.ControlCollection CreateControlsInstance() => new ControlCollection(this);

    /// <summary>The first page added becomes selected at once, without the cancelable Selecting/Deselecting dance: there was nothing to deselect.</summary>
    private void OnTabPageAdded(TabPage page, bool firstPage)
    {
        if (!firstPage) return;

        _selectedIndex = 0;
        UpdateTabSelection(updateFocus: false);
        page.RaiseEnter(EventArgs.Empty);
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>
    /// A page leaving shifts the selected index along with it, or - when
    /// the selected page itself was removed - silently selects the one that
    /// takes its place; removal is not something a handler gets to cancel.
    /// </summary>
    private void OnTabPageRemoved(int removedIndex)
    {
        if (TabPages.Count == 0)
        {
            _selectedIndex = -1;
            return;
        }

        if (removedIndex < _selectedIndex)
        {
            _selectedIndex--;
            return;
        }

        if (removedIndex != _selectedIndex) return;

        var next = Math.Min(removedIndex, TabPages.Count - 1);
        _selectedIndex = next;
        UpdateTabSelection(updateFocus: false);
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    /// <summary>Inserts a page; a page put before the selected one moves the selection index along, keeping the same page selected.</summary>
    private void InsertPage(int index, TabPage page)
    {
        var wasEmpty = TabPages.Count == 0;
        Controls.Add(page);
        Controls.SetChildIndex(page, index);

        var position = Controls.IndexOf(page);
        if (!wasEmpty && position <= _selectedIndex) _selectedIndex++;

        PerformLayout(this, "TabPages");
        Invalidate();
    }

    internal override LayoutEngine LayoutEngineInternal => PageLayout.Instance;

    /// <summary>Every page fills the page area, whatever its own Dock or Anchor: the tab control sizes its pages itself.</summary>
    private sealed class PageLayout : LayoutEngine
    {
        public static PageLayout Instance { get; } = new();

        public override void InitLayout(object child, BoundsSpecified specified)
        {
        }

        public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
        {
            if (container is not TabControl tabs) return false;

            var area = tabs.DisplayRectangle;
            foreach (Control page in tabs.Controls)
            {
                if (page.Bounds != area) ((Layout.IArrangedElement)page).SetBounds(area, BoundsSpecified.None);
            }

            return false;
        }
    }

    public override string ToString() => $"{base.ToString()}, TabPages.Count: {TabPages.Count}";

    /// <summary>TabControl's children, restricted to TabPage: nothing else may sit directly on the strip.</summary>
    public new class ControlCollection : Control.ControlCollection
    {
        private readonly TabControl _owner;

        public ControlCollection(TabControl owner)
            : base(owner)
        {
            _owner = owner;
        }

        public override void Add(Control? value)
        {
            if (value is null) return;

            if (value is not TabPage page)
            {
                throw new ArgumentException(
                    "TabControl's Controls property can only have TabPage controls added to it. The control being added is not derived from TabPage.",
                    nameof(value));
            }

            var firstPage = Count == 0;

            // Hidden until it is the selection: UpdateTabSelection shows only the active page, and the
            // control's own layout gives every page the page area, leaving the page's Dock as the user set it.
            page.Visible = false;
            base.Add(page);

            _owner.OnTabPageAdded(page, firstPage);
        }

        public override void Remove(Control? value)
        {
            if (value is not TabPage page)
            {
                base.Remove(value);
                return;
            }

            var index = IndexOf(page);
            base.Remove(page);

            if (index >= 0) _owner.OnTabPageRemoved(index);
        }
    }

    /// <summary>The convenience view of TabControl's children that the designer and most code use instead of Controls.</summary>
    public class TabPageCollection : ICollection, IEnumerable, IList
    {
        private readonly TabControl _owner;

        public TabPageCollection(TabControl owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            _owner = owner;
        }

        [Browsable(false)]
        public int Count => _owner.Controls.Count;

        public bool IsReadOnly => false;

        public virtual TabPage this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index), index, $"Index {index} is out of range.");

                return (TabPage)_owner.Controls[index];
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                RemoveAt(index);
                Insert(index, value);
            }
        }

        /// <summary>The first page with the name, compared without regard to case; null when none has it.</summary>
        public virtual TabPage? this[string? key]
        {
            get
            {
                var index = IndexOfKey(key);

                return index >= 0 ? this[index] : null;
            }
        }

        public void Add(TabPage value) => _owner.Controls.Add(value);

        public void Add(string? text) => Add(new TabPage(text));

        public void Add(string? key, string? text) => Add(new TabPage(text) { Name = key ?? string.Empty });

        public void Add(string? key, string? text, int imageIndex) => Add(new TabPage(text) { Name = key ?? string.Empty, ImageIndex = imageIndex });

        public void Add(string? key, string? text, string imageKey) => Add(new TabPage(text) { Name = key ?? string.Empty, ImageKey = imageKey });

        public void AddRange(params TabPage[] pages)
        {
            ArgumentNullException.ThrowIfNull(pages);

            _owner.SuspendLayout();

            try
            {
                foreach (var page in pages) Add(page);
            }
            finally
            {
                _owner.ResumeLayout(true);
            }
        }

        public virtual void Clear() => _owner.Controls.Clear();

        public bool Contains(TabPage page) => page is not null && _owner.Controls.Contains(page);

        public virtual bool ContainsKey(string? key) => _owner.Controls.ContainsKey(key);

        public IEnumerator GetEnumerator() => _owner.Controls.GetEnumerator();

        public int IndexOf(TabPage page) => page is null ? -1 : _owner.Controls.IndexOf(page);

        public virtual int IndexOfKey(string? key) => _owner.Controls.IndexOfKey(key);

        public void Insert(int index, TabPage tabPage)
        {
            ArgumentNullException.ThrowIfNull(tabPage);

            _owner.InsertPage(index, tabPage);
        }

        public void Insert(int index, string? text) => Insert(index, new TabPage(text));

        public void Insert(int index, string? key, string? text) => Insert(index, new TabPage(text) { Name = key ?? string.Empty });

        public void Insert(int index, string? key, string? text, int imageIndex) => Insert(index, new TabPage(text) { Name = key ?? string.Empty, ImageIndex = imageIndex });

        public void Insert(int index, string? key, string? text, string imageKey) => Insert(index, new TabPage(text) { Name = key ?? string.Empty, ImageKey = imageKey });

        public void Remove(TabPage value) => _owner.Controls.Remove(value);

        public void RemoveAt(int index) => Remove(this[index]);

        public virtual void RemoveByKey(string? key)
        {
            var index = IndexOfKey(key);
            if (index >= 0) RemoveAt(index);
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        void ICollection.CopyTo(Array array, int index)
        {
            foreach (var page in this) array.SetValue(page, index++);
        }

        bool IList.IsFixedSize => false;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (TabPage)value!;
        }

        int IList.Add(object? value)
        {
            var page = (TabPage)value!;
            Add(page);

            return IndexOf(page);
        }

        bool IList.Contains(object? value) => value is TabPage page && Contains(page);

        int IList.IndexOf(object? value) => value is TabPage page ? IndexOf(page) : -1;

        void IList.Insert(int index, object? value) => Insert(index, (TabPage)value!);

        void IList.Remove(object? value)
        {
            if (value is TabPage page) Remove(page);
        }
    }
}
