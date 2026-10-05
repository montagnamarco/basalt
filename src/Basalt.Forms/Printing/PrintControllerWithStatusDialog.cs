using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Resources;
using Avalonia.Threading;

namespace System.Windows.Forms;

/// <summary>Wraps a print controller and shows a cancellable progress window while an application loop runs.</summary>
public class PrintControllerWithStatusDialog : PrintController
{
    private readonly PrintController _underlyingController;
    private readonly string _dialogTitle;
    private static readonly ResourceManager Resources = new(
        "System.Windows.Forms.PrintControllerWithStatusDialog", typeof(PrintControllerWithStatusDialog).Assembly);
    private readonly object _stateGate = new();
    private readonly Dictionary<int, PrintStatusForm> _statusForms = [];
    private int _pageNumber;
    private int _jobVersion;
    private bool _running;
    private volatile bool _canceled;

    public PrintControllerWithStatusDialog(PrintController underlyingController)
        : this(underlyingController, GetText("Title"))
    {
    }

    public PrintControllerWithStatusDialog(PrintController underlyingController, string dialogTitle)
    {
        _underlyingController = underlyingController ?? throw new ArgumentNullException(nameof(underlyingController));
        _dialogTitle = dialogTitle ?? throw new ArgumentNullException(nameof(dialogTitle));
    }

    public override bool IsPreview => _underlyingController.IsPreview;

    public override void OnStartPrint(PrintDocument document, PrintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(e);
        int version;
        lock (_stateGate)
        {
            if (_running) throw new InvalidOperationException("A print job is already active.");
            _running = true;
            _canceled = false;
            _pageNumber = 1;
            version = ++_jobVersion;
        }
        try
        {
            ShowStatus(version);
            _underlyingController.OnStartPrint(document, e);
            if (_canceled) e.Cancel = true;
        }
        catch
        {
            FinishJob(version);
            throw;
        }
    }

    public override Graphics OnStartPage(PrintDocument document, PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(e);
        UpdateStatus(CurrentVersion, _pageNumber);
        var graphics = _underlyingController.OnStartPage(document, e);
        if (_canceled) e.Cancel = true;
        return graphics;
    }

    public override void OnEndPage(PrintDocument document, PrintPageEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(e);
        _underlyingController.OnEndPage(document, e);
        if (_canceled) e.Cancel = true;
        _pageNumber++;
    }

    public override void OnEndPrint(PrintDocument document, PrintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(e);
        try
        {
            if (_canceled) e.Cancel = true;
            _underlyingController.OnEndPrint(document, e);
        }
        finally
        {
            FinishJob(CurrentVersion);
        }
    }

    internal void Cancel()
    {
        lock (_stateGate)
        {
            if (_running) _canceled = true;
        }
    }

    private void Cancel(int version)
    {
        lock (_stateGate)
        {
            if (_running && _jobVersion == version) _canceled = true;
        }
    }

    private bool IsActive(int version)
    {
        lock (_stateGate) return _running && _jobVersion == version;
    }

    private int CurrentVersion
    {
        get { lock (_stateGate) return _jobVersion; }
    }

    private void FinishJob(int version)
    {
        lock (_stateGate)
        {
            if (_jobVersion == version) _running = false;
        }
        CloseStatus(version);
    }

    private void ShowStatus(int version)
    {
        if (Avalonia.Application.Current is null) return;

        OnUiThread(() =>
        {
            if (!IsActive(version)) return;
            var form = new PrintStatusForm(_dialogTitle);
            form.CancelRequested += (_, _) => Cancel(version);
            _statusForms[version] = form;
            form.Show(Form.ActiveForm);
        });
    }

    private void UpdateStatus(int version, int pageNumber)
    {
        OnUiThread(() =>
        {
            if (IsActive(version) && _statusForms.TryGetValue(version, out var form) && !form.IsDisposed)
                form.SetPage(pageNumber);
        });
    }

    private void CloseStatus(int version)
    {
        OnUiThread(() =>
        {
            if (!_statusForms.Remove(version, out var form)) return;
            if (!form.IsDisposed) form.Complete();
        });
    }

    private static string GetText(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private sealed class PrintStatusForm : Form
    {
        private readonly Label _pageLabel;
        private bool _completing;

        internal PrintStatusForm(string title)
        {
            Text = title;
            ClientSize = new Size(260, 90);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;

            _pageLabel = new Label { Location = new Point(12, 12), Size = new Size(235, 26) };
            var cancel = new Button { Text = GetText("Cancel"), Location = new Point(173, 51), Size = new Size(75, 26) };
            cancel.Click += (_, _) => Close();
            CancelButton = cancel;
            Controls.Add(_pageLabel);
            Controls.Add(cancel);
            SetPage(1);
        }

        internal event EventHandler? CancelRequested;

        internal void SetPage(int pageNumber) => _pageLabel.Text =
            string.Format(CultureInfo.CurrentUICulture, GetText("Page"), pageNumber);

        internal void Complete()
        {
            _completing = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_completing) CancelRequested?.Invoke(this, EventArgs.Empty);
            base.OnFormClosing(e);
        }
    }
}
