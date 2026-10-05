using Avalonia;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsPropertyPanel
{
    private bool _propertyFocusRequested;

    internal void RequestSelectedPropertyFocus()
    {
        if (!_propertyFocusRequested)
        {
            _propertyFocusRequested = true;
            LayoutUpdated += OnPropertyFocusLayout;
        }
        TryFocusSelectedProperty();
    }

    private void OnPropertyFocusLayout(object? sender, EventArgs args) => TryFocusSelectedProperty();

    private void TryFocusSelectedProperty()
    {
        // Dock can recreate a closed pane over several layout passes. Keep
        // the request until its real row exists and can receive focus.
        if (_propertyFocusRequested && _grid.FocusSelectedProperty())
            CancelSelectedPropertyFocusRequest();
    }

    internal void CancelSelectedPropertyFocusRequest()
    {
        _propertyFocusRequested = false;
        LayoutUpdated -= OnPropertyFocusLayout;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        CancelSelectedPropertyFocusRequest();
        CancelViewStateRestore();
        _focusAfterEdit = false;
        _expectedEditedIdentity = null;
        base.OnDetachedFromVisualTree(args);
    }
}
