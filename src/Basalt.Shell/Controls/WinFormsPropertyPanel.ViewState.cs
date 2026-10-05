using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

public sealed partial class WinFormsPropertyPanel
{
    private sealed record GridViewState(Vector Offset, bool HadFocus);

    private int _viewStateGeneration;
    private GridViewState? _pendingViewState;
    private bool _focusAfterEdit;
    private string? _expectedEditedIdentity;
    private ScrollViewer? _focusScrollSuppressed;
    private WindowBase? _focusScrollOwner;
    private bool _previousBringIntoViewOnFocusChange;
    private int _focusScrollGeneration;

    private void MarkEditFocus(string propertyName, string? value)
    {
        _focusAfterEdit = true;
        _expectedEditedIdentity = propertyName == "Name" ? value : null;
    }

    internal Vector? CapturePropertyEditorOffset() => GridScrollViewer()?.Offset;

    internal void RestoreSelectedPropertyFocus(Vector? originalOffset = null)
    {
        var scroll = GridScrollViewer();
        if (scroll is null)
        {
            RequestSelectedPropertyFocus();
            return;
        }

        CancelViewStateRestore();
        _pendingViewState = new GridViewState(originalOffset ?? scroll.Offset, true);
        var generation = _viewStateGeneration;
        LayoutUpdated += OnViewStateLayoutUpdated;
        Dispatcher.UIThread.Post(() => TryRestoreViewState(generation), DispatcherPriority.Loaded);
    }

    private void SetGridProperties(IReadOnlyList<DesignableProperty> properties, bool preserveView)
    {
        var scroll = GridScrollViewer();
        var offset = preserveView ? scroll?.Offset ?? default : default;
        // A restore still waiting for the edit to re-enable the panel means the
        // row held focus before a modal editor: the edit's own refresh must keep it.
        var hadFocus = preserveView && (_focusAfterEdit || _pendingViewState is { HadFocus: true } ||
            _grid.GetVisualDescendants().OfType<InputElement>().Any(element => element.IsFocused));
        _focusAfterEdit = false;
        _expectedEditedIdentity = null;

        CancelViewStateRestore();
        _grid.Properties = properties;

        if (scroll is null)
        {
            return;
        }

        _pendingViewState = new GridViewState(offset, hadFocus);
        var generation = _viewStateGeneration;
        LayoutUpdated += OnViewStateLayoutUpdated;
        Dispatcher.UIThread.Post(() => TryRestoreViewState(generation), DispatcherPriority.Loaded);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty && IsEffectivelyEnabled && _pendingViewState is not null)
        {
            var generation = _viewStateGeneration;
            Dispatcher.UIThread.Post(() => TryRestoreViewState(generation), DispatcherPriority.Loaded);
        }
    }

    private ScrollViewer? GridScrollViewer() =>
        _grid.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnViewStateLayoutUpdated(object? sender, EventArgs args) =>
        TryRestoreViewState(_viewStateGeneration);

    private void TryRestoreViewState(int generation)
    {
        if (generation != _viewStateGeneration || _pendingViewState is not { } state)
        {
            return;
        }

        var scroll = GridScrollViewer();
        if (scroll is null || scroll.Bounds.Height <= 0)
        {
            return;
        }

        // Focus() fails on a disabled control. The designer disables the panel while
        // an edit runs, so the restore waits until it is enabled again.
        if (state.HadFocus && !IsEffectivelyEnabled)
        {
            return;
        }

        CancelViewStateRestore();
        if (state.HadFocus)
        {
            SuppressFocusScrollThroughWindowActivation(scroll);
            _grid.FocusSelectedProperty();
        }

        // Keep the exact pre-editor position even if focus changed during layout.
        scroll.Offset = state.Offset;
    }

    private void SuppressFocusScrollThroughWindowActivation(ScrollViewer scroll)
    {
        RestoreAutomaticFocusScrolling();
        if (TopLevel.GetTopLevel(this) is not WindowBase owner)
        {
            return;
        }

        _focusScrollSuppressed = scroll;
        _focusScrollOwner = owner;
        _previousBringIntoViewOnFocusChange = scroll.BringIntoViewOnFocusChange;
        scroll.BringIntoViewOnFocusChange = false;
        owner.Activated += OnFocusScrollOwnerActivated;
        DetachedFromVisualTree += OnFocusScrollPanelDetached;

        // An already-active owner will not raise Activated again. The queued
        // focus/layout work completes before the background restoration.
        if (owner.IsActive)
        {
            PostAutomaticFocusScrollingRestore();
        }
    }

    private void OnFocusScrollOwnerActivated(object? sender, EventArgs args) =>
        PostAutomaticFocusScrollingRestore();

    private void OnFocusScrollPanelDetached(object? sender, VisualTreeAttachmentEventArgs args) =>
        RestoreAutomaticFocusScrolling();

    private void PostAutomaticFocusScrollingRestore()
    {
        var generation = _focusScrollGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == _focusScrollGeneration)
            {
                RestoreAutomaticFocusScrolling();
            }
        }, DispatcherPriority.Background);
    }

    private void RestoreAutomaticFocusScrolling()
    {
        ++_focusScrollGeneration;
        if (_focusScrollSuppressed is { } scroll)
        {
            scroll.BringIntoViewOnFocusChange = _previousBringIntoViewOnFocusChange;
            _focusScrollSuppressed = null;
        }

        if (_focusScrollOwner is { } owner)
        {
            owner.Activated -= OnFocusScrollOwnerActivated;
            _focusScrollOwner = null;
        }

        DetachedFromVisualTree -= OnFocusScrollPanelDetached;
    }

    private void CancelViewStateRestore()
    {
        ++_viewStateGeneration;
        _pendingViewState = null;
        LayoutUpdated -= OnViewStateLayoutUpdated;
    }
}
