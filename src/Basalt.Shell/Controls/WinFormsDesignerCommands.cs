using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

/// <summary>
/// A command the Designer, Format and Edit menus, or their shortcuts, give the WinForms designer. The menus name the same
/// commands for the XAML designer too; this enum is what the WinForms designer answers.
/// </summary>
public enum WinFormsDesignerCommand
{
    AlignLeft,
    AlignCenter,
    AlignRight,
    AlignTop,
    AlignMiddle,
    AlignBottom,
    SameWidth,
    SameHeight,
    SameSize,
    SpaceHorizontally,
    SpaceVertically,
    CenterHorizontally,
    CenterVertically,
    BringToFront,
    SendToBack,
    LockControls,
    SelectParent,
    SelectAll,
    Delete,
    ZoomIn,
    ZoomOut,
    ZoomReset,
    ZoomToFit,
    ToggleRulers,
}

/// <summary>
/// The one place that decides where a designer command goes: to the WinForms designer when it is in front, and otherwise
/// to the caller's own fallback, the XAML designer or the code editor. It also builds the Format menu, which only the
/// WinForms designer has.
/// </summary>
internal static class WinFormsDesignerCommands
{
    /// <summary>Runs the command on the WinForms designer if one is in front. False means the caller must run its own.</summary>
    internal static bool TryRun(WinFormsDesignerView? designer, WinFormsDesignerCommand command)
    {
        if (designer is null) return false;

        designer.RunDesignerCommand(command);
        return true;
    }

    /// <summary>
    /// The Format menu. Each entry runs on the WinForms designer in front; with another document in front it does nothing,
    /// as the menu has no other designer to format.
    /// </summary>
    internal static IReadOnlyList<MenuEntry> FormatEntries(Func<WinFormsDesignerView?> designer)
    {
        MenuEntry Item(string key, WinFormsDesignerCommand command) =>
            new(Localizer.Get(key), Action: () => TryRun(designer(), command));

        return
        [
            new(Localizer.Get(StringKeys.MenuFormatAlign), Children:
            [
                Item(StringKeys.WinFormsDesignerAlignLeft, WinFormsDesignerCommand.AlignLeft),
                Item(StringKeys.WinFormsDesignerAlignCenter, WinFormsDesignerCommand.AlignCenter),
                Item(StringKeys.WinFormsDesignerAlignRight, WinFormsDesignerCommand.AlignRight),
                MenuEntry.Separator,
                Item(StringKeys.WinFormsDesignerAlignTop, WinFormsDesignerCommand.AlignTop),
                Item(StringKeys.WinFormsDesignerAlignMiddle, WinFormsDesignerCommand.AlignMiddle),
                Item(StringKeys.WinFormsDesignerAlignBottom, WinFormsDesignerCommand.AlignBottom),
            ]),
            new(Localizer.Get(StringKeys.MenuFormatMakeSameSize), Children:
            [
                Item(StringKeys.MenuFormatMakeSameWidth, WinFormsDesignerCommand.SameWidth),
                Item(StringKeys.MenuFormatMakeSameHeight, WinFormsDesignerCommand.SameHeight),
                Item(StringKeys.MenuFormatMakeSameBoth, WinFormsDesignerCommand.SameSize),
            ]),
            new(Localizer.Get(StringKeys.MenuFormatHorizontalSpacing), Children:
            [
                Item(StringKeys.MenuFormatMakeEqual, WinFormsDesignerCommand.SpaceHorizontally),
            ]),
            new(Localizer.Get(StringKeys.MenuFormatVerticalSpacing), Children:
            [
                Item(StringKeys.MenuFormatMakeEqual, WinFormsDesignerCommand.SpaceVertically),
            ]),
            new(Localizer.Get(StringKeys.MenuFormatCenterInForm), Children:
            [
                Item(StringKeys.MenuFormatCenterHorizontally, WinFormsDesignerCommand.CenterHorizontally),
                Item(StringKeys.MenuFormatCenterVertically, WinFormsDesignerCommand.CenterVertically),
            ]),
            MenuEntry.Separator,
            new(Localizer.Get(StringKeys.MenuFormatOrder), Children:
            [
                Item(StringKeys.DesignerBringToFront, WinFormsDesignerCommand.BringToFront),
                Item(StringKeys.DesignerSendToBack, WinFormsDesignerCommand.SendToBack),
            ]),
            MenuEntry.Separator,
            Item(StringKeys.MenuFormatLockControls, WinFormsDesignerCommand.LockControls),
        ];
    }
}
