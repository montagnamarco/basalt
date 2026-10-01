namespace System.Windows.Forms;

/// <summary>What a message loop runs for: by default, until its main form closes.</summary>
public class ApplicationContext : IDisposable
{
    private Form? _mainForm;

    public ApplicationContext()
        : this(null)
    {
    }

    public ApplicationContext(Form? mainForm)
    {
        MainForm = mainForm;
    }

    ~ApplicationContext() => Dispose(false);

    /// <summary>The form whose closing ends the loop.</summary>
    public Form? MainForm
    {
        get => _mainForm;
        set
        {
            if (_mainForm is not null) _mainForm.HandleDestroyed -= OnMainFormDestroy;

            _mainForm = value;

            if (_mainForm is not null) _mainForm.HandleDestroyed += OnMainFormDestroy;
        }
    }

    public object? Tag { get; set; }

    public event EventHandler? ThreadExit;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        if (_mainForm is not null)
        {
            if (!_mainForm.IsDisposed) _mainForm.Dispose();

            _mainForm = null;
        }
    }

    /// <summary>Ends the loop this context runs.</summary>
    public void ExitThread() => ExitThreadCore();

    protected virtual void ExitThreadCore() => ThreadExit?.Invoke(this, EventArgs.Empty);

    /// <summary>A main form that closes ends the loop, unless it is only recreating its window.</summary>
    protected virtual void OnMainFormClosed(object? sender, EventArgs e) => ExitThreadCore();

    private void OnMainFormDestroy(object? sender, EventArgs e)
    {
        if (sender is Form { RecreatingHandle: true }) return;

        if (sender is Form form) form.HandleDestroyed -= OnMainFormDestroy;

        OnMainFormClosed(sender, e);
    }
}
