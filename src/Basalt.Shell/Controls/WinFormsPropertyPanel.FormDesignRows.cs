using Basalt.Core.Localization;
using Basalt.Designer;

namespace Basalt.Shell.Controls;

/// <summary>
/// The Localizable and Language rows of the root form. The designer session owns both values, and the
/// grid only shows them and reports edits back, so the rows are built here instead of from the form's
/// TypeDescriptor properties.
/// </summary>
/// <param name="IsLocalizable">Whether the form resolves its values through ApplyResources.</param>
/// <param name="Culture">The culture the surface shows, or null for "(Default)".</param>
/// <param name="Cultures">The culture names offered by Language; the current culture is always among them.</param>
/// <param name="SetLocalizable">Receives the value chosen for Localizable.</param>
/// <param name="SetLanguage">Receives the culture chosen for Language, or null for "(Default)".</param>
public sealed record WinFormsFormDesignRows(
    bool IsLocalizable,
    string? Culture,
    IReadOnlyList<string> Cultures,
    Action<bool> SetLocalizable,
    Action<string?> SetLanguage);

public sealed partial class WinFormsPropertyPanel
{
    /// <summary>The grid row name of Localizable, as Visual Studio's Properties window spells it.</summary>
    internal const string LocalizableRowName = "Localizable";

    /// <summary>The grid row name of Language.</summary>
    internal const string LanguageRowName = "Language";

    private WinFormsFormDesignRows? _formRows;

    private static string DefaultLanguageText => Localizer.Get(StringKeys.WinFormsDesignerLanguageDefault);

    /// <summary>The two Design rows, which replace any property of the same name the form itself reports.</summary>
    private static IReadOnlyList<DesignableProperty> FormDesignProperties(WinFormsFormDesignRows rows) =>
    [
        new DesignableProperty(LocalizableRowName, typeof(bool), PropertyEditorKind.Boolean, PropertyCategory.Design,
            [], rows.IsLocalizable ? bool.TrueString : bool.FalseString)
        {
            DisplayName = Localizer.Get(StringKeys.WinFormsDesignerLocalizable),
            DefaultValue = bool.FalseString,
            // Only the neutral language can move values in and out of the code, so a culture freezes the switch.
            IsReadOnly = rows.Culture is not null,
            IsModifiedOverride = rows.IsLocalizable,
            CanResetOverride = false,
        },
        new DesignableProperty(LanguageRowName, typeof(string), PropertyEditorKind.Enumeration, PropertyCategory.Design,
            [DefaultLanguageText, .. rows.Cultures], rows.Culture ?? DefaultLanguageText)
        {
            DisplayName = Localizer.Get(StringKeys.WinFormsDesignerLanguage),
            DefaultValue = DefaultLanguageText,
            // Language is design-time only and means nothing until the form is localizable.
            IsReadOnly = !rows.IsLocalizable,
            IsModifiedOverride = rows.Culture is not null,
            CanResetOverride = false,
        },
    ];

    /// <summary>
    /// Sends an edit of a Design row to the session. Returns false for every other row, which keeps the
    /// control's own edit path. A Localizable value that does not parse is dropped rather than sent to the
    /// form's own properties, where no such property exists.
    /// </summary>
    private bool TryEditFormDesignRow(string name, string? value)
    {
        if (_formRows is not { } rows) return false;
        if (name == LocalizableRowName)
        {
            if (bool.TryParse(value, out var localizable)) rows.SetLocalizable(localizable);
            return true;
        }

        if (name == LanguageRowName)
        {
            rows.SetLanguage(value is null || value == DefaultLanguageText ? null : value);
            return true;
        }

        return false;
    }

    private static bool IsFormDesignRowName(string name) =>
        name is LocalizableRowName or LanguageRowName;
}
