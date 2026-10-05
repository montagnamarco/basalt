using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

public sealed partial class CodeEditor
{
    private readonly ComboBox _navigationObjects = new() { Name = "VisualBasicObjects" };
    private readonly ComboBox _navigationMembers = new() { Name = "VisualBasicMembers" };
    private CancellationTokenSource? _navigationRequest;
    private bool _updatingNavigation;
    private bool _publishingCaret;
    private bool _navigationAttached;

    private Control CreateNavigationLayout()
    {
        _document.PropertyChanged += OnNavigationDocumentChanged;
        _editor.TextArea.Caret.PositionChanged += PublishNavigationCaret;
        AttachedToVisualTree += (_, _) =>
        {
            _navigationAttached = true;
            ApplyRequestedCaret();
            QueueNavigationRefresh(immediate: true);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _navigationAttached = false;
            _navigationRequest?.Cancel();
        };
        if (_document.Language != SourceLanguage.VisualBasic)
        {
            return _editor;
        }
        ToolTip.SetTip(_navigationObjects, Localizer.Get(StringKeys.EditorVbObjects));
        ToolTip.SetTip(_navigationMembers, Localizer.Get(StringKeys.EditorVbMembers));
        _navigationObjects.SelectionChanged += (_, _) => ShowNavigationMembers();
        _navigationMembers.SelectionChanged += async (_, _) => await NavigateSelectedMemberAsync();
        var selectors = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Children = { _navigationObjects, _navigationMembers },
        };
        Grid.SetColumn(_navigationMembers, 1);
        _navigationObjects.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _navigationMembers.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        var smartTagOverlay = new Canvas { ClipToBounds = true, Children = { _smartTagMargin } };
        var editorHost = new Grid { Children = { _editor, smartTagOverlay } };
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { selectors, editorHost },
        };
        Grid.SetRow(editorHost, 1);
        return layout;
    }

    private void OnNavigationDocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EditorDocumentViewModel.CaretOffset) && !_publishingCaret)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyRequestedCaret();
            }
            else
            {
                Dispatcher.UIThread.Post(ApplyRequestedCaret);
            }
        }
        else if (args.PropertyName == nameof(EditorDocumentViewModel.Text))
        {
            QueueNavigationRefresh();
        }
    }

    private void PublishNavigationCaret(object? sender, EventArgs args)
    {
        _publishingCaret = true;
        try
        {
            _document.CaretOffset = _editor.CaretOffset;
        }
        finally
        {
            _publishingCaret = false;
        }
    }

    private void ApplyRequestedCaret()
    {
        if (!_navigationAttached)
        {
            return;
        }
        _editor.CaretOffset = Math.Clamp(_document.CaretOffset, 0, _editor.Document.TextLength);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(_editor.CaretOffset).LineNumber);
        FocusText();
    }

    private void QueueNavigationRefresh(bool immediate = false)
    {
        if (!_navigationAttached || _document.Language != SourceLanguage.VisualBasic)
        {
            return;
        }
        _navigationRequest?.Cancel();
        _navigationRequest = new CancellationTokenSource();
        _ = RefreshNavigationAsync(immediate, _navigationRequest.Token);
    }

    private async Task RefreshNavigationAsync(bool immediate, CancellationToken cancellationToken)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(150, cancellationToken);
            }
            var source = _document.Text;
            var companionPath = _document.FilePath.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase)
                ? null : Path.ChangeExtension(_document.FilePath, ".Designer.vb");
            var companionDocument = companionPath is null ? null : _shell.OpenDocuments.FirstOrDefault(document =>
                string.Equals(document.FilePath, companionPath, StringComparison.OrdinalIgnoreCase));
            var companion = companionDocument?.Text;
            var objects = await Task.Run(async () =>
            {
                if (companion is null && companionPath is not null && File.Exists(companionPath))
                {
                    companion = await File.ReadAllTextAsync(companionPath, cancellationToken);
                }
                return VisualBasicEditorNavigation.Read(source, companion);
            }, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !_navigationAttached || _document.Text != source
                || (companionDocument is not null && companionDocument.Text != companion))
            {
                return;
            }
            var selected = _navigationObjects.SelectedItem as VisualBasicNavigationObject;
            _updatingNavigation = true;
            _navigationObjects.ItemsSource = objects;
            _navigationObjects.SelectedItem = objects.FirstOrDefault(item =>
                item.ClassName == selected?.ClassName && item.Name == selected?.Name)
                ?? objects.Where(item => item.Name == item.ClassName && item.Offset <= _editor.CaretOffset)
                    .LastOrDefault() ?? objects.FirstOrDefault();
            _updatingNavigation = false;
            ShowNavigationMembers();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _shell.WriteOutput(Localizer.Get(StringKeys.EditorVbNavigationFailed, exception.Message));
        }
    }

    private void ShowNavigationMembers()
    {
        if (_updatingNavigation)
        {
            return;
        }
        _updatingNavigation = true;
        try
        {
            _navigationMembers.ItemsSource = (_navigationObjects.SelectedItem as VisualBasicNavigationObject)?.Members ?? [];
            _navigationMembers.SelectedItem = null;
        }
        finally
        {
            _updatingNavigation = false;
        }
    }

    private async Task NavigateSelectedMemberAsync()
    {
        if (_updatingNavigation || _navigationMembers.SelectedItem is not VisualBasicNavigationMember member
            || _navigationObjects.SelectedItem is not VisualBasicNavigationObject target)
        {
            return;
        }
        try
        {
            if (member.EventName is null)
            {
                _document.RequestCaret(member.Offset);
                ApplyRequestedCaret();
                return;
            }
            if (_document.FilePath.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var original = _document.Text;
            var edited = await Task.Run(() => VisualBasicEditorNavigation.SelectEvent(original, target, member));
            if (!_navigationAttached || _document.Text != original)
            {
                return;
            }
            _document.Text = edited.Source;
            _document.RequestCaret(edited.Offset);
            ApplyRequestedCaret();
        }
        catch (Exception exception)
        {
            _shell.WriteOutput(Localizer.Get(StringKeys.EditorVbNavigationFailed, exception.Message));
        }
    }
}
