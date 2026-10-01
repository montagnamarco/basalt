// The enumerations of this namespace, with the names and values of Microsoft's
// System.Windows.Forms.Primitives reference assembly. The values are the contract: they are
// compiled into the applications that use them, stored in .resx files and written
// by the designer as numbers. Regenerate with docs/planning/winforms-phase0/apigen
// instead of editing by hand.

namespace System.Windows.Forms.Automation;

public enum AutomationLiveSetting
{
    Off = 0,
    Polite = 1,
    Assertive = 2,
}

public enum AutomationNotificationKind
{
    ItemAdded = 0,
    ItemRemoved = 1,
    ActionCompleted = 2,
    ActionAborted = 3,
    Other = 4,
}

public enum AutomationNotificationProcessing
{
    ImportantAll = 0,
    ImportantMostRecent = 1,
    All = 2,
    MostRecent = 3,
    CurrentThenMostRecent = 4,
}
