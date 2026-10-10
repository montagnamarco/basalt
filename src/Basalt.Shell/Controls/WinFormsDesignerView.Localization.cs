using System.Globalization;
using Basalt.Core.Localization;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Shell.Controls;

internal sealed partial class WinFormsDesignerView
{
    /// <summary>The culture .resx buffers this view has shown or written, by path, for hosts without a find hook.</summary>
    private readonly Dictionary<string, EditorDocumentViewModel> _cultureBuffers = new(StringComparer.Ordinal);

    /// <summary>The session behind the surface, for the Language and Localizable rows and for tests.</summary>
    internal WinFormsDesignerSession? DesignerSession => _session;

    /// <summary>
    /// The Localizable and Language rows of the form the session shows, for the property panel. Each edit
    /// runs through the same guarded path as a property edit, so a failure is shown instead of thrown.
    /// </summary>
    internal WinFormsFormDesignRows? FormDesignRows() => _session is null ? null : new WinFormsFormDesignRows(
        _session.IsLocalizable,
        _session.Culture,
        LanguageChoices(_session.Culture),
        value => RunEditAsync(() => DesignerSession!.SetLocalizableAsync(value)),
        culture => RunEditAsync(() => SetLanguageAsync(culture)));

    /// <summary>
    /// The cultures Language offers: every neutral culture the runtime knows, sorted by display name. The
    /// current culture is added when it is not in that list, so the grid can always show what is shown.
    /// </summary>
    internal static IReadOnlyList<string> LanguageChoices(string? current)
    {
        var names = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(culture => culture.Name.Length > 0)
            .Select(culture => culture.Name)
            .ToList();
        if (current is not null && !names.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(current);
        }

        return names.OrderBy(DisplayNameOf, StringComparer.CurrentCulture).ToArray();
    }

    private static string DisplayNameOf(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            // A culture the runtime does not know is listed under its own name rather than dropped.
            return name;
        }
    }

    /// <summary>
    /// Shows the form in <paramref name="culture"/>, or in the neutral values for null ("(Default)").
    /// The culture's .resx sibling (Form1.it.resx) is read through the editor's buffers; a file that does
    /// not exist yet is created by the first edit, the way the first image creates the neutral .resx.
    /// </summary>
    internal async Task SetLanguageAsync(string? culture)
    {
        if (_disposed || _session is not { } session) return;
        var cultureResources = culture is null ? null : await LoadCultureResourcesAsync(session, culture);
        BindCultureResourceEditing(session);
        session.SetCulture(culture, cultureResources);
        ShowProblems();
    }

    /// <summary>
    /// The culture's .resx as the session reads it, or null when the file does not exist. Used by a language switch
    /// and by an undo or redo that must show the language its edit was made in.
    /// </summary>
    private async Task<WinFormsResourceDocument?> LoadCultureResourcesAsync(WinFormsDesignerSession session, string culture)
    {
        var buffer = await FindCultureBufferAsync(CulturePath(culture));
        if (_disposed || !ReferenceEquals(session, _session))
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        if (buffer is null) return null;
        _cultureBuffers[CulturePath(culture)] = buffer;
        var parsed = await ParseResourcesAsync(buffer.Text);
        return parsed.Document ?? throw new FormatException(parsed.Problem);
    }

    private string CulturePath(string culture) => Path.ChangeExtension(CompanionPath, "." + culture + ".resx");

    /// <summary>
    /// The cultures of the Form1.&lt;culture&gt;.resx files beside the neutral .resx, listed from disk. Each one is then read
    /// through <see cref="LoadCultureResourcesAsync"/>, which takes the host's open buffer when there is one, so a delete
    /// or rename changes the text the user sees, not a stale copy.
    /// </summary>
    private IReadOnlyList<string> CultureResourceNames()
    {
        var neutralPath = Path.ChangeExtension(CompanionPath, ".resx");
        var directory = Path.GetDirectoryName(neutralPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return [];
        const string extension = ".resx";
        var stem = Path.GetFileNameWithoutExtension(neutralPath);
        // The pattern guarantees the stem, its dot and the extension, so the middle is the culture name. Only a name the
        // runtime knows is a culture file: Form1.backup.resx is a file of the user's, not a language, and an edit must not
        // remove or rename its entries.
        return Directory.EnumerateFiles(directory, stem + ".*" + extension)
            .Select(file => Path.GetFileName(file)[(stem.Length + 1)..^extension.Length])
            .Where(IsCultureName)
            .ToArray();
    }

    /// <summary>Whether the runtime knows the name as a culture ("it", "fr-FR"). A name it does not is not a culture file.</summary>
    private static bool IsCultureName(string name)
    {
        if (name.Length == 0) return false;
        try
        {
            _ = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return true;
        }
        catch (Exception exception) when (exception is CultureNotFoundException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<EditorDocumentViewModel?> FindCultureBufferAsync(string path)
    {
        if (CachedCultureBuffer(path) is { } found) return found;
        // Only a file that exists is opened; a missing one is created on the first edit instead.
        var open = _getCode ?? _openCode;
        if (open is null || !File.Exists(path)) return null;
        return await open(path);
    }

    /// <summary>
    /// The culture buffer the host already holds. With a find hook the host's open documents decide, so a closed
    /// buffer is never written to; without one, the buffers this view has used answer.
    /// </summary>
    private EditorDocumentViewModel? CachedCultureBuffer(string path) => _findCode is null
        ? _cultureBuffers.GetValueOrDefault(path)
        : _findCode(path);

    /// <summary>
    /// Wires the culture .resx the way <c>BindResourceEditing</c> wires the neutral one. Each callback names the
    /// culture its edit was made in: the session may have moved to another language by the time an undo runs,
    /// and that undo must still reach the file it changed.
    /// </summary>
    private void BindCultureResourceEditing(WinFormsDesignerSession session)
    {
        session.LoadCultureResources = culture => LoadCultureResourcesAsync(session, culture);
        session.ListCultureResources = CultureResourceNames;
        session.PrepareCultureResourceEdit = async (culture, expected, replacement) =>
        {
            var path = CulturePath(culture);
            var buffer = await EditableCultureBufferAsync(path, replacement);
            // A file created just now already holds the replacement; an existing one must still hold the expected text.
            if (_disposed || !ReferenceEquals(session, _session)
                || (buffer.Text != expected && buffer.Text != replacement))
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            _cultureBuffers[path] = buffer;
        };
        // Verified for every culture file before any of them is written, so a stale buffer stops the edit before a write.
        session.VerifyCultureResourceSource = (culture, expected, replacement) =>
        {
            var buffer = CachedCultureBuffer(CulturePath(culture));
            if (_disposed || !ReferenceEquals(session, _session) || buffer is null
                || (buffer.Text != expected && buffer.Text != replacement))
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
        };
        session.CommitCultureResourceSource = (culture, expected, replacement) =>
        {
            var path = CulturePath(culture);
            var buffer = CachedCultureBuffer(path);
            if (_disposed || !ReferenceEquals(session, _session) || buffer is null)
                throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
            if (buffer.Text != replacement)
            {
                if (buffer.Text != expected)
                    throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeChanged));
                buffer.Text = replacement;
            }
            _cultureBuffers[path] = buffer;
        };
    }

    /// <summary>
    /// The buffer a culture edit writes: the open one, else the existing file opened from disk. A file that
    /// exists is never recreated, because creating it would replace the translation with an empty resource. A file
    /// that is gone is created holding <paramref name="replacement"/>, the text the edit writes; only an edit that
    /// writes a value reaches this point, so no culture file is created for a no-op.
    /// </summary>
    private async Task<EditorDocumentViewModel> EditableCultureBufferAsync(string path, string replacement)
    {
        if (CachedCultureBuffer(path) is { } open) return open;
        if (File.Exists(path))
        {
            var getDocument = _getCode ?? _openCode
                ?? throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
            return await getDocument(path);
        }
        return await CreateCultureBufferAsync(path, replacement);
    }

    private async Task<EditorDocumentViewModel> CreateCultureBufferAsync(string path, string text)
    {
        if (_createCode is null)
            throw new InvalidOperationException(Localizer.Get(StringKeys.WinFormsDesignerCodeUnavailable));
        return await _createCode(path, text);
    }
}
