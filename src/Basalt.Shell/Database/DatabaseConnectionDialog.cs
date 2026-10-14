using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Basalt.Core.Localization;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;

namespace Basalt.Shell.Database;

/// <summary>
/// Adds or edits a database connection.
/// </summary>
/// <remarks>
/// The fields follow the engine: a SQLite connection is a file, a server has a
/// host, a port, a database and a login, and SQL Server alone offers integrated
/// security and certificate trust. The advanced box, when filled in, is the
/// connection string used as it is, for what the fields do not reach.
///
/// Test Connection opens the connection through the same provider a real open
/// uses, shows the engine's answer, and closes it again. Nothing is saved until
/// OK. An empty password on an edit keeps the one already stored.
/// </remarks>
public sealed class DatabaseConnectionDialog : Window
{
    private readonly ComboBox _kind;
    private readonly TextBox _name;
    private readonly TextBox _file;
    private readonly TextBox _server;
    private readonly TextBox _port;
    private readonly TextBox _database;
    private readonly CheckBox _integrated;
    private readonly TextBox _user;
    private readonly TextBox _password;
    private readonly CheckBox _encrypt;
    private readonly CheckBox _trust;
    private readonly ComboBox _ssl;
    private readonly TextBox _advanced;
    private readonly TextBlock _result;
    private readonly Button _test;
    private readonly CheckBox _clear;
    private readonly DatabaseWorkspace? _workspace;
    private readonly string? _connectionId;

    private readonly StackPanel _fileRow;
    private readonly StackPanel _serverRows;
    private readonly StackPanel _loginRows;
    private readonly StackPanel _sqlServerRows;
    private readonly StackPanel _sslRow;
    private readonly TextBlock _passwordNote;

    /// <summary>The engine the SSL choices were last set for, so they are not reset on every change.</summary>
    private DatabaseKind _sslFor;

    private DatabaseConnectionDialog(DatabaseDraft initial, string title, DatabaseWorkspace? workspace, string? connectionId)
    {
        _workspace = workspace;
        _connectionId = connectionId;
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _name = new TextBox { Text = initial.Name, Name = "DatabaseName" };

        var choices = DatabaseKinds.All.Select(kind => new KindChoice(kind)).ToList();
        _kind = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = choices.First(c => c.Kind == initial.Kind),
            Name = "DatabaseKind",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _file = new TextBox { Text = initial.FilePath, Name = "DatabaseFile" };
        var browse = new Button
        {
            Content = Localizer.Get(StringKeys.DbFieldBrowse),
            Name = "DatabaseBrowse",

            // Stretched to the row, so it is as tall as the box beside it; the gap is the same as the rest of the dialog's.
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(Spacing.Tight, 0, 0, 0),
        };
        browse.Click += async (_, _) => await BrowseAsync();

        // The box takes the row's width and Browse sits to its right. A grid, not a dock: a dock puts the
        // first child at the left edge, which is where Browse was.
        var fileLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        fileLine.Children.Add(_file);
        fileLine.Children.Add(browse);
        Grid.SetColumn(browse, 1);

        _fileRow = new StackPanel
        {
            Spacing = Spacing.Tight,
            Children =
            {
                Label(StringKeys.DbFieldFile),
                fileLine,
            },
        };

        _server = new TextBox { Text = initial.Server, Name = "DatabaseServer" };
        _port = new TextBox { Text = initial.Port, Name = "DatabasePort" };
        _database = new TextBox { Text = initial.Database, Name = "DatabaseDatabase" };
        _serverRows = new StackPanel
        {
            Spacing = Spacing.Tight,
            Children =
            {
                Label(StringKeys.DbFieldServer), _server,
                Label(StringKeys.DbFieldPort), _port,
                Label(StringKeys.DbFieldDatabase), _database,
            },
        };

        _integrated = new CheckBox
        {
            Content = Localizer.Get(StringKeys.DbFieldIntegrated),
            IsChecked = initial.Integrated,
            Name = "DatabaseIntegrated",
        };
        _integrated.IsCheckedChanged += (_, _) => UpdateVisibility();

        _user = new TextBox { Text = initial.User, Name = "DatabaseUser" };
        _password = new TextBox { Text = "", PasswordChar = '•', Name = "DatabasePassword" };
        _passwordNote = new TextBlock
        {
            Text = Localizer.Get(StringKeys.DbPasswordNote),
            FontSize = PanelMetrics.SecondaryFontSize,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        // Offered on an edit only: a connection that never had a password has nothing to clear.
        _clear = new CheckBox
        {
            Content = Localizer.Get(StringKeys.DbClearPassword),
            Name = "DatabaseClearPassword",
            IsVisible = connectionId is not null,
        };

        _loginRows = new StackPanel
        {
            Spacing = Spacing.Tight,
            Children =
            {
                _integrated,
                Label(StringKeys.DbFieldUser), _user,
                Label(StringKeys.DbFieldPassword), _password, _passwordNote, _clear,
            },
        };

        _encrypt = new CheckBox { Content = Localizer.Get(StringKeys.DbFieldEncrypt), IsChecked = initial.Encrypt, Name = "DatabaseEncrypt" };
        _trust = new CheckBox
        {
            Content = Localizer.Get(StringKeys.DbFieldTrustCertificate),
            IsChecked = initial.TrustServerCertificate,
            Name = "DatabaseTrust",
        };
        _sqlServerRows = new StackPanel { Spacing = Spacing.Tight, Children = { _encrypt, _trust } };

        // The choices are the engine's own keywords, so they are set for the engine the draft names.
        _sslFor = initial.Kind;
        var sslChoices = SslChoices(initial.Kind);
        _ssl = new ComboBox
        {
            ItemsSource = sslChoices,
            SelectedItem = sslChoices.Contains(initial.SslMode) ? initial.SslMode : sslChoices[1],
            Name = "DatabaseSsl",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _sslRow = new StackPanel { Spacing = Spacing.Tight, Children = { Label(StringKeys.DbFieldSslMode), _ssl } };

        _advanced = new TextBox { Text = initial.Advanced, Name = "DatabaseAdvanced" };
        var advancedHint = new TextBlock
        {
            Text = Localizer.Get(StringKeys.DbAdvancedHint),
            FontSize = PanelMetrics.SecondaryFontSize,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        _result = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = PanelMetrics.FontSize };

        _test = new Button { Content = Localizer.Get(StringKeys.DbTestConnection), Name = "DatabaseTest" };
        _test.Click += async (_, _) => await TestAsync();

        var ok = new Button { Content = Localizer.Get(StringKeys.DbOk), Classes = { "primary" }, IsDefault = true, Name = "DatabaseOk" };
        ok.Click += (_, _) => Accept();

        var cancel = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), IsCancel = true, Name = "DatabaseCancel" };
        cancel.Click += (_, _) => Close(null);

        _kind.SelectionChanged += (_, _) => UpdateVisibility();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = Spacing.Normal,
            Children = { _test, ok, cancel },
        };

        Content = new StackPanel
        {
            Spacing = Spacing.Normal,
            Margin = Spacing.DialogPadding,
            Children =
            {
                new StackPanel { Spacing = Spacing.Tight, Children = { Label(StringKeys.DbFieldName), _name } },
                Label(StringKeys.DbFieldProvider), _kind,
                _fileRow,
                _serverRows,
                _loginRows,
                _sqlServerRows,
                _sslRow,
                new Expander
                {
                    Header = Localizer.Get(StringKeys.DbFieldAdvanced),
                    Name = "DatabaseAdvancedExpander",
                    IsExpanded = initial.Advanced.Length > 0,
                    Content = new StackPanel { Spacing = Spacing.Tight, Children = { _advanced, advancedHint } },
                },
                _result,
                buttons,
            },
        };

        UpdateVisibility();
    }

    /// <summary>
    /// Asks for a connection. Returns the draft to save, or null when cancelled.
    /// </summary>
    public static Task<DatabaseDraft?> AskAsync(
        Window owner, string title, DatabaseDraft initial, DatabaseWorkspace workspace, string? connectionId)
    {
        var dialog = new DatabaseConnectionDialog(initial, title, workspace, connectionId);

        return dialog.ShowDialog<DatabaseDraft?>(owner);
    }

    /// <summary>The engine the fields currently describe.</summary>
    private DatabaseKind Kind => ((KindChoice)_kind.SelectedItem!).Kind;

    /// <summary>The draft as the fields now read.</summary>
    private DatabaseDraft Current() => new(
        _name.Text ?? "",
        Kind,
        _file.Text ?? "",
        _server.Text ?? "",
        _port.Text ?? "",
        _database.Text ?? "",
        _user.Text ?? "",
        _password.Text ?? "",
        _integrated.IsChecked == true,
        _encrypt.IsChecked == true,
        _trust.IsChecked == true,
        _ssl.SelectedItem as string ?? "",
        _advanced.Text ?? "",
        _clear.IsChecked == true);

    /// <summary>Shows the fields the chosen engine uses, and only those.</summary>
    private void UpdateVisibility()
    {
        var kind = Kind;
        var server = kind is DatabaseKind.SqlServer or DatabaseKind.PostgreSql or DatabaseKind.MySql;
        var windowsLogin = kind == DatabaseKind.SqlServer && _integrated.IsChecked == true;

        _fileRow.IsVisible = kind == DatabaseKind.Sqlite;
        _serverRows.IsVisible = server;
        _loginRows.IsVisible = server;
        _user.IsEnabled = !windowsLogin;
        _password.IsEnabled = !windowsLogin;
        _passwordNote.IsVisible = server && !windowsLogin;
        _integrated.IsVisible = kind == DatabaseKind.SqlServer;
        _sqlServerRows.IsVisible = kind == DatabaseKind.SqlServer;

        var ssl = kind is DatabaseKind.PostgreSql or DatabaseKind.MySql;
        _sslRow.IsVisible = ssl;

        if (ssl && _sslFor != kind)
        {
            _sslFor = kind;

            var choices = SslChoices(kind);
            _ssl.ItemsSource = choices;
            _ssl.SelectedItem = choices[1];
        }
    }

    /// <summary>Checks the fields and, when they are whole, closes the dialog with them.</summary>
    private void Accept()
    {
        var draft = Current();

        if (draft.Name.Trim().Length == 0)
        {
            _result.Text = Localizer.Get(StringKeys.DbNameRequired);
            return;
        }

        // The advanced string stands for every field, so the fields are not asked for then.
        if (draft.Advanced.Trim().Length == 0 && Missing(draft) is { } problem)
        {
            _result.Text = problem;
            return;
        }

        // The string is checked here, where the user can fix it, rather than when the connection is saved.
        try
        {
            ConnectionStrings.SplitPassword(ConnectionStrings.Build(draft));
        }
        catch (ArgumentException ex)
        {
            _result.Text = Localizer.Get(StringKeys.DbBadConnectionString, SecretScrub.Clean(ex.Message, draft.Password, draft.Advanced));
            return;
        }

        Close(draft);
    }

    private static string? Missing(DatabaseDraft draft)
    {
        if (draft.Kind == DatabaseKind.Sqlite)
            return draft.FilePath.Trim().Length == 0 ? Localizer.Get(StringKeys.DbFileRequired) : null;

        if (draft.Server.Trim().Length == 0) return Localizer.Get(StringKeys.DbServerRequired);

        return null;
    }

    /// <summary>Opens and closes the connection the fields describe, and says what the engine answered.</summary>
    private async Task TestAsync()
    {
        var draft = await WithStoredPasswordAsync(Current()).ConfigureAwait(true);

        _test.IsEnabled = false;
        _result.Text = Localizer.Get(StringKeys.DbTesting);

        try
        {
            var described = await DatabaseWorkspace.TestAsync(draft).ConfigureAwait(true);

            _result.Text = Localizer.Get(StringKeys.DbTestOk, described);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _result.Text = Localizer.Get(StringKeys.DbTestFailed, ex.Message);
        }
        finally
        {
            _test.IsEnabled = true;
        }
    }

    /// <summary>
    /// On an edit, an empty password field means the stored one, as saving does: the test uses it too.
    /// The keychain is read on the pool, since it may run a system tool.
    /// </summary>
    private async Task<DatabaseDraft> WithStoredPasswordAsync(DatabaseDraft draft)
    {
        if (draft.Password.Length > 0 || draft.ClearPassword || _workspace is null || _connectionId is null) return draft;

        var stored = await Task.Run(() => _workspace.Passwords.Get(_connectionId)).ConfigureAwait(true);

        return draft with { Password = stored ?? "" };
    }

    /// <summary>Opens the operating system's file dialog for a SQLite file.</summary>
    private async Task BrowseAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Get(StringKeys.DbFieldFile),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localizer.Get(StringKeys.OpenFilterSqlite))
                {
                    Patterns = ["*.db", "*.sqlite", "*.sqlite3", "*.db3"]
                },
                new FilePickerFileType(Localizer.Get(StringKeys.OpenFilterAllFiles)) { Patterns = ["*"] }
            ]
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) _file.Text = path;
    }

    /// <summary>The SSL modes the engine's own connection string keywords take.</summary>
    private static IReadOnlyList<string> SslChoices(DatabaseKind kind) =>
        kind == DatabaseKind.MySql
            ? ["None", "Preferred", "Required"]
            : ["Disable", "Prefer", "Require"];

    private static TextBlock Label(string key) => new()
    {
        Text = Localizer.Get(key),
        FontSize = PanelMetrics.FontSize,
    };

    /// <summary>An engine in the provider list, shown by its name.</summary>
    private sealed record KindChoice(DatabaseKind Kind)
    {
        public override string ToString() => DatabaseKinds.Label(Kind);
    }
}
