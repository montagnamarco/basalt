using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// Project > Add Window… dialog: its text must be English (routed through
/// the localizer, not hardcoded Italian) and its buttons must follow the
/// Windows/Visual Studio order: the primary action first, Cancel after it,
/// the pair right-aligned.
/// </summary>
public sealed class NewWindowDialogTests
{
    /// <summary>Words that only appear in Italian, and only as user-visible text.</summary>
    private static readonly string[] Italian =
    [
        "finestra", "Nuova", "Nome", "della", "classe", "facoltativo",
        "Spazio dei nomi", "Titolo", "mostrato", "nella barra",
        "Indicare", "trattini bassi", "Annulla",
    ];

    [AvaloniaFact]
    public void HasNoHardcodedItalianText()
    {
        var dialog = new NewWindowDialog(Path.GetTempPath(), SourceLanguage.VisualBasic);

        var texts = new[]
        {
            dialog.Title,
            dialog.FindControl<TextBlock>("ErrorLabel")?.Text,
            (dialog.FindControl<Button>("CreateButton")?.Content as string),
            (dialog.FindControl<Button>("CancelButton")?.Content as string),
        };

        foreach (var text in texts)
        {
            if (text is null) continue;

            foreach (var word in Italian)
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [AvaloniaFact]
    public void PutsTheCreateButtonBeforeCancelRightAligned()
    {
        var dialog = new NewWindowDialog(Path.GetTempPath(), SourceLanguage.VisualBasic);

        var panel = dialog.FindControl<StackPanel>("ButtonPanel")!;
        var create = dialog.FindControl<Button>("CreateButton")!;
        var cancel = dialog.FindControl<Button>("CancelButton")!;

        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, panel.HorizontalAlignment);

        var order = panel.Children.ToList();
        Assert.True(
            order.IndexOf(create) < order.IndexOf(cancel),
            "The primary action (Create) must come before Cancel, matching Windows dialog order.");

        Assert.True(create.IsDefault, "Enter must trigger the primary action.");
        Assert.True(cancel.IsCancel, "Esc must dismiss the dialog.");
    }
}
