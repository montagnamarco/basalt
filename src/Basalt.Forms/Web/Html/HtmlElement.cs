using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// WinForms wraps one node of Internet Explorer's DOM in an HtmlElement.
/// Basalt has no HTML engine and so no DOM: nothing in this assembly ever
/// calls the constructor below, matching WinForms itself, where HtmlElement
/// has no public constructor either - the only way to get one is through a
/// WebBrowser's Document, which here always returns null. Every member
/// still exists, for source compatibility, and refuses the moment code
/// reaches past the null checks that would normally stop it first.
/// </summary>
public sealed class HtmlElement
{
    private const string NoDom = "There is no HTML DOM behind this control: HtmlElement has nothing to wrap.";

    internal HtmlElement()
    {
    }

    public HtmlElementCollection All => throw new PlatformNotSupportedException(NoDom);

    public bool CanHaveChildren => throw new PlatformNotSupportedException(NoDom);

    public HtmlElementCollection Children => throw new PlatformNotSupportedException(NoDom);

    public Rectangle ClientRectangle => throw new PlatformNotSupportedException(NoDom);

    public HtmlDocument Document => throw new PlatformNotSupportedException(NoDom);

    public object DomElement => throw new PlatformNotSupportedException(NoDom);

    public bool Enabled
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement FirstChild => throw new PlatformNotSupportedException(NoDom);

    public string Id
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string InnerHtml
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string InnerText
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string Name
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement NextSibling => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement OffsetParent => throw new PlatformNotSupportedException(NoDom);

    public Rectangle OffsetRectangle => throw new PlatformNotSupportedException(NoDom);

    public string OuterHtml
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string OuterText
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public HtmlElement Parent => throw new PlatformNotSupportedException(NoDom);

    public int ScrollLeft
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public Rectangle ScrollRectangle => throw new PlatformNotSupportedException(NoDom);

    public int ScrollTop
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string Style
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public short TabIndex
    {
        get => throw new PlatformNotSupportedException(NoDom);
        set => throw new PlatformNotSupportedException(NoDom);
    }

    public string TagName => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement AppendChild(HtmlElement newElement) => throw new PlatformNotSupportedException(NoDom);

    public void AttachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public void DetachEventHandler(string eventName, EventHandler eventHandler) => throw new PlatformNotSupportedException(NoDom);

    public override bool Equals(object? obj) => throw new PlatformNotSupportedException(NoDom);

    public void Focus() => throw new PlatformNotSupportedException(NoDom);

    public string? GetAttribute(string attributeName) => throw new PlatformNotSupportedException(NoDom);

    public HtmlElementCollection GetElementsByTagName(string tagName) => throw new PlatformNotSupportedException(NoDom);

    public override int GetHashCode() => throw new PlatformNotSupportedException(NoDom);

    public HtmlElement InsertAdjacentElement(HtmlElementInsertionOrientation orientation, HtmlElement insertedElement) =>
        throw new PlatformNotSupportedException(NoDom);

    public object? InvokeMember(string methodName) => throw new PlatformNotSupportedException(NoDom);

    public object? InvokeMember(string methodName, params object[] parameter) => throw new PlatformNotSupportedException(NoDom);

    public void RaiseEvent(string eventName) => throw new PlatformNotSupportedException(NoDom);

    public void RemoveFocus() => throw new PlatformNotSupportedException(NoDom);

    public void ScrollIntoView(bool alignWithTop) => throw new PlatformNotSupportedException(NoDom);

    public void SetAttribute(string attributeName, string value) => throw new PlatformNotSupportedException(NoDom);

    public static bool operator ==(HtmlElement? left, HtmlElement? right) => throw new PlatformNotSupportedException(NoDom);

    public static bool operator !=(HtmlElement? left, HtmlElement? right) => throw new PlatformNotSupportedException(NoDom);

    public event HtmlElementEventHandler? Click
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DoubleClick
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Drag
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DragEnd
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DragLeave
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? DragOver
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? Focusing
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? GotFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? KeyDown
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? KeyPress
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? KeyUp
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? LosingFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? LostFocus
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseDown
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseEnter
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseLeave
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseMove
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseOver
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }

    public event HtmlElementEventHandler? MouseUp
    {
        add => throw new PlatformNotSupportedException(NoDom);
        remove => throw new PlatformNotSupportedException(NoDom);
    }
}
