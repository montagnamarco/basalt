using System.Globalization;

namespace System.Windows.Forms.Internal;

/// <summary>Which standard button a caption is for; None stands for a custom, non-standard button.</summary>
internal enum StandardButton
{
    None, OK, Cancel, Abort, Retry, Ignore, Yes, No, TryAgain, Continue, Close, Help,
}

/// <summary>
/// The caption Windows shows on a standard button, in the user's language;
/// English for languages not listed here. Shared by MessageBoxForm (keyed from
/// a DialogResult) and TaskDialogButton (keyed from its own standard buttons),
/// so both show the same word for "Cancel".
/// </summary>
internal static class StandardButtonCaptions
{
    public static string For(StandardButton button)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var table = Captions.TryGetValue(language, out var found) ? found : Captions["en"];

        return table[button];
    }

    private static readonly Dictionary<string, Dictionary<StandardButton, string>> Captions = new()
    {
        ["en"] = new()
        {
            [StandardButton.OK] = "OK", [StandardButton.Cancel] = "Cancel", [StandardButton.Abort] = "&Abort",
            [StandardButton.Retry] = "&Retry", [StandardButton.Ignore] = "&Ignore", [StandardButton.Yes] = "&Yes",
            [StandardButton.No] = "&No", [StandardButton.TryAgain] = "&Try Again", [StandardButton.Continue] = "&Continue",
            [StandardButton.Close] = "Close", [StandardButton.Help] = "Help",
        },
        ["it"] = new()
        {
            [StandardButton.OK] = "OK", [StandardButton.Cancel] = "Annulla", [StandardButton.Abort] = "&Interrompi",
            [StandardButton.Retry] = "&Riprova", [StandardButton.Ignore] = "I&gnora", [StandardButton.Yes] = "&Sì",
            [StandardButton.No] = "&No", [StandardButton.TryAgain] = "&Riprova", [StandardButton.Continue] = "&Continua",
            [StandardButton.Close] = "Chiudi", [StandardButton.Help] = "Guida",
        },
        ["de"] = new()
        {
            [StandardButton.OK] = "OK", [StandardButton.Cancel] = "Abbrechen", [StandardButton.Abort] = "&Abbrechen",
            [StandardButton.Retry] = "&Wiederholen", [StandardButton.Ignore] = "&Ignorieren", [StandardButton.Yes] = "&Ja",
            [StandardButton.No] = "&Nein", [StandardButton.TryAgain] = "&Wiederholen", [StandardButton.Continue] = "&Weiter",
            [StandardButton.Close] = "Schließen", [StandardButton.Help] = "Hilfe",
        },
        ["fr"] = new()
        {
            [StandardButton.OK] = "OK", [StandardButton.Cancel] = "Annuler", [StandardButton.Abort] = "&Abandonner",
            [StandardButton.Retry] = "&Recommencer", [StandardButton.Ignore] = "&Ignorer", [StandardButton.Yes] = "&Oui",
            [StandardButton.No] = "&Non", [StandardButton.TryAgain] = "&Réessayer", [StandardButton.Continue] = "&Continuer",
            [StandardButton.Close] = "Fermer", [StandardButton.Help] = "Aide",
        },
        ["es"] = new()
        {
            [StandardButton.OK] = "Aceptar", [StandardButton.Cancel] = "Cancelar", [StandardButton.Abort] = "&Anular",
            [StandardButton.Retry] = "&Reintentar", [StandardButton.Ignore] = "&Omitir", [StandardButton.Yes] = "&Sí",
            [StandardButton.No] = "&No", [StandardButton.TryAgain] = "&Volver a intentarlo", [StandardButton.Continue] = "&Continuar",
            [StandardButton.Close] = "Cerrar", [StandardButton.Help] = "Ayuda",
        },
    };
}
